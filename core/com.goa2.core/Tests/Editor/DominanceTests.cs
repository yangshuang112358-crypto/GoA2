using System.IO;
using System.Linq;
using Goa2.Domain;
using Goa2.Infrastructure;
using Goa2.Rules;
using NUnit.Framework;
using static Goa2.Tests.TurnFlowTests;
namespace Goa2.Tests
{
 public sealed class DominanceTests
 {
  internal const string Card="shargatha-10-独霸一方";
  [Test] public void ExactTextAndGate()
  {var c=BattlefieldTests.Catalog().Card(Card);Assert.That(CombatRules.HasPrimaryProgram(c),Is.True);Assert.That(CombatRules.HasPrimaryProgram(c,87),Is.False);c.Text+="所有英雄";Assert.That(CombatRules.HasPrimaryProgram(c),Is.False);}
  [TestCase(false)] [TestCase(true)] public void IncludesProtectedHeavyWithoutChangingActualTeamOrProtection(bool dual)
  {
   var cat=BattlefieldTests.Catalog();var g=CharmTests.Planning(cat,Card);var s=new JsonStateCodec().Read(g.ExportSave());s.Units.Single(u=>u.Id==CharmTests.Heavy).Position=new Hex(4,-9);s.Units.Single(u=>u.Id==CharmTests.Ranged).Position=new Hex(2,-8);
   if(dual){s.Units.Single(u=>u.Seat==3).Position=new Hex(5,-9);s.Effects.Add(new ActiveEffect{SourceUnitId="hero:3",SourceCardId="sabina-16-战斗武装",ControllerSeat=3,Kind=EffectKind.FriendlyAttackMinionsDual,Window=EffectTimeline.Create(s.Round,s.Turn,4,EffectDuration.ThisRound)});}
   var a=CharmTests.DebugMath(cat,s);Assert.That(a.EnemySupportSources,Is.Empty);Assert.That(a.FriendlyGuardSources,Does.Contain(CharmTests.Melee));Assert.That(a.FriendlyGuardSources.Contains(CharmTests.Heavy),Is.EqualTo(dual));Assert.That(s.Units.Single(u=>u.Id==CharmTests.Heavy).Team,Is.EqualTo(Team.Red));Assert.That(GameRules.LegalMinionRemovals(s),Does.Not.Contain(CharmTests.Heavy));
  }
  [Test] public void AllyStillReceivesEnemySupport()
  {var cat=BattlefieldTests.Catalog();var g=CharmTests.Planning(cat,Card);var s=new JsonStateCodec().Read(g.ExportSave());s.Units.Single(u=>u.Id==CharmTests.Heavy).Position=new Hex(5,-9);var a=CharmTests.DebugMath(cat,s,2);Assert.That(a.EnemySupportSources,Does.Contain(CharmTests.Heavy));}
  [Test] public void NewestEffectStillUsesSourceLifecycle()
  {var cat=BattlefieldTests.Catalog();var g=CharmTests.Planning(cat,Card);var s=new JsonStateCodec().Read(g.ExportSave());Apply(g,0,CommandKind.DebugDefeatHero,"hero:0",target:1);Assert.That(g.View(0).Effects.Any(e=>e.SourceCardId==Card),Is.False);ChargeTests.Restore(cat,g);}
  [Test] public void Old87DefenseKeepsExactBytes()
  {var cat=BattlefieldTests.Catalog();string save=File.ReadAllText(Path.Combine(ContentTests.Root(),"tests/fixtures/engine87-dominion-defense.json"));var g=LocalGameFactory.Restore(cat,save);Assert.That(g.ExportSave(),Is.EqualTo(save));Assert.That(g.View(0).SupportedPrimaryCards,Does.Not.Contain(Card));}
 }
}
