using System.IO;
using System.Linq;
using Goa2.Domain;
using Goa2.Infrastructure;
using Goa2.Rules;
using NUnit.Framework;
using static Goa2.Tests.TurnFlowTests;
using static Goa2.Tests.PetrificationTests;
namespace Goa2.Tests
{
 public sealed class PetrificationGazeTests
 {
  internal const string Gaze="shargatha-16-石化凝视";
  [Test] public void ExactTextAndGate(){var c=BattlefieldTests.Catalog().Card(Gaze);Assert.That(CombatRules.HasPrimaryProgram(c),Is.True);Assert.That(CombatRules.HasPrimaryProgram(c,96),Is.False);c.Text+="最近";Assert.That(CombatRules.HasPrimaryProgram(c),Is.False);}
  [Test] public void AllEnemiesInRangeRegardlessOfDistance()
  {var cat=BattlefieldTests.Catalog();var g=Cast(cat,Gaze);var s=State(g);Hero(s,3).Position=new Hex(6,-9);Assert.That(PetrificationRules.Targets(s),Is.EquivalentTo(new[]{"hero:1","hero:3"}));Assert.That(EffectRules.CanAffect(s,1,Hero(s,2)),Is.True);Assert.That(PetrificationRules.TerrainCells(s),Is.EquivalentTo(new[]{Hero(s,1).Position,Hero(s,3).Position}));ChargeTests.Restore(cat,g);}
  [Test] public void ImmuneNearestDoesNotShieldFartherEnemiesInAllMode()
  {var cat=BattlefieldTests.Catalog();var s=State(Cast(cat,Gaze));Hero(s,3).Position=new Hex(6,-9);s.Effects.Add(new ActiveEffect{SourceUnitId="hero:1",Kind=EffectKind.ImmunityAndUnitTraversal,Window=EffectTimeline.Create(s.Round,s.Turn,4,EffectDuration.ThisTurn)});Assert.That(PetrificationRules.Targets(s),Is.EquivalentTo(new[]{"hero:3"}));Assert.That(Reach(cat,s,Hero(s,1),2),Is.Not.Empty);}
  [Test] public void RadiusBoundaryAndPassiveRemainDynamic()
  {var cat=BattlefieldTests.Catalog();var s=State(Cast(cat,Gaze));Hero(s,3).Position=new Hex(7,-9);Assert.That(PetrificationRules.Targets(s),Is.EquivalentTo(new[]{"hero:1"}));s.Players[0].RangeBonus=1;Assert.That(PetrificationRules.Targets(s),Is.EquivalentTo(new[]{"hero:1","hero:3"}));s.Players[0].RangeBonus=0;Assert.That(PetrificationRules.Targets(s),Is.EquivalentTo(new[]{"hero:1"}));}
  [Test] public void OutsideStartCompletesWholeMovementThenBecomesStone()
  {var cat=BattlefieldTests.Catalog();var s=State(Cast(cat,Gaze));Hero(s,1).Position=new Hex(7,-9);Assert.That(Straight(cat,s,Hero(s,1),2).Any(m=>m.Destination==new Hex(5,-9)),Is.True);Hero(s,1).Position=new Hex(5,-9);Assert.That(Reach(cat,s,Hero(s,1),2),Is.Empty);}
  [Test] public void OwnPlacementWorksWhileOtherUnitDisplacementAndAttackFail()
  {var cat=BattlefieldTests.Catalog();var g=Cast(cat,Gaze);var s=State(g);Assert.That(EffectRules.CanDisplace(cat,s,0,Hero(s,3)),Is.False);Assert.That(EffectRules.CanDisplace(cat,s,3,Hero(s,3)),Is.True);Assert.That(EffectRules.CanBeAttacked(s,Hero(s,2),Hero(s,3),true),Is.False);OpportuneMomentTests.AdvanceTo(g,3);Apply(g,3,CommandKind.BeginPrimary);Apply(g,3,CommandKind.ChoosePlacement,cell:new Hex(1,-8));ChargeTests.Restore(cat,g);}
  [Test] public void TerrainStopsUnitTraversalPushButNotPhantasmTraversal()
  {var cat=BattlefieldTests.Catalog();var s=State(Cast(cat,Gaze));s.Units.RemoveAll(u=>!u.Seat.HasValue);Hero(s,0).Position=new Hex(4,-8);Hero(s,1).Position=new Hex(6,-8);Hero(s,2).Position=new Hex(5,-8);Hero(s,3).Position=new Hex(3,-9);s.Effects.Add(new ActiveEffect{SourceUnitId="hero:2",Kind=EffectKind.ImmunityAndUnitTraversal,Window=EffectTimeline.Create(s.Round,s.Turn,4,EffectDuration.ThisTurn)});var pushed=PushRules.AwayFromAdjacent(cat,s,Hero(s,0),Hero(s,2),2);Assert.That(pushed.Path.Last(),Is.EqualTo(new Hex(5,-8)));Assert.That(pushed.StopReason,Is.EqualTo("obstacle"));s.Players[2].HeroId="shargatha";s.Players[2].PurpleCardId="shargatha-12-幻化";pushed=PushRules.AwayFromAdjacent(cat,s,Hero(s,0),Hero(s,2),2);Assert.That(pushed.Path.Last(),Is.EqualTo(new Hex(7,-8)));}
  [Test] public void NonHeroUnitsNeverBecomeStone()
  {var cat=BattlefieldTests.Catalog();var s=State(Cast(cat,Gaze));var m=s.Units.First(u=>u.Kind=="melee");m.Position=new Hex(4,-9);Assert.That(PetrificationRules.Targets(s),Does.Not.Contain(m.Id));Assert.That(EffectRules.CanAffect(s,0,m),Is.True);}
  [Test] public void TurnExpiryAndSourceDefeatClearAllTraits()
  {var cat=BattlefieldTests.Catalog();var g=Cast(cat,Gaze);Apply(g,0,CommandKind.DebugAdvance,"turn");Assert.That(g.View(null).Players.Any(p=>p.IsPetrified),Is.False);g=Cast(cat,Gaze);Apply(g,0,CommandKind.DebugDefeatHero,"hero:0",target:1);Assert.That(g.View(null).Players.Any(p=>p.IsPetrified),Is.False);ChargeTests.Restore(cat,g);}
  [Test] public void Old96PendingPlacementBytesUnchanged(){var cat=BattlefieldTests.Catalog();var save=File.ReadAllText(Path.Combine(ContentTests.Root(),"tests/fixtures/engine96-petrify-eye-placement.json"));var g=LocalGameFactory.Restore(cat,save);Assert.That(g.ExportSave(),Is.EqualTo(save));Assert.That(g.View(0).SupportedPrimaryCards,Does.Not.Contain(Gaze));}
 }
}
