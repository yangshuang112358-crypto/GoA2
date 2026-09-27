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
 public sealed class PetrificationEyeTests
 {
  internal const string Eye="shargatha-14-石化之眼";
  [Test] public void ExactTextAndGate(){var c=BattlefieldTests.Catalog().Card(Eye);Assert.That(CombatRules.HasPrimaryProgram(c),Is.True);Assert.That(CombatRules.HasPrimaryProgram(c,95),Is.False);c.Text+="所有";Assert.That(CombatRules.HasPrimaryProgram(c),Is.False);}
  [Test] public void IncludesAllTiedNearestHeroesDespiteMissingParenthesis()
  {var cat=BattlefieldTests.Catalog();var g=Cast(cat,Eye);Assert.That(g.View(null).Players.Where(p=>p.IsPetrified).Select(p=>p.Seat),Is.EquivalentTo(new[]{1,3}));ChargeTests.Restore(cat,g);}
  [Test] public void RadiusThreeAndRangePassiveAreLive()
  {var cat=BattlefieldTests.Catalog();var s=State(Cast(cat,Eye));Hero(s,1).Position=new Hex(6,-9);Hero(s,3).Position=new Hex(7,-9);Assert.That(PetrificationRules.Targets(s),Is.EquivalentTo(new[]{"hero:1"}));Hero(s,1).Position=new Hex(7,-8);Assert.That(PetrificationRules.Targets(s),Is.Empty);s.Players[0].RangeBonus=1;Assert.That(PetrificationRules.Targets(s),Is.EquivalentTo(new[]{"hero:1","hero:3"}));}
  [Test] public void ImmuneNearestStillOccupiesNearestSlot()
  {var cat=BattlefieldTests.Catalog();var s=State(Cast(cat,Eye));Hero(s,3).Position=new Hex(5,-8);s.Effects.Add(new ActiveEffect{SourceUnitId="hero:1",Kind=EffectKind.ImmunityAndUnitTraversal,Window=EffectTimeline.Create(s.Round,s.Turn,4,EffectDuration.ThisTurn)});Assert.That(PetrificationRules.Targets(s),Is.Empty);}
  [Test] public void SourceMovementImmediatelyChangesTargets()
  {var cat=BattlefieldTests.Catalog();var g=Cast(cat,Eye);Apply(g,0,CommandKind.DebugTeleport,"hero:0",cell:new Hex(5,-8));Assert.That(g.View(null).Players.Where(p=>p.IsPetrified).Select(p=>p.Seat),Is.EquivalentTo(new[]{1}));ChargeTests.Restore(cat,g);}
  [Test] public void WholeStraightMovementFinishesBeforeNewStone()
  {var cat=BattlefieldTests.Catalog();var s=State(Cast(cat,Eye));Hero(s,1).Position=new Hex(6,-8);Hero(s,2).Position=new Hex(6,-7);Hero(s,3).Position=new Hex(3,-6);Assert.That(Straight(cat,s,Hero(s,1),2).Any(m=>m.Destination==new Hex(4,-8)),Is.True);Hero(s,1).Position=new Hex(4,-8);Assert.That(Reach(cat,s,Hero(s,1),3),Is.Empty);}
  [Test] public void SourceDefeatCancelsUpgradedAura(){var cat=BattlefieldTests.Catalog();var g=Cast(cat,Eye);Apply(g,0,CommandKind.DebugDefeatHero,"hero:0",target:1);Assert.That(g.View(null).Players.Any(p=>p.IsPetrified),Is.False);ChargeTests.Restore(cat,g);}
  [Test] public void Old95PendingPlacementBytesUnchanged(){var cat=BattlefieldTests.Catalog();var save=File.ReadAllText(Path.Combine(ContentTests.Root(),"tests/fixtures/engine95-petrify-placement.json"));var g=LocalGameFactory.Restore(cat,save);Assert.That(g.ExportSave(),Is.EqualTo(save));Assert.That(g.View(0).SupportedPrimaryCards,Does.Not.Contain(Eye));}
 }
}
