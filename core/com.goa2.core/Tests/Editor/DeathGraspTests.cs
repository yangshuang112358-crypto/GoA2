using System.IO;
using System.Linq;
using Goa2.Domain;
using Goa2.Infrastructure;
using Goa2.Rules;
using NUnit.Framework;
using static Goa2.Tests.TurnFlowTests;
namespace Goa2.Tests
{
 public sealed class DeathGraspTests
 {
  internal const string Card="shargatha-11-死亡缠绕";
  [Test] public void ExactTextAndGate(){var c=BattlefieldTests.Catalog().Card(Card);Assert.That(CombatRules.HasPrimaryProgram(c),Is.True);Assert.That(CombatRules.HasPrimaryProgram(c,93),Is.False);c.Text+="红色";Assert.That(CombatRules.HasPrimaryProgram(c),Is.False);}
  [TestCase("tigerclaw-00-瞬闪打击",true)] [TestCase("tigerclaw-02-偷袭",false)] [TestCase("tigerclaw-08-偷天妙手",false)] [TestCase("tigerclaw-18-躲闪",false)] public void GoldIsTheOnlyMovementException(string played,bool allowed)
  {var cat=BattlefieldTests.Catalog();var g=BindingTests.Next(cat,played,Card);Assert.That(g.View(1).SecondaryMoves.Count>0,Is.EqualTo(allowed));ChargeTests.Restore(cat,g);}
  [Test] public void RedAttackStillWorksButItsAfterAttackMovementIsPrevented()
  {var cat=BattlefieldTests.Catalog();var g=BindingTests.Next(cat,"tigerclaw-02-偷袭",Card);Apply(g,1,CommandKind.BeginPrimary);Apply(g,1,CommandKind.ChooseAttackTarget,"hero:0");Apply(g,0,CommandKind.Defend,"shargatha-13-石化");Assert.That(g.View(1).Units.Single(u=>u.Seat==1).Position,Is.EqualTo(new Hex(4,-8)));Assert.That(g.View(1).Pending?.Kind,Is.Not.EqualTo("effect_move"));Assert.That(g.View(1).Events.Any(e=>e.Kind=="AttackDeclared"),Is.True);ChargeTests.Restore(cat,g);}
  [Test] public void MovingAwayImmediatelyRestoresRedMovement()
  {var cat=BattlefieldTests.Catalog();var g=BindingTests.Next(cat,"tigerclaw-02-偷袭",Card);Apply(g,0,CommandKind.DebugTeleport,"hero:0",cell:new Hex(2,-8));Assert.That(g.View(1).SecondaryMoves,Is.Not.Empty);ChargeTests.Restore(cat,g);}
  [Test] public void DefeatingTheSourceRestoresTheSameRedAttacksAfterMove()
  {var cat=BattlefieldTests.Catalog();var g=BindingTests.Next(cat,"tigerclaw-02-偷袭",Card);Apply(g,1,CommandKind.BeginPrimary);Apply(g,1,CommandKind.ChooseAttackTarget,"hero:0");Apply(g,0,CommandKind.DeclineDefense);Assert.That(g.View(1).Effects.Any(e=>e.SourceCardId==Card),Is.False);Assert.That(g.View(1).Pending?.Kind,Is.EqualTo("effect_move"));Assert.That(g.View(1).EffectMoves,Is.Not.Empty);ChargeTests.Restore(cat,g);}
  [Test] public void Old93BindingPendingBytesUnchanged(){var cat=BattlefieldTests.Catalog();var save=File.ReadAllText(Path.Combine(ContentTests.Root(),"tests/fixtures/engine93-binding-defense.json"));var g=LocalGameFactory.Restore(cat,save);Assert.That(g.ExportSave(),Is.EqualTo(save));Assert.That(g.View(0).SupportedPrimaryCards,Does.Not.Contain(Card));}
 }
}
