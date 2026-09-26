using System.IO;
using System.Linq;
using Goa2.Application;
using Goa2.Domain;
using Goa2.Infrastructure;
using Goa2.Rules;
using NUnit.Framework;
using static Goa2.Tests.SessionTests;
using static Goa2.Tests.TurnFlowTests;
namespace Goa2.Tests
{
 public sealed class RisingTideTests
 {
  internal const string Card="arien-03-潮起";
  internal static GameSession Ready(ContentCatalog cat)
  {var g=SurgeTests.Ready(cat,Card);Apply(g,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(0,-3));return g;}
  [Test] public void ExactContractAndVersion()
  {var c=BattlefieldTests.Catalog().Card(Card);Assert.That(c.PrimaryValue,Is.EqualTo(5));Assert.That(c.SubtypeValue,Is.EqualTo(2));Assert.That(CombatRules.HasPrimaryProgram(c),Is.True);Assert.That(CombatRules.HasPrimaryProgram(c,75),Is.False);c.Text+="最多";Assert.That(CombatRules.HasPrimaryProgram(c),Is.False);}
  [Test] public void RangeTwoAttackLeavesOnlyAdjacentEnemyForFixedPush()
  {
   var cat=BattlefieldTests.Catalog();var g=Ready(cat);SurgeTests.Attack(g);Assert.That(g.View(0).EffectTargets,Is.EqualTo(new[]{"hero:3"}));g=ChargeTests.Restore(cat,g);
   Apply(g,0,CommandKind.ChooseEffectTarget,"hero:3");Assert.That(g.View(0).Units.Single(u=>u.Seat==1).Position,Is.EqualTo(new Hex(0,-3)));
   Assert.That(g.View(0).Units.Single(u=>u.Seat==3).Position,Is.EqualTo(new Hex(-2,-3)));ChargeTests.Restore(cat,g);
  }
  [Test] public void OnlyRangedBonusExtendsAttackBeyondTwo()
  {
   var cat=BattlefieldTests.Catalog();var g=Ready(cat);Apply(g,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(0,-2));var s=new JsonStateCodec().Read(g.ExportSave());
   s.Players[0].RangeBonus=8;Assert.That(CombatRules.AttackTargets(cat,s,0),Does.Not.Contain("hero:1"));s.Players[0].RangedBonus=1;Assert.That(CombatRules.AttackTargets(cat,s,0),Does.Contain("hero:1"));
  }
  [Test] public void DefeatedDistantHeroDoesNotRemoveOtherPushOption()
  {
   var cat=BattlefieldTests.Catalog();var g=Ready(cat);SurgeTests.Attack(g,false);Assert.That(g.View(0).EffectTargets,Is.EqualTo(new[]{"hero:3"}));
   Apply(g,0,CommandKind.ChooseEffectTarget,"skip");Assert.That(g.View(0).Events.Count(e=>e.Kind=="CardResolved" && e.CardId==Card),Is.EqualTo(1));ChargeTests.Restore(cat,g);
  }
  [Test] public void SecondaryMovementDoesNotOfferAttackOrPush()
  {
   var cat=BattlefieldTests.Catalog();var g=Ready(cat);Apply(g,0,CommandKind.Move,cell:g.View(0).SecondaryMoves.First().Destination);
   Assert.That(g.View(0).Events.Any(e=>e.Kind=="UnitPushed" || e.Kind=="AttackCalculated"),Is.False);ChargeTests.Restore(cat,g);
  }
  [Test] public void Old75PushChoiceKeepsBytesAndCapabilities()
  {
   var cat=BattlefieldTests.Catalog();string save=File.ReadAllText(Path.Combine(ContentTests.Root(),"tests/fixtures/engine75-surge-push-choice.json"));var g=LocalGameFactory.Restore(cat,save);
   Assert.That(g.ExportSave(),Is.EqualTo(save));Assert.That(g.View(0).SupportedPrimaryCards,Does.Not.Contain(Card));
  }
 }
}
