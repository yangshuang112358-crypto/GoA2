using System;
using System.IO;
using System.Linq;
using Goa2.Domain;
using Goa2.Infrastructure;
using Goa2.Infrastructure.Scenarios;
using NUnit.Framework;
using UnityEngine;
namespace Goa2.UI3D.Tests
{
    public sealed class ActionSequenceTests
    {
        private ContentCatalog catalog;
        [SetUp] public void Setup()=>catalog=ContentLoader.LoadDirectory(Path.Combine(UnityEngine.Application.streamingAssetsPath,"Goa2"));
        private ScenarioRunner Runner(string name)=>new ScenarioRunner(catalog,ScenarioRunner.Load(File.ReadAllText(Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath,"../../tests/scenarios/"+name+".json")))));
        private static void Steps(ScenarioRunner runner,int count){for(int i=0;i<count;i++)Assert.That(runner.Next().Passed,Is.True,"Fixture step "+i);}
        [Test] public void RealDefenseDiscardIsNestedAndIdenticalForAllSeats()
        {
            var r=Runner("throwing-axe-reflection");Steps(r,9);
            Assert.That(r.Session.View(0).ActionSequence.Cards.Count,Is.EqualTo(4));
            Steps(r,7);var before=r.Session.View(0).ActionSequence;
            Assert.That(before.Cards.Any(c=>c.Role=="defense"),Is.False,"No speculative response before commitment");
            Steps(r,1);var v=r.Session.View(0).ActionSequence;
            var d=v.Cards.Single(c=>c.Role=="defense");var parent=v.Cards.Single(c=>c.Id==d.ParentId);
            Assert.That(d.CardId,Is.EqualTo("wasp-10-反射屏障"));Assert.That(parent.CardId,Is.EqualTo("brogan-02-投掷飞斧"));
            Steps(r,2);v=r.Session.View(0).ActionSequence;
            var pay=v.Cards.Single(c=>c.CardId=="brogan-06-铜墙铁壁");Assert.That(pay.ParentId,Is.EqualTo(d.Id));
            Assert.That(v.Cards.Count(c=>c.CardId==d.CardId),Is.EqualTo(1),"Defense discard is not duplicated");
            var json=JsonUtility.ToJson(v);
            for(int seat=0;seat<4;seat++)Assert.That(JsonUtility.ToJson(r.Session.View(seat).ActionSequence),Is.EqualTo(json));
            Assert.That(JsonUtility.ToJson(r.Session.View(null).ActionSequence),Is.EqualTo(json));
        }
        [Test] public void HiddenSelectionsNeverGenerateMainCards()
        {
            var r=Runner("throwing-axe-reflection");Steps(r,8);
            for(int seat=0;seat<4;seat++)Assert.That(r.Session.View(seat).ActionSequence.Cards,Is.Empty);
        }
        [Test] public void RestoreReconstructsSameTraceAndFollowingPlanningKeepsIt()
        {
            var r=Runner("throwing-axe-reflection");Steps(r,19);
            var before=r.Session.View(0).ActionSequence;
            var restored=LocalGameFactory.Restore(catalog,r.Session.ExportSave());
            Assert.That(JsonUtility.ToJson(restored.View(2).ActionSequence),Is.EqualTo(JsonUtility.ToJson(before)));
            Steps(r,1);var next=r.Session.View(0).ActionSequence;
            Assert.That(next.Id,Is.EqualTo(before.Id));Assert.That(next.Cards.Select(c=>c.Id),Is.EquivalentTo(before.Cards.Select(c=>c.Id)));
            Assert.That(next.Cards.Where(c=>c.IsMain).All(c=>c.Resolved),Is.True);
        }
        [Test] public void NestedCounterattacksAreDistinctRootsAndDefensesRemainChildren()
        {
            var r=Runner("counterattack-nested");Steps(r,25);var v=r.Session.View(0).ActionSequence;
            var replies=v.Cards.Where(n=>n.Role=="reaction").ToList();Assert.That(replies.Count,Is.EqualTo(2));
            Assert.That(replies.All(n=>n.ParentId==""&&n.Resolved&&!n.IsMain),Is.True);
            Assert.That(replies[0].Id,Is.Not.EqualTo(replies[1].Id));
            var shield=v.Cards.Single(n=>n.Role=="defense"&&n.CardId=="wasp-08-偏转屏障");Assert.That(shield.ParentId,Is.EqualTo(replies[0].Id));
            Assert.That(v.Cards.Single(n=>n.CardId=="shargatha-13-石化").ParentId,Is.EqualTo(shield.Id));
        }
        [Test] public void DefensePreludeCreatesCommittedDefenseThenPurpleThenDiscard()
        {
            var r=Runner("phantasm-defense");Steps(r,23);var v=r.Session.View(2).ActionSequence;
            var d=v.Cards.Single(n=>n.Role=="defense");var u=v.Cards.Single(n=>n.Role=="ultimate");
            Assert.That(u.ParentId,Is.EqualTo(d.Id));Assert.That(d.Results,Does.Not.Contain("已弃置"));
            Steps(r,2);v=r.Session.View(3).ActionSequence;
            Assert.That(v.Cards.Count(n=>n.Role=="defense"),Is.EqualTo(1));
            Assert.That(v.Cards.Single(n=>n.Role=="discard"&&n.Seat==1).ParentId,Is.EqualTo(u.Id));
            Assert.That(v.Cards.Single(n=>n.Id==d.Id).Results,Does.Contain("已弃置"));
        }
        [Test] public void AttackAfterMoveDoesNotBecomeAChildOfOrdinaryDefense()
        {
            var r=Runner("counterattack-moved");while(!r.Complete)Assert.That(r.Next().Passed,Is.True);
            var v=r.Session.View(0).ActionSequence;
            Assert.That(v.Cards.Where(n=>n.Role=="defense").SelectMany(n=>n.Results).Any(x=>x.StartsWith("移动至")),Is.False);
            Assert.That(v.Cards.Where(n=>n.IsMain).SelectMany(n=>n.Results).Any(x=>x.StartsWith("移动至")),Is.True);
        }
        [TestCase("cloak-repeat")][TestCase("phantasm-primary")][TestCase("counterattack-defense")]
        [TestCase("thunderstorm-pay")][TestCase("poison-dagger-ally")][TestCase("tidal-wave-counterattack")]
        public void RealFixturesHaveStableAcyclicPublicTrace(string scenario)
        {
            var r=Runner(scenario);
            while(!r.Complete)
            {
                Assert.That(r.Next().Passed,Is.True);var v=r.Session.View(0).ActionSequence;
                Assert.That(v.Cards.Select(n=>n.Id).Distinct().Count(),Is.EqualTo(v.Cards.Count));
                var seen=new System.Collections.Generic.HashSet<string>();
                foreach(var n in v.Cards){Assert.That(n.ParentId==""||seen.Contains(n.ParentId),Is.True,"Parent must precede child");seen.Add(n.Id);Assert.That(catalog.Cards.Any(c=>c.Id==n.CardId),Is.True);}
                Assert.That(JsonUtility.ToJson(r.Session.View(3).ActionSequence),Is.EqualTo(JsonUtility.ToJson(v)));
            }
        }
        [Test] public void GroupingExcludesStartedResponsesAndExtraRoots()
        {
            var a=new ActionCardView{Initiative=7};var b=new ActionCardView{Initiative=7};
            Assert.That(Goa2.Presentation.UI3D.ActionSequenceRail.Grouped(a,b),Is.True);
            a.Started=true;Assert.That(Goa2.Presentation.UI3D.ActionSequenceRail.Grouped(a,b),Is.False);
            a.Started=false;b.Role="reaction";Assert.That(Goa2.Presentation.UI3D.ActionSequenceRail.Grouped(a,b),Is.False);
            b.Role="main";b.Initiative=6;Assert.That(Goa2.Presentation.UI3D.ActionSequenceRail.Grouped(a,b),Is.False);
        }
        [Test] public void RouletteCuesAreShort(){foreach(var cue in new[]{"tick","move","drop","insert"})Assert.That(Goa2.Presentation.UI3D.ActionSequenceAudio.Duration(cue),Is.InRange(.05f,.23f));}
        [Test] public void FourWayPreviewUsesAlreadyFlippedCoinAndHonorsActualCaptainChoice()
        {
            var r=Runner("action-sequence-four-tie");Steps(r,6);var v=r.Session.View(0);
            Assert.That(v.ActionSequence.Cards.Select(n=>n.Seat),Is.EqualTo(new[]{0,1,2,3}));
            Steps(r,1);v=r.Session.View(0);Assert.That(v.ActionSequence.Cards.Select(n=>n.Seat),Is.EqualTo(new[]{2,1,0,3}));
            Assert.That(v.ActionSequence.Cards[0].Started,Is.True);Assert.That(v.ActionSequence.Cards.Skip(1).All(n=>!n.Started),Is.True);
            Steps(r,2);v=r.Session.View(0);Assert.That(v.ActionSequence.Cards.Select(n=>n.Seat),Is.EqualTo(new[]{2,3,0,1}));
        }
    }
}
