using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using Goa2.Application;
using Goa2.Domain;
using Goa2.Infrastructure;
using Goa2.Rules;
using NUnit.Framework;
using static Goa2.Tests.SessionTests;
using static Goa2.Tests.TurnFlowTests;
namespace Goa2.Tests
{
 public sealed class PetrificationTests
 {
  internal const string Card="shargatha-13-石化";
  internal static GameSession Ready(ContentCatalog cat,string card=Card,string broganCard="brogan-06-铜墙铁壁")
  {
   var g=LocalGameFactory.Create(cat,"stone",new[]{"A","B","C","D"},42,true);Apply(g,0,CommandKind.DebugPrepare,"shargatha,wasp,brogan,arien");Apply(g,0,CommandKind.DebugEquipCard,card,target:0);if(broganCard!="brogan-06-铜墙铁壁")Apply(g,0,CommandKind.DebugEquipCard,broganCard,target:2);
   var pos=new[]{new Hex(3,-8),new Hex(4,-8),new Hex(6,-8),new Hex(3,-9)};for(int i=0;i<4;i++)Apply(g,0,CommandKind.DebugTeleport,"hero:"+i,cell:pos[i]);
   var cards=new[]{card,"wasp-07-抵挡屏障",broganCard,"arien-07-潮水"};for(int i=0;i<4;i++)Apply(g,i,CommandKind.SelectCard,cards[i]);OpportuneMomentTests.AdvanceTo(g,0);return g;
  }
  internal static GameSession Cast(ContentCatalog cat,string card=Card){var g=Ready(cat,card);Apply(g,0,CommandKind.BeginPrimary);return g;}
  internal static GameState State(GameSession g)=>new JsonStateCodec().Read(g.ExportSave());
  internal static UnitState Hero(GameState s,int seat)=>s.Units.Single(u=>u.Seat==seat);
  internal static List<MoveOption> Reach(ContentCatalog c,GameState s,UnitState u,int n)=> (List<MoveOption>)typeof(MovementRules).GetMethod("Reachable",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{c,s,u,n,null});
  internal static List<MoveOption> Straight(ContentCatalog c,GameState s,UnitState u,int n)=> (List<MoveOption>)typeof(MovementRules).GetMethod("StraightExact",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{c,s,u,n,null,null});
  [Test] public void ExactTextAndGate(){var c=BattlefieldTests.Catalog().Card(Card);Assert.That(CombatRules.HasPrimaryProgram(c),Is.True);Assert.That(CombatRules.HasPrimaryProgram(c,94),Is.False);c.Text+="更远";Assert.That(CombatRules.HasPrimaryProgram(c),Is.False);}
  [Test] public void TiedNearestEnemiesBecomeImmuneButRemainHeroes()
  {var cat=BattlefieldTests.Catalog();var g=Cast(cat);var s=State(g);foreach(int seat in new[]{1,3}){Assert.That(EffectRules.CanAffect(s,0,Hero(s,seat)),Is.False);Assert.That(Hero(s,seat).Kind,Is.EqualTo("hero"));Assert.That(EffectRules.CanAffect(s,seat,Hero(s,seat)),Is.True);}Assert.That(EffectRules.CanAffect(s,1,Hero(s,2)),Is.True);ChargeTests.Restore(cat,g);}
  [Test] public void RangeAndNearestAreRecomputedFromCurrentPositions()
  {var cat=BattlefieldTests.Catalog();var s=State(Cast(cat));Hero(s,3).Position=new Hex(5,-8);Assert.That(EffectRules.CanAffect(s,0,Hero(s,3)),Is.True);Hero(s,1).Position=new Hex(6,-9);Assert.That(EffectRules.CanAffect(s,0,Hero(s,3)),Is.False);Hero(s,3).Position=new Hex(6,-8);Assert.That(EffectRules.CanAffect(s,0,Hero(s,3)),Is.True);s.Players[0].RangeBonus=1;Assert.That(EffectRules.CanAffect(s,0,Hero(s,3)),Is.False);}
  [Test] public void ExistingImmuneNearestDoesNotRedirectToFartherHero()
  {var cat=BattlefieldTests.Catalog();var s=State(Cast(cat));Hero(s,3).Position=new Hex(5,-8);s.Effects.Add(new ActiveEffect{SourceUnitId="hero:1",Kind=EffectKind.ImmunityAndUnitTraversal,Window=EffectTimeline.Create(s.Round,s.Turn,4,EffectDuration.ThisTurn)});Assert.That(Reach(cat,s,Hero(s,1),2),Is.Not.Empty);Assert.That(EffectRules.CanAffect(s,0,Hero(s,3)),Is.True);Hero(s,3).Position=new Hex(3,-9);Assert.That(EffectRules.CanAffect(s,0,Hero(s,3)),Is.False);}
  [Test] public void AttackOnlyImmunityDoesNotPreventStoneSkill()
  {var cat=BattlefieldTests.Catalog();var s=State(Cast(cat));s.Effects.Add(new ActiveEffect{SourceUnitId="hero:1",Kind=EffectKind.AttackActionImmunity,Window=EffectTimeline.Create(s.Round,s.Turn,4,EffectDuration.ThisTurn)});Assert.That(Reach(cat,s,Hero(s,1),2),Is.Empty);}
  [Test] public void MovementDeniedButOwnPlacementSkillStillExecutes()
  {var cat=BattlefieldTests.Catalog();var g=Cast(cat);OpportuneMomentTests.AdvanceTo(g,3);Assert.That(g.View(3).SecondaryMoves,Is.Empty);Assert.That(g.View(3).FastMoves,Is.Empty);var before=g.ExportSave();Assert.That(g.Execute(3,Cmd(g,3,CommandKind.Move,destination:new Hex(4,-9))).Accepted,Is.False);Assert.That(g.ExportSave(),Is.EqualTo(before));Apply(g,3,CommandKind.BeginPrimary);Assert.That(g.View(3).Placements,Is.Not.Empty);ChargeTests.Restore(cat,g);Apply(g,3,CommandKind.ChoosePlacement,cell:g.View(3).Placements.First());}
  [Test] public void InternalAndRequiredStraightMovementAreDenied()
  {var cat=BattlefieldTests.Catalog();var s=State(Cast(cat));Assert.That(Reach(cat,s,Hero(s,1),5),Is.Empty);Assert.That(Straight(cat,s,Hero(s,1),2),Is.Empty);Assert.That(EffectRules.CanDisplace(cat,s,2,Hero(s,1)),Is.False);Assert.That(EffectRules.CanBeAttacked(s,Hero(s,0),Hero(s,1),false),Is.False);}
  [Test] public void CompleteCurrentMovementBeforeStoneRecalculation()
  {var cat=BattlefieldTests.Catalog();var s=State(Cast(cat));Hero(s,1).Position=new Hex(6,-9);Hero(s,3).Position=new Hex(3,-6);var mover=Hero(s,1);var end=new Hex(3,-9);Assert.That(Reach(cat,s,mover,3).Any(m=>m.Destination==end),Is.True);mover.Position=end;Assert.That(Reach(cat,s,mover,3),Is.Empty);Assert.That(EffectRules.CanAffect(s,0,Hero(s,3)),Is.True);}
  [Test] public void StraightMoveCanCrossNewNearestPointWithoutBeingCutShort()
  {var cat=BattlefieldTests.Catalog();var s=State(Cast(cat));s.Units.RemoveAll(u=>!u.Seat.HasValue || u.Seat==2);Hero(s,1).Position=new Hex(6,-8);Hero(s,3).Position=new Hex(3,-6);Assert.That(Straight(cat,s,Hero(s,1),2).Any(m=>m.Destination==new Hex(4,-8)),Is.True);Hero(s,1).Position=new Hex(4,-8);Assert.That(Reach(cat,s,Hero(s,1),2),Is.Empty);}
  [Test] public void UnitTraversalCannotCrossStoneTerrainButPhantasmCan()
  {var cat=BattlefieldTests.Catalog();var s=State(Cast(cat));s.Units.RemoveAll(u=>!u.Seat.HasValue);Hero(s,0).Position=new Hex(5,-9);Hero(s,1).Position=new Hex(5,-8);Hero(s,2).Position=new Hex(6,-8);Hero(s,3).Position=new Hex(3,-9);s.Effects.Add(new ActiveEffect{SourceUnitId="hero:2",Kind=EffectKind.ImmunityAndUnitTraversal,Window=EffectTimeline.Create(s.Round,s.Turn,4,EffectDuration.ThisTurn)});Assert.That(Straight(cat,s,Hero(s,2),2).Any(m=>m.Destination==new Hex(4,-8)),Is.False);
   Hero(s,0).Position=new Hex(6,-8);Hero(s,2).Position=new Hex(6,-9);s.Players[0].Level=8;s.Players[0].PurpleCardId="shargatha-12-幻化";Assert.That(Straight(cat,s,Hero(s,0),2).Any(m=>m.Destination==new Hex(4,-8)),Is.True);Assert.That(Reach(cat,s,Hero(s,0),3).Any(m=>m.Destination==new Hex(5,-8)),Is.False);
  }
  [Test] public void StoneStillBlocksFastMoveFromEnemyRegion()
  {var cat=BattlefieldTests.Catalog();var g=Cast(cat);var s=State(g);s.ActiveSeat=2;Hero(s,2).Position=cat.Cells.First(c=>!c.Obstacle && c.Region==cat.Cell(Hero(s,1).Position).Region && !s.Units.Any(u=>u.Position==c.Position)).Position;s.Players[2].Cards.Single(c=>c.Zone==CardZone.PlayedUnresolved).CardId="brogan-00-猛攻";Assert.That(MovementRules.LegalMoves(cat,s,2,MoveMode.Fast),Is.Empty);var team=Hero(s,2).Team;s.Units.RemoveAll(u=>u.Team!=team);Assert.That(MovementRules.LegalMoves(cat,s,2,MoveMode.Fast),Is.Not.Empty);}
  [Test] public void PetrifiedTigerclawRetainsImmunePurpleTriggerButCannotMove()
  {var cat=BattlefieldTests.Catalog();var s=State(Cast(cat));s.Players[1].HeroId="tigerclaw";s.Players[1].PurpleCardId="tigerclaw-13-斗篷与匕首";Assert.That(UltimateRules.HasImmuneActionPrelude(cat,s,1),Is.True);Assert.That(Reach(cat,s,Hero(s,1),2),Is.Empty);}
  [Test] public void ExpiryAndSourceDefeatRestoreTargets()
  {var cat=BattlefieldTests.Catalog();var g=Cast(cat);Apply(g,0,CommandKind.DebugDefeatHero,"hero:0",target:1);Assert.That(EffectRules.CanAffect(State(g),2,Hero(State(g),1)),Is.True);ChargeTests.Restore(cat,g);g=Cast(cat);Apply(g,0,CommandKind.DebugAdvance,"turn");Assert.That(g.View(0).Effects.Any(e=>e.SourceCardId==Card),Is.False);}
  [Test] public void NoEnemiesStillCreatesExpiringAura()
  {var cat=BattlefieldTests.Catalog();var g=Ready(cat);Apply(g,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(6,-9));Apply(g,0,CommandKind.DebugTeleport,"hero:3",cell:new Hex(6,-7));Apply(g,0,CommandKind.BeginPrimary);Assert.That(g.View(0).Effects.Any(e=>e.SourceCardId==Card),Is.True);Assert.That(EffectRules.CanAffect(State(g),0,Hero(State(g),1)),Is.True);}
  [Test] public void Old94SaveUnchanged(){var cat=BattlefieldTests.Catalog();var save=File.ReadAllText(Path.Combine(ContentTests.Root(),"tests/fixtures/engine94-death-grasp-defense.json"));var g=LocalGameFactory.Restore(cat,save);Assert.That(g.ExportSave(),Is.EqualTo(save));Assert.That(g.View(0).SupportedPrimaryCards,Does.Not.Contain(Card));}
  [Test] public void ActualMovementCompletesThenUpdatesPublicTraits()
  {var cat=BattlefieldTests.Catalog();var g=Ready(cat);Apply(g,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(6,-9));Apply(g,0,CommandKind.DebugTeleport,"hero:3",cell:new Hex(3,-6));Apply(g,0,CommandKind.BeginPrimary);OpportuneMomentTests.AdvanceTo(g,1);Assert.That(g.View(null).Players.Single(p=>p.Seat==1).IsPetrified,Is.False);Apply(g,1,CommandKind.Move,cell:new Hex(4,-9));Assert.That(g.View(null).Players.Single(p=>p.Seat==1).IsPetrified,Is.True);Assert.That(g.View(null).Players.Single(p=>p.Seat==3).IsPetrified,Is.False);ChargeTests.Restore(cat,g);}
  [Test] public void SourceRetrievalCancelsAuraDuringSameTurn()
  {var cat=BattlefieldTests.Catalog();var g=Ready(cat,broganCard:"brogan-10-吟游诗人");Apply(g,0,CommandKind.DebugTeleport,"hero:2",cell:new Hex(4,-9));Apply(g,0,CommandKind.BeginPrimary);OpportuneMomentTests.AdvanceTo(g,2);Apply(g,2,CommandKind.BeginPrimary);Apply(g,2,CommandKind.ChooseEffectTarget,"hero:0");Apply(g,0,CommandKind.ChooseRecoveredCard,Card);Assert.That(g.View(0).Effects.Any(e=>e.SourceCardId==Card),Is.False);Assert.That(g.View(null).Players.Any(p=>p.IsPetrified),Is.False);ChargeTests.Restore(cat,g);}
  [Test] public void WrongActorAndDuplicateCastAreAtomic()
  {var cat=BattlefieldTests.Catalog();var g=Ready(cat);var before=g.ExportSave();Assert.That(g.Execute(1,Cmd(g,1,CommandKind.BeginPrimary)).Accepted,Is.False);Assert.That(g.ExportSave(),Is.EqualTo(before));var cmd=Cmd(g,0,CommandKind.BeginPrimary);Assert.That(g.Execute(0,cmd).Accepted,Is.True);before=g.ExportSave();Assert.That(g.Execute(0,cmd).Duplicate,Is.True);Assert.That(g.ExportSave(),Is.EqualTo(before));Assert.That(g.View(0).Effects.Count(e=>e.SourceCardId==Card),Is.EqualTo(1));}
 }
}
