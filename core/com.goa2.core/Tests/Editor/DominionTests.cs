using System.IO;
using System.Linq;
using Goa2.Domain;
using Goa2.Infrastructure;
using Goa2.Rules;
using NUnit.Framework;
using static Goa2.Tests.SessionTests;
using static Goa2.Tests.TurnFlowTests;
namespace Goa2.Tests
{
 public sealed class DominionTests
 {
  internal const string Card="shargatha-08-统治领域";
  [Test] public void ExactTextAndGate()
  {var c=BattlefieldTests.Catalog().Card(Card);Assert.That(c.SubtypeValue,Is.EqualTo(3));Assert.That(CombatRules.HasPrimaryProgram(c),Is.True);Assert.That(CombatRules.HasPrimaryProgram(c,86),Is.False);c.Text+="重型";Assert.That(CombatRules.HasPrimaryProgram(c),Is.False);}
  [Test] public void EnemyMeleeAndRangedChangeOnlyCasterDefenseButHeavyRemainsEnemy()
  {
   var cat=BattlefieldTests.Catalog();var g=CharmTests.Planning(cat,Card);Apply(g,0,CommandKind.DebugTeleport,CharmTests.Ranged,cell:new Hex(2,-8));Apply(g,0,CommandKind.DebugTeleport,CharmTests.Heavy,cell:new Hex(4,-9));var s=new JsonStateCodec().Read(g.ExportSave());var a=CharmTests.DebugMath(cat,s);
   Assert.That(a.FriendlyGuardSources,Is.EqualTo(new[]{CharmTests.Melee}));Assert.That(a.EnemySupportSources,Is.EqualTo(new[]{CharmTests.Heavy}));Assert.That(a.FinalAttack,Is.EqualTo(5));Assert.That(s.Units.Single(u=>u.Id==CharmTests.Ranged).Team,Is.EqualTo(Team.Red));
  }
  [Test] public void DoesNotGrantAllyTheSameOverride()
  {
   var cat=BattlefieldTests.Catalog();var g=CharmTests.Planning(cat,Card);var s=new JsonStateCodec().Read(g.ExportSave());var a=CharmTests.DebugMath(cat,s,2);Assert.That(a.EnemySupportSources,Does.Contain(CharmTests.Melee));Assert.That(a.FriendlyGuardSources,Does.Not.Contain(CharmTests.Melee));
  }
  [TestCase(false)] [TestCase(true)] public void DrillConvertsMeleeAndOnlyUnprotectedHeavyCanBeCharmed(bool heavyAlone)
  {
   var cat=BattlefieldTests.Catalog();var g=CharmTests.Planning(cat,Card);var s=new JsonStateCodec().Read(g.ExportSave());s.Units.Single(u=>u.Id==CharmTests.Heavy).Position=new Hex(5,-9);
   if(heavyAlone)s.Units.RemoveAll(u=>u.Team==Team.Red && u.Kind!="hero" && u.Id!=CharmTests.Heavy);
   s.Effects.Add(new ActiveEffect{SourceUnitId="hero:3",SourceCardId="sabina-14-战斗演练",ControllerSeat=3,Kind=EffectKind.FriendlyAttackMinionsRanged,Window=EffectTimeline.Create(s.Round,s.Turn,4,EffectDuration.ThisRound)});
   var a=CharmTests.DebugMath(cat,s);Assert.That(a.EnemySupportSources.Contains(CharmTests.Heavy),Is.EqualTo(!heavyAlone));Assert.That(a.EnemySupportSources,Does.Not.Contain(CharmTests.Melee));Assert.That(a.FriendlyGuardSources,Is.Empty);Assert.That(s.Units.Single(u=>u.Id==CharmTests.Heavy).Kind,Is.EqualTo("heavy"));
  }
  [Test] public void CannotConvertEvenUnprotectedHeavyWithoutARelevantCombatType()
  {
   var cat=BattlefieldTests.Catalog();var g=CharmTests.Planning(cat,Card);var s=new JsonStateCodec().Read(g.ExportSave());s.Units.RemoveAll(u=>u.Team==Team.Red && u.Kind!="hero" && u.Id!=CharmTests.Heavy);s.Units.Single(u=>u.Id==CharmTests.Heavy).Position=new Hex(4,-8);var a=CharmTests.DebugMath(cat,s);Assert.That(a.EnemySupportSources,Is.EqualTo(new[]{CharmTests.Heavy}));
  }
  [Test] public void Old86DefenseKeepsExactBytes()
  {var cat=BattlefieldTests.Catalog();string save=File.ReadAllText(Path.Combine(ContentTests.Root(),"tests/fixtures/engine86-charm-defense.json"));var g=LocalGameFactory.Restore(cat,save);Assert.That(g.ExportSave(),Is.EqualTo(save));Assert.That(g.View(0).SupportedPrimaryCards,Does.Not.Contain(Card));}
 }
}
