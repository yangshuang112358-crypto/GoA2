using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Goa2.Infrastructure.Scenarios;
using Newtonsoft.Json;
using Goa2.Domain;
using Goa2.Infrastructure;
using Goa2.Rules.Cards;
using NUnit.Framework;

namespace Goa2.Ai.Tests
{
    public sealed class SemanticsTests
    {
        private static ContentCatalog Catalog()
        {
            var d=new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while(d!=null && !File.Exists(Path.Combine(d.FullName,"content","manifest.json")))d=d.Parent;
            return ContentLoader.LoadDirectory(d!.FullName);
        }
        [Test]
        public void EveryStaticProgramFieldAndOpcodeRequiresSemanticReview()
        {
            var assembly=typeof(PublicCardSemantics).Assembly;
            foreach(var entry in new[]{("PrimaryProgram",PublicCardSemantics.PrimaryFields,new[]{"Id","Version","Instructions"}),("DefenseProgram",PublicCardSemantics.DefenseFields,new[]{"Id","Version"}),("UltimateProgram",PublicCardSemantics.UltimateFields,new[]{"Id","Version"})})
            {
                var fields=assembly.GetType("Goa2.Rules.Cards."+entry.Item1)!.GetFields().Select(f=>f.Name);
                Assert.That(entry.Item2.Concat(entry.Item3),Is.EquivalentTo(fields));
            }
            var steps=PublicStepSemantics.Describe();
            Assert.That(PublicEffectSemantics.Describe().Select(e=>e.Kind),Is.EquivalentTo(Enum.GetNames(typeof(EffectKind))));
            Assert.That(steps.Select(s=>s.Code).Distinct().Count(),Is.EqualTo(steps.Count));
            foreach(var c in Catalog().Cards)Assert.That(PublicCardSemantics.Describe(c,GameState.CurrentEngineVersion).Steps.Distinct(),Is.SubsetOf(steps.Select(s=>s.Code)));
        }
        [Test]
        public void AllScenarioHistoriesHaveExplicitSemanticHandlingForEverySeat()
        {
            var d=new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while(d!=null && !Directory.Exists(Path.Combine(d.FullName,"tests","scenarios")))d=d.Parent;
            var root=d!.FullName;var c=Catalog();var ids=new StableIds(c);var errors=new List<string>();var events=new HashSet<string>();var samples=new Dictionary<string,ObservedEvent>();int count=0;
            var output=Path.Combine(TestContext.CurrentContext.TestDirectory,"semantic-audit");Directory.CreateDirectory(output);
            foreach(var file in Directory.GetFiles(Path.Combine(root,"tests","scenarios"),"*.json").OrderBy(x=>x,StringComparer.Ordinal))
            {
                var definition=ScenarioRunner.Load(File.ReadAllText(file));definition.VerifyReplayAfterEachStep=false;
                var runner=new ScenarioRunner(c,definition);while(!runner.Complete)runner.Next();
                if(!runner.Report.Passed){errors.Add(Path.GetFileName(file)+": scenario did not pass");continue;}
                for(int seat=0;seat<4;seat++)
                {
                    var view=runner.Session.View(seat);
                    foreach(var e in view.Events.Where(e=>!e.Kind.StartsWith("Debug",StringComparison.Ordinal)))events.Add(e.Kind);
                    try
                    {
                        var observed=ObservationProjector.Project(view,seat,ids,PublicRuleProfile.From(c));count++;
                        foreach(var e in observed.PublicHistory.Concat(observed.OwnHistory))samples[e.Kind+"|"+e.Value+"|"+e.SecondaryValue+"|"+e.Attack?.Reason]=e;
                    }
                    catch(Exception e)
                    {
                        string key=Path.GetFileNameWithoutExtension(file)+"-"+seat;
                        errors.Add(key+": "+e.Message);
                        File.WriteAllText(Path.Combine(output,key+"-failure-save.json"),runner.Session.ExportSave());
                    }
                }
            }
            File.WriteAllText(Path.Combine(output,"report.json"),JsonConvert.SerializeObject(new{ProjectedSeatHistories=count,EventKinds=events.OrderBy(x=>x).ToArray(),Errors=errors},Formatting.Indented));
            File.WriteAllText(Path.Combine(output,"event-examples.json"),JsonConvert.SerializeObject(samples.Values,Formatting.Indented));
            Assert.That(errors,Is.Empty,string.Join("\n",errors.Take(25)));
        }
        [Test]
        public void AllCanonicalCardsHaveBoundPublicMechanicsAndUnknownTextIsRejected()
        {
            var catalog=Catalog();
            foreach(var c in catalog.Cards)
            {
                var description=PublicCardSemantics.Describe(c,GameState.CurrentEngineVersion);
                Assert.That(description.Steps,Is.Not.Empty,c.Id);
                Assert.That(description.Parameters.Select(x=>x.Key).Distinct().Count(),Is.EqualTo(description.Parameters.Count),c.Id);
            }
            var changed=catalog.Cards.First(); changed.Text+=" future rule";
            Assert.Throws<InvalidOperationException>(()=>PublicCardSemantics.Describe(changed,GameState.CurrentEngineVersion));
        }
        [Test]
        public void RepeatPoisonPetrifyAndUltimateSemanticsAreExplicit()
        {
            var catalog=Catalog();
            PublicCardMechanics Card(string id)=>PublicCardSemantics.Describe(catalog.Cards.Single(c=>c.Id.StartsWith(id+"-",StringComparison.Ordinal)),GameState.CurrentEngineVersion);
            Assert.That(Card("wasp-17").Parameters.Select(p=>p.Key),Does.Not.Contain("AttackSubtype"));
            Assert.That(Card("tigerclaw-12").Parameters.Select(p=>p.Key),Does.Not.Contain("PlacementTarget"));
            Assert.That(Card("wasp-17").Steps.Count(x=>x=="OptionalRepeatOrbitalMove"),Is.EqualTo(2));
            Assert.That(Card("tigerclaw-12").Constraints,Does.Contain("poison_nonstacking_round_expiry_source_independent"));
            Assert.That(Card("shargatha-14").Constraints,Does.Contain("nearest_before_immunity_all_ties"));
            Assert.That(Card("arien-16").Parameters.Single(p=>p.Key=="PersistImmunityThroughDefeat").Flag,Is.True);
            Assert.That(Card("tigerclaw-13").Constraints,Does.Contain("basic_attack_repeat_once_different_target_no_recursion"));
        }
        [Test]
        public void HistoricalAttackAndRestrictionReasonsAreProjectedWithoutOpaqueIds()
        {
            var c=Catalog();var s=LocalGameFactory.Create(c,"semantics",new[]{"a","b","c","d"},17,true);
            var v=s.View(0);v.PrimaryRestriction=c.Cards[0].Id;
            v.Events.Add(new GameEvent{Kind="AttackDeclared",Seat=0,CardId=c.Cards[0].Id,Detail="hero:1",AttackValues=new AttackBreakdown{AttackerSeat=0,DefenderSeat=1,TargetUnitId="hero:1",SourceCardId=c.Cards[0].Id,BaseAttack=3,FinalAttack=5}});
            v.Events.Add(new GameEvent{Kind="EffectNotScheduled",Seat=0,CardId=c.Cards[0].Id,Detail="next_turn_outside_round"});
            var o=ObservationProjector.Project(v,0,new StableIds(c),PublicRuleProfile.From(c));
            Assert.That(o.Restrictions.Single().Reason,Is.EqualTo("skill_suppressed"));
            Assert.That(o.PublicHistory[^2].Attack!.Final,Is.EqualTo(5));
            Assert.That(o.PublicHistory.Last().Value,Is.EqualTo("next_turn_outside_round"));
            v.Events.Add(new GameEvent{Kind="FuturePublicEvent",Detail="new fact"});
            Assert.Throws<InvalidOperationException>(()=>ObservationProjector.Project(v,0,new StableIds(c),PublicRuleProfile.From(c)));
        }
        [Test]
        public void PrivateHistoryAndEffectOpaqueNumbersCannotLeakAcrossSeats()
        {
            var c=Catalog();var s=LocalGameFactory.Create(c,"semantics",new[]{"a","b","c","d"},17,true);var v=s.View(0);
            v.Events.Add(new GameEvent{Kind="DefenseCalculated",Seat=0,PrivateTo=0,CardId=c.Cards[0].Id,Detail="3:5"});
            v.Events.Add(new GameEvent{Kind="DefenseCalculated",Seat=1,PrivateTo=1,CardId="foreign-private-card",Detail="999:999"});
            v.Events.Add(new GameEvent{Kind="EffectCreated",Seat=0,CardId=c.Cards[0].Id,Detail="effect:93012"});
            v.Events.Add(new GameEvent{Kind="EffectExpired",Seat=0,CardId=c.Cards[0].Id,Detail="effect:93012"});
            var o=ObservationProjector.Project(v,0,new StableIds(c),PublicRuleProfile.From(c));
            Assert.That(o.OwnHistory.Single().Amount,Is.EqualTo(3));Assert.That(o.PublicHistory.Last().EffectKey,Is.EqualTo(o.PublicHistory[^2].EffectKey));
            string before=JsonConvert.SerializeObject(o);
            foreach(var e in v.Events)e.Detail=e.Detail.Replace("effect:93012","effect:100000001");
            Assert.That(JsonConvert.SerializeObject(ObservationProjector.Project(v,0,new StableIds(c),PublicRuleProfile.From(c))),Is.EqualTo(before));
            Assert.That(before,Does.Not.Contain("foreign-private-card").And.Not.Contain("93012").And.Not.Contain("999"));
        }
    }
}
