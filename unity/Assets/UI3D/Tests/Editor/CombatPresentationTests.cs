using System;
using System.IO;
using System.Linq;
using Goa2.Application;
using Goa2.Domain;
using Goa2.Infrastructure;
using Goa2.Presentation.UI3D;
using NUnit.Framework;
using UnityEngine;
namespace Goa2.UI3D.Tests
{
    public sealed class CombatPresentationTests
    {
        private static GameView View()
        {
            var v=new GameView{MatchId="presentation",Round=1,Revision=1};
            for(int i=0;i<4;i++)v.Players.Add(new PlayerView{Seat=i,Team=i%2==0?Team.Blue:Team.Red,Level=1});
            v.Units.Add(new UnitState{Id="hero:0",Seat=0,Kind="hero",Team=Team.Blue,Position=new Hex(0,0)});
            v.Units.Add(new UnitState{Id="hero:1",Seat=1,Kind="hero",Team=Team.Red,Position=new Hex(1,0)});
            v.Units.Add(new UnitState{Id="bow",Kind="ranged",Team=Team.Blue,Position=new Hex(3,0)});
            return v;
        }
        private static void Event(GameView v,string kind,int? seat,string detail,string command="defend",Hex? from=null,AttackBreakdown attack=null)
        {v.Events.Add(new GameEvent{Sequence=v.Events.Count+1,CommandId=command,Kind=kind,Seat=seat,Detail=detail,From=from,AttackValues=attack});}
        [Test]public void DefenseDiscardPrecedesShotThenHeroAndAssistRewards()
        {
            var v=View();var fx=new CombatPresentationTimeline();fx.Observe(v,0);
            v.Revision++;Event(v,"AttackCalculated",0,"","attack",attack:new AttackBreakdown{AttackerSeat=0,DefenderSeat=1,TargetUnitId="hero:1"});fx.Observe(v,1);
            v.Revision++;Event(v,"DiscardColorShown",1,"blue");Event(v,"DefenseResolved",1,"failure");Event(v,"HeroDefeated",1,"by:0",from:new Hex(1,0));Event(v,"GoldAwarded",0,"3");Event(v,"AssistGoldAwarded",2,"1");v.Players[0].Gold=3;v.Players[2].Gold=1;v.Units.RemoveAll(u=>u.Id=="hero:1");fx.Observe(v,10);
            Assert.That(fx.Shots.Single().Start,Is.GreaterThanOrEqualTo(12.4f));Assert.That(fx.Shots[0].Support.Single().Id,Is.EqualTo("bow"));
            Assert.That(fx.Ghosts(v,10).Single().Id,Is.EqualTo("hero:1"));Assert.That(v.Units.Any(u=>u.Id=="hero:1"),Is.False,"Ghost never enters authoritative units");
            Assert.That(fx.Rewards.Select(r=>r.Amount),Is.EqualTo(new[]{3,1}));Assert.That(fx.Rewards.All(r=>r.Start==fx.Shots[0].Impact),Is.True);
            Assert.That(fx.VisibleGold(0,3,13),Is.Zero);Assert.That(v.Players[0].Gold,Is.EqualTo(3));Assert.That(fx.VisibleGold(0,3,18),Is.EqualTo(3));
            fx.Observe(v,11);Assert.That(fx.Shots.Count,Is.EqualTo(1));Assert.That(fx.Rewards.Count,Is.EqualTo(2));
            v.Units.Add(new UnitState{Id="hero:1",Seat=1,Kind="hero",Team=Team.Red,Position=new Hex(0,4)});
            Assert.That(fx.Ghosts(v,11).Single().Position,Is.EqualTo(new Hex(1,0)),"Immediate respawn elsewhere does not erase the original impact snapshot");
        }
        [Test]public void RewardsBindToTheirOwnDefeatAndDoNotReplayAfterReconnect()
        {
            var v=View();var fx=new CombatPresentationTimeline();fx.Observe(v,0);v.Revision++;
            Event(v,"MinionDefeated",0,"heavy-a","kill-a",new Hex(2,0));Event(v,"GoldAwarded",0,"4","kill-a");
            Event(v,"GoldAwarded",0,"9","unrelated");Event(v,"MinionDefeated",2,"melee-b","kill-b",new Hex(-2,0));Event(v,"GoldAwarded",2,"2","kill-b");fx.Observe(v,3);
            Assert.That(fx.Rewards.Count,Is.EqualTo(2));Assert.That(fx.Rewards[0].Origin,Is.Not.EqualTo(fx.Rewards[1].Origin));
            fx.Reset();fx.Observe(v,4);Assert.That(fx.Rewards,Is.Empty);Assert.That(fx.Shots,Is.Empty);
        }
        [Test]public void NewRoundFlushesPendingVisibleGoldWithoutChangingAuthority()
        {
            var v=View();var fx=new CombatPresentationTimeline();fx.Observe(v,0);v.Revision++;
            Event(v,"HeroDefeated",1,"by:0","kill",new Hex(1,0));Event(v,"GoldAwarded",0,"2","kill");fx.Observe(v,1);Assert.That(fx.VisibleGold(0,2,1),Is.Zero);
            v.Round=2;v.Revision++;fx.Observe(v,2);Assert.That(fx.VisibleGold(0,1,2),Is.EqualTo(1));Assert.That(fx.Rewards,Is.Empty);
        }
        [Test]public void DefenseMovementAndRepeatedAttackKeepIndependentSnapshots()
        {
            var v=View();var fx=new CombatPresentationTimeline();fx.Observe(v,0);v.Revision++;
            Event(v,"AttackCalculated",0,"","attack",attack:new AttackBreakdown{AttackerSeat=0,TargetUnitId="hero:1"});fx.Observe(v,1);
            Event(v,"UnitPlaced",1,"hero:1","defend",from:new Hex(1,0));v.Events.Last().To=new Hex(1,1);
            v.Units.Single(u=>u.Seat==1).Position=new Hex(1,1);Event(v,"DefenseResolved",1,"success");Event(v,"AttackResolved",0,"");
            Event(v,"AttackCalculated",1,"","counter",attack:new AttackBreakdown{AttackerSeat=1,TargetUnitId="hero:0"});Event(v,"DefenseResolved",0,"success","counter");v.Revision++;fx.Observe(v,2);
            Assert.That(fx.Shots.Count,Is.EqualTo(2));Assert.That(fx.Shots[0].Target.Position,Is.EqualTo(new Hex(1,1)));Assert.That(fx.Shots[1].Source.Position,Is.EqualTo(new Hex(1,1)));
            Assert.That(fx.Shots[1].Start,Is.GreaterThanOrEqualTo(fx.Shots[0].End));Assert.That(fx.Rewards,Is.Empty);
        }
        [Test]public void QueuedRangedAttacksEachGetAVisiblePreparationAndStableArrowSnapshot()
        {
            var v=View();var fx=new CombatPresentationTimeline();fx.Observe(v,0);
            Event(v,"AttackCalculated",0,"","first",attack:new AttackBreakdown{AttackerSeat=0,DefenderSeat=1,TargetUnitId="hero:1"});
            v.Revision++;fx.Observe(v,1);
            Event(v,"DefenseResolved",1,"success","first");Event(v,"AttackResolved",0,"","first");
            v.Revision++;fx.Observe(v,4);
            var first=fx.Shots.Single();
            Assert.That(first.PrepareStarted,Is.EqualTo(1),"Time already spent waiting for defense is retained");
            Assert.That(first.Start,Is.EqualTo(4),"An already prepared first shot does not wind up twice");
            for(int i=0;i<2;i++)
            {
                Event(v,"AttackCalculated",0,"","repeat-"+i,attack:new AttackBreakdown{AttackerSeat=0,DefenderSeat=1,TargetUnitId="hero:1"});
                Event(v,"DefenseResolved",1,"success","repeat-"+i);Event(v,"AttackResolved",0,"","repeat-"+i);
            }
            v.Revision++;fx.Observe(v,4.1f);
            Assert.That(fx.Shots.Count,Is.EqualTo(3));
            for(int i=1;i<fx.Shots.Count;i++)
            {
                var shot=fx.Shots[i];
                Assert.That(shot.PrepareStarted,Is.GreaterThanOrEqualTo(fx.Shots[i-1].End));
                Assert.That(shot.Start-shot.PrepareStarted,Is.GreaterThanOrEqualTo(1.8f-.0001f));
                Assert.That(shot.Release,Is.EqualTo(shot.Start+.12f));
                Assert.That(shot.Impact-shot.Release,Is.EqualTo(.5f).Within(.0001f));
                Assert.That(shot.End-shot.Start,Is.EqualTo(1.2f).Within(.0001f));
            }
            var arrow=new CombatPresentationTimeline.ArrowLaunch{Tail=new Vector3(1,2,3),Direction=Vector3.right,Length=.6f};
            first.Arrows.Add("bow",arrow);fx.Observe(v,4.2f);
            Assert.That(fx.Shots[0].Arrows["bow"],Is.SameAs(arrow),"Re-observing a projection must not resample the release socket");
            Assert.That(fx.Shots[1].Arrows,Is.Empty,"Repeated shots own separate release snapshots");
        }
        [TestCase(false)][TestCase(true)]
        public void DefenseBeforeMovementWaitsForNewBowsWithoutRewindingAlreadyDrawnBows(bool newBowJoins)
        {
            var v=View();
            v.Units.Add(new UnitState{Id="other-bow",Kind="ranged",Team=Team.Blue,Position=new Hex(newBowJoins?4:5,0)});
            var fx=new CombatPresentationTimeline();fx.Observe(v,0);
            Event(v,"AttackCalculated",0,"","attack",attack:new AttackBreakdown{AttackerSeat=0,DefenderSeat=1,TargetUnitId="hero:1"});
            v.Revision++;fx.Observe(v,1);
            var preparedBow=new ArcherMotion();preparedBow.Advance(1,true);preparedBow.Advance(9,true);
            Event(v,"UnitMoved",1,"hero:1","defense-before",from:new Hex(1,0));v.Events.Last().To=new Hex(2,0);
            v.Units.Single(u=>u.Id=="hero:1").Position=new Hex(2,0);
            Event(v,"DefenseResolved",1,"success");Event(v,"AttackResolved",0,"");
            v.Revision++;fx.Observe(v,10);
            var shot=fx.Shots.Single();
            Assert.That(shot.Support.Count,Is.EqualTo(newBowJoins?2:1));
            Assert.That(shot.PrepareStarted,Is.EqualTo(newBowJoins?10:1));
            Assert.That(shot.Start,Is.EqualTo(newBowJoins?11.8f:10).Within(.0001f),
                "Only a newly participating bow needs another visible preparation window");
            preparedBow.Advance(10,true,shot.Id,shot.Release);
            Assert.That(preparedBow.Progress,Is.EqualTo(1),"Delaying release does not rewind an existing bow's per-unit clock");
            Assert.That(preparedBow.Released,Is.False);
        }
        [Test]public void ResolvedAttackRetainedDuringAfterMoveCannotPrepareOrStrikeAgain()
        {
            var v=View();var fx=new CombatPresentationTimeline();fx.Observe(v,0);
            v.Attack=new AttackBreakdown{AttackerSeat=0,DefenderSeat=1,TargetUnitId="hero:1",SourceCardId="after-move"};
            v.Pending=new PendingChoice{Kind="defense",ChooserSeat=1,UnitId="hero:1"};
            Event(v,"AttackCalculated",0,"","attack",attack:v.Attack);v.Revision++;fx.Observe(v,1);
            Assert.That(CombatPresentationTimeline.IsPendingAttack(v),Is.True);
            Event(v,"DefenseResolved",1,"success");Event(v,"AttackResolved",0,"");
            v.Pending=new PendingChoice{Kind="effect_move",ChooserSeat=0,Source="after-move"};
            v.Revision++;fx.Observe(v,3);
            Assert.That(CombatPresentationTimeline.IsPendingAttack(v),Is.False);
            Assert.That(fx.Shots.Count,Is.EqualTo(1));
            fx.Observe(v,3.1f);
            Event(v,"HeroDefeated",1,"another-effect","unrelated",new Hex(1,0));v.Revision++;fx.Observe(v,3.2f);
            Assert.That(fx.Shots.Count,Is.EqualTo(1),"The retained Attack must not re-enter waiting and create a second strike");
            fx.Reset();fx.Observe(v,4);
            Event(v,"HeroDefeated",1,"another-effect","after-reconnect",new Hex(1,0));v.Revision++;fx.Observe(v,4.1f);
            Assert.That(fx.Shots,Is.Empty,"A reconnect on attack-after movement must not recover an already resolved strike");
        }
        [Test]public void PendingAttackSurvivesDefensePreludeButEndsBeforeDefenseAfterEffect()
        {
            var v=View();
            v.Attack=new AttackBreakdown{AttackerSeat=0,DefenderSeat=1,TargetUnitId="hero:1",SourceCardId="attack"};
            Event(v,"AttackCalculated",0,"","attack",attack:v.Attack);
            v.Pending=new PendingChoice{Kind="forced_discard",ChooserSeat=0,ResumeAt="before_action_discard"};
            Assert.That(CombatPresentationTimeline.IsPendingAttack(v),Is.True,"A defender's before-action purple ability has not resolved defense yet");
            Event(v,"DefenseResolved",1,"success");
            v.Pending=new PendingChoice{Kind="effect_move",ChooserSeat=1,ResumeAt="defense_response_move"};
            Assert.That(CombatPresentationTimeline.IsPendingAttack(v),Is.False,"Defense after-effects can pause before AttackResolved is emitted");
            Event(v,"AttackResolved",0,"");
            Event(v,"AttackCalculated",0,"","repeat",attack:v.Attack);
            v.Pending=new PendingChoice{Kind="defense",ChooserSeat=1,UnitId="hero:1"};
            Assert.That(CombatPresentationTimeline.IsPendingAttack(v),Is.True,"Repeating the same card against the same target has a new event boundary");
            v.Attack=null;Assert.That(CombatPresentationTimeline.IsPendingAttack(v),Is.False);
        }
        [Test]public void RealSneakAttackProjectionRetainsBreakdownWithoutRearmingAfterDefense()
        {
            var root=Directory.GetParent(UnityEngine.Application.dataPath).Parent.FullName;
            var catalog=ContentLoader.LoadDirectory(root);
            var game=LocalGameFactory.Create(catalog,"presentation-sneak",new[]{"A","B","C","D"},42,true);
            Apply(game,0,CommandKind.DebugPrepare,"tigerclaw,arien,brogan,wasp");
            Apply(game,0,CommandKind.DebugEquipCard,"arien-13-挑战者",target:1);
            Apply(game,0,CommandKind.DebugTeleport,"hero:0",cell:new Hex(7,-10));
            Apply(game,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(8,-10));
            for(int seat=0;seat<4;seat++)
            {
                string card=seat==0?"tigerclaw-02-偷袭":game.View(seat).OwnCards.Select(c=>catalog.Card(c.CardId))
                    .Where(c=>c.PrimaryFamily!="defense" && c.Color!="gold").OrderBy(c=>c.Initiative).ThenBy(c=>c.Id,StringComparer.Ordinal).First().Id;
                Apply(game,seat,CommandKind.SelectCard,card);
            }
            var fx=new CombatPresentationTimeline();fx.Observe(game.View(null),0);
            Apply(game,0,CommandKind.BeginPrimary);Apply(game,0,CommandKind.ChooseAttackTarget,"hero:1");
            fx.Observe(game.View(null),1);
            for(int seat=-1;seat<4;seat++)
                Assert.That(CombatPresentationTimeline.IsPendingAttack(game.View(seat<0?(int?)null:seat)),Is.True);
            Apply(game,1,CommandKind.Defend,"arien-13-挑战者");
            var after=game.View(null);string save=game.ExportSave();
            Assert.That(after.Attack,Is.Not.Null,"The actual core retains the breakdown through the attack-after move");
            Assert.That(after.Pending.Kind,Is.EqualTo("effect_move"));Assert.That(after.Pending.ChooserSeat,Is.EqualTo(0));
            for(int seat=-1;seat<4;seat++)
                Assert.That(CombatPresentationTimeline.IsPendingAttack(game.View(seat<0?(int?)null:seat)),Is.False);
            fx.Observe(after,4);Assert.That(fx.Shots.Count,Is.EqualTo(1));
            fx.Observe(after,4.1f);Assert.That(fx.Shots.Count,Is.EqualTo(1));
            var restored=LocalGameFactory.Restore(catalog,save);fx.Reset();fx.Observe(restored.View(null),5);
            Assert.That(fx.Shots,Is.Empty);Assert.That(CombatPresentationTimeline.IsPendingAttack(restored.View(null)),Is.False);
            Assert.That(game.ExportSave(),Is.EqualTo(save),"Presentation inspection cannot change rule state");
        }
        private static void Apply(GameSession game,int seat,CommandKind kind,string value="",int target=-1,Hex cell=default)
        {
            var view=game.View(seat);
            var result=game.Execute(seat,new Command{Id=Guid.NewGuid().ToString("N"),MatchId=view.MatchId,ExpectedRevision=view.Revision,
                ActorSeat=seat,Kind=kind,Value=value,TargetSeat=target,Destination=cell});
            Assert.That(result.Accepted,Is.True,kind+": "+result.Code+" "+result.Message);
        }
    }
}
