using System.IO;
using System.Linq;
using Goa2.Domain;
using Goa2.Infrastructure;
using Goa2.Rules;
using NUnit.Framework;
using static Goa2.Tests.TurnFlowTests;
namespace Goa2.Tests
{
 public sealed class FloodTests
 {
  internal const string Card="arien-17-洪水";
  [Test] public void ExactTextAndGate(){var c=BattlefieldTests.Catalog().Card(Card);Assert.That(CombatRules.HasPrimaryProgram(c),Is.True);Assert.That(CombatRules.HasPrimaryProgram(c,89),Is.False);c.Text+="叠加";Assert.That(CombatRules.HasPrimaryProgram(c),Is.False);}
  [Test] public void ActiveNowAndNextTurnThenExpires()
  {var cat=BattlefieldTests.Catalog();var g=SlipperyTests.Active(cat,card:Card);var e=g.View(0).Effects.Single();Assert.That(e.Window.StartTurn,Is.EqualTo(1));Assert.That(e.Window.EndTurn,Is.EqualTo(2));Assert.That(g.View(1).SecondaryMoves.All(m=>m.Path.Count<=2),Is.True);Apply(g,0,CommandKind.DebugAdvance,"turn");Assert.That(g.View(0).Effects.Single().SourceCardId,Is.EqualTo(Card));Apply(g,0,CommandKind.DebugSelectAll,"first");Apply(g,0,CommandKind.DebugAdvance,"turn");Assert.That(g.View(0).Turn,Is.EqualTo(3));Assert.That(g.View(0).Effects.Any(eff=>eff.SourceCardId==Card),Is.False);ChargeTests.Restore(cat,g);}
  [Test] public void OutsideStartStillAllowsEnteringAndInternalMoveUnaffected()
  {var cat=BattlefieldTests.Catalog();var g=SlipperyTests.Active(cat,card:Card);Apply(g,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(7,-8));Assert.That(g.View(1).SecondaryMoves.Any(m=>m.Path.Count>2 && m.Destination.Distance(new Hex(3,-8))<=3),Is.True);OpportuneMomentTests.AdvanceTo(g,3);Apply(g,3,CommandKind.BeginPrimary);Assert.That(g.View(3).EffectMoves.Any(m=>m.Path.Count==3),Is.True);ChargeTests.Restore(cat,g);}
  [Test] public void SourceDefeatCancelsBothTurns()
  {var cat=BattlefieldTests.Catalog();var g=SlipperyTests.Active(cat,card:Card);Apply(g,0,CommandKind.DebugDefeatHero,"hero:0",target:1);Assert.That(g.View(0).Effects.Any(e=>e.SourceCardId==Card),Is.False);ChargeTests.Restore(cat,g);}
  [TestCase(3)] [TestCase(4)] public void TwoTurnWindowNeverCrossesRound(int turn)
  {var cat=BattlefieldTests.Catalog();var g=SlipperyTests.Ready(cat,card:Card);var s=new JsonStateCodec().Read(g.ExportSave());s.Turn=turn;new GameRules().Apply(cat,s,new Command{ActorSeat=0,Kind=CommandKind.BeginPrimary});var e=s.Effects.Single();Assert.That(e.Window.StartTurn,Is.EqualTo(turn));Assert.That(e.Window.EndTurn,Is.EqualTo(System.Math.Min(4,turn+1)));Assert.That(e.Window.EndRound,Is.EqualTo(s.Round));}
  [Test] public void Old89PendingBytesUnchanged(){var cat=BattlefieldTests.Catalog();var save=File.ReadAllText(Path.Combine(ContentTests.Root(),"tests/fixtures/engine89-slippery-internal.json"));var g=LocalGameFactory.Restore(cat,save);Assert.That(g.ExportSave(),Is.EqualTo(save));Assert.That(g.View(0).SupportedPrimaryCards,Does.Not.Contain(Card));}
 }
}
