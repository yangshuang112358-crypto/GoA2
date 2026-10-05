using System.Linq;
using Goa2.Domain;
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
    }
}
