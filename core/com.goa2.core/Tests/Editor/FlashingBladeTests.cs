using System;
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
 public sealed class FlashingBladeTests
 {
  internal const string Blade="arien-00-华丽刀锋",Minion="minion:-1,-3";
  private static CommandKind Return => (CommandKind)Enum.Parse(typeof(CommandKind),"ChooseMinionReturn");
  internal static GameSession Setup(ContentCatalog cat,bool boundary=false)
  {
   var g=LocalGameFactory.Create(cat,"flashing",new[]{"A","B","C","D"},42,true);Apply(g,0,CommandKind.DebugPrepare,"arien,wasp,brogan,sabina");
   Apply(g,0,CommandKind.DebugTeleport,"hero:0",cell:new Hex(0,-2));Apply(g,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(0,-3));Apply(g,0,CommandKind.DebugTeleport,"hero:2",cell:new Hex(boundary?-2:-1,-2));Apply(g,0,CommandKind.DebugTeleport,"hero:3",cell:boundary?new Hex(-1,-2):new Hex(0,-4));
   if(boundary)Apply(g,0,CommandKind.DebugSetCoin,"red");
   string[] cards={Blade,boundary?"wasp-06-静电封锁":"wasp-07-抵挡屏障","brogan-06-铜墙铁壁","sabina-07-指挥"};for(int s=0;s<4;s++)Apply(g,s,CommandKind.SelectCard,cards[s]);if(boundary)Apply(g,1,CommandKind.BeginPrimary);Assert.That(g.View(0).ActiveSeat,Is.EqualTo(0));return g;
  }
  internal static void Target(GameSession g){Apply(g,0,CommandKind.BeginPrimary);Apply(g,0,CommandKind.ChooseAttackTarget,"hero:1");}
  [Test] public void ContractAndEngineGate()
  {var c=BattlefieldTests.Catalog().Card(Blade);Assert.That(c.PrimaryCategory,Is.EqualTo("基础攻击"));Assert.That(c.PrimaryValue,Is.EqualTo(4));Assert.That(c.Initiative,Is.EqualTo(11));Assert.That(c.SecondaryMovement,Is.EqualTo(1));Assert.That(c.SecondaryDefense,Is.EqualTo(2));Assert.That(CombatRules.HasPrimaryProgram(c),Is.True);Assert.That(CombatRules.HasPrimaryProgram(c,41),Is.False);c.Text+="同时移动";Assert.That(CombatRules.HasPrimaryProgram(c),Is.False);}
  [TestCase(false)] [TestCase(true)] public void MovedMinionReturnsAfterOriginalAttackAndDefense(bool defend)
  {
   var cat=BattlefieldTests.Catalog();var g=Setup(cat);Target(g);g=ChargeTests.Restore(cat,g);Assert.That(g.View(0).EffectTargets,Does.Contain(Minion));Apply(g,0,CommandKind.ChooseEffectTarget,Minion);g=ChargeTests.Restore(cat,g);Apply(g,0,CommandKind.ChooseEffectMove,cell:new Hex(-1,-4));g=ChargeTests.Restore(cat,g);
   Assert.That(g.View(1).Attack!.TargetUnitId,Is.EqualTo("hero:1"));Assert.That(g.View(1).Attack!.FinalAttack,Is.EqualTo(4));Assert.That(g.View(1).Attack!.FriendlyGuard,Is.Zero);Assert.That(g.View(0).Units.Single(u=>u.Id==Minion).Position,Is.EqualTo(new Hex(-1,-4)));
   Apply(g,1,defend?CommandKind.Defend:CommandKind.DeclineDefense,defend?"wasp-13-控物":"");g=ChargeTests.Restore(cat,g);Assert.That(g.View(1).Pending!.Kind,Is.EqualTo("minion_return"));Assert.That(g.View(1).Pending!.ChooserSeat,Is.EqualTo(1));Assert.That(g.View(0).Events.Any(e=>e.Kind=="CardResolved" && e.CardId==Blade),Is.False);
   string before=g.ExportSave();Assert.That(g.Execute(0,Cmd(g,0,Return,Minion,destination:new Hex(-2,-3))).Accepted,Is.False);Assert.That(g.ExportSave(),Is.EqualTo(before));var coin=g.View(0).DecisionCoin;
   var command=Cmd(g,1,Return,Minion,destination:new Hex(-2,-3));Assert.That(g.Execute(1,command).Accepted,Is.True);before=g.ExportSave();Assert.That(g.Execute(1,command).Duplicate,Is.True);Assert.That(g.ExportSave(),Is.EqualTo(before));g=ChargeTests.Restore(cat,g);
   Assert.That(g.View(0).Units.Single(u=>u.Id==Minion).Position,Is.EqualTo(new Hex(-2,-3)));Assert.That(g.View(0).DecisionCoin,Is.EqualTo(coin));Assert.That(g.View(0).Events.Count(e=>e.Kind=="AttackDeclared"),Is.EqualTo(1));Assert.That(g.View(0).Events.Count(e=>e.Kind=="CardResolved" && e.CardId==Blade),Is.EqualTo(1));Assert.That(g.View(0).Players[0].Gold,Is.EqualTo(defend?0:1));
  }
  [TestCase(2)] [TestCase(3)] public void SourceChoosesFriendlyOrEnemyHeroMoveAndOriginalTargetStays(int movedSeat)
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);Target(g);Assert.That(g.View(0).EffectTargets,Does.Contain("hero:"+movedSeat));Apply(g,0,CommandKind.ChooseEffectTarget,"hero:"+movedSeat);Assert.That(g.View(movedSeat).EffectMoves,Is.Empty);var dest=g.View(0).EffectMoves.First().Destination;Apply(g,0,CommandKind.ChooseEffectMove,cell:dest);g=ChargeTests.Restore(cat,g);Assert.That(g.View(0).Units.Single(u=>u.Seat==movedSeat).Position,Is.EqualTo(dest));Assert.That(g.View(1).Attack!.TargetUnitId,Is.EqualTo("hero:1"));Apply(g,1,CommandKind.Defend,"wasp-13-控物");ChargeTests.Restore(cat,g);Assert.That(g.View(0).Pending,Is.Null);}
  [TestCase(false)] [TestCase(true)] public void SkippingAtEitherOptionalWindowKeepsOriginalGuard(bool afterSelecting)
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);Target(g);if(afterSelecting)Apply(g,0,CommandKind.ChooseEffectTarget,Minion);Apply(g,0,afterSelecting?CommandKind.ChooseEffectMove:CommandKind.ChooseEffectTarget,"skip");g=ChargeTests.Restore(cat,g);Assert.That(g.View(1).Attack!.TargetUnitId,Is.EqualTo("hero:1"));Assert.That(g.View(1).Attack!.FriendlyGuard,Is.EqualTo(1));Assert.That(g.View(1).Attack!.FinalAttack,Is.EqualTo(3));Apply(g,1,CommandKind.Defend,"wasp-13-控物");Assert.That(g.View(0).Pending,Is.Null);}
  [Test] public void SelfOriginalTargetFarUnitAndProtectedHeavyCannotBeMoved()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);Apply(g,0,CommandKind.DebugTeleport,"hero:2",cell:new Hex(6,-8));Apply(g,0,CommandKind.DebugTeleport,"minion:-3,0",cell:new Hex(-1,-2));Target(g);string before=g.ExportSave();foreach(string id in new[]{"hero:0","hero:1","minion:1,-1","minion:-3,0"}){Assert.That(g.View(0).EffectTargets,Does.Not.Contain(id));Assert.That(g.Execute(0,Cmd(g,0,CommandKind.ChooseEffectTarget,id)).Accepted,Is.False);Assert.That(g.ExportSave(),Is.EqualTo(before));}}
  [Test] public void OriginalMinionTargetIsNotReplacedByTheMovedMinion()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);Apply(g,0,CommandKind.DebugTeleport,"minion:4,-1",cell:new Hex(1,-2));Apply(g,0,CommandKind.BeginPrimary);Apply(g,0,CommandKind.ChooseAttackTarget,"minion:4,-1");Apply(g,0,CommandKind.ChooseEffectTarget,"minion:1,-1");var dest=g.View(0).EffectMoves.First(m=>cat.Cell(m.Destination)!.Region=="mid").Destination;Apply(g,0,CommandKind.ChooseEffectMove,cell:dest);g=ChargeTests.Restore(cat,g);Assert.That(g.View(0).Units.Any(u=>u.Id=="minion:4,-1"),Is.False);Assert.That(g.View(0).Units.Single(u=>u.Id=="minion:1,-1").Position,Is.EqualTo(dest));Assert.That(g.View(0).Players[0].Gold,Is.EqualTo(2));}
  [Test] public void PreviousPlacementWindowRemainsByteStable()
  {var cat=BattlefieldTests.Catalog();string save=File.ReadAllText(Path.Combine(ContentTests.Root(),"tests/fixtures/engine41-tidal-placement.json"));var g=LocalGameFactory.Restore(cat,save);Assert.That(g.ExportSave(),Is.EqualTo(save));Assert.That(g.View(0).SupportedPrimaryCards,Does.Not.Contain(Blade));Apply(g,0,CommandKind.ChoosePlacement,cell:new Hex(6,-6));ChargeTests.Restore(cat,g);}
  [Test] public void ActualStaticBoundaryRestrictsTheMovedUnitBeforeOriginalAttack()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat,true);Apply(g,0,CommandKind.BeginPrimary);Apply(g,0,CommandKind.ChooseAttackTarget,"hero:3");Apply(g,0,CommandKind.ChooseEffectTarget,"hero:2");Assert.That(g.View(0).EffectMoves.Any(m=>m.Destination==new Hex(-3,-2)),Is.False);string before=g.ExportSave();Assert.That(g.Execute(0,Cmd(g,0,CommandKind.ChooseEffectMove,destination:new Hex(-3,-2))).Accepted,Is.False);Assert.That(g.ExportSave(),Is.EqualTo(before));Apply(g,0,CommandKind.ChooseEffectMove,cell:new Hex(-2,-1));g=ChargeTests.Restore(cat,g);Assert.That(g.View(3).Attack!.TargetUnitId,Is.EqualTo("hero:3"));Apply(g,3,CommandKind.Defend,"sabina-17-演练");ChargeTests.Restore(cat,g);}
  [Test] public void MoveBelongsToAttackerAndNeitherHeroMovementBonusExtendsTheFixedStep()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);Target(g);string before=g.ExportSave();Assert.That(g.Execute(2,Cmd(g,2,CommandKind.ChooseEffectTarget,"hero:2")).Accepted,Is.False);Assert.That(g.ExportSave(),Is.EqualTo(before));Apply(g,0,CommandKind.ChooseEffectTarget,"hero:2");before=g.ExportSave();var valid=g.View(0).EffectMoves.First().Destination;Assert.That(g.Execute(2,Cmd(g,2,CommandKind.ChooseEffectMove,destination:valid)).Accepted,Is.False);Assert.That(g.ExportSave(),Is.EqualTo(before));var state=new JsonStateCodec().Read(before);state.Players[0].MovementBonus=8;state.Players[2].MovementBonus=8;var moves=GameRules.LegalEffectMoves(cat,state,0);Assert.That(moves,Is.Not.Empty);Assert.That(moves.All(m=>m.Path.Count==2),Is.True);Assert.That(GameRules.LegalEffectMoves(cat,state,2),Is.Empty);}
  [Test] public void NoOtherMovableUnitContinuesDirectlyToDefense()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);Apply(g,0,CommandKind.DebugTeleport,Minion,cell:new Hex(6,-8));Apply(g,0,CommandKind.DebugTeleport,"hero:2",cell:new Hex(7,-8));Apply(g,0,CommandKind.DebugTeleport,"hero:3",cell:new Hex(6,-9));Target(g);Assert.That(g.View(1).Pending!.Kind,Is.EqualTo("defense"));Assert.That(g.View(0).Events.Any(e=>e.Kind=="OtherMoveTargetChoiceRequired"),Is.False);Apply(g,1,CommandKind.Defend,"wasp-13-控物");ChargeTests.Restore(cat,g);}
  [Test] public void VictoryDoesNotLeaveAMinionReturnChoiceAfterTheMatch()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);Apply(g,0,CommandKind.DebugSetCrystal,"1",target:1);Target(g);Apply(g,0,CommandKind.ChooseEffectTarget,Minion);Apply(g,0,CommandKind.ChooseEffectMove,cell:new Hex(-1,-4));Apply(g,1,CommandKind.DeclineDefense);ChargeTests.Restore(cat,g);Assert.That(g.View(0).Phase,Is.EqualTo(Phase.Finished));Assert.That(g.View(0).Pending,Is.Null);Assert.That(g.View(0).Events.Any(e=>e.Kind=="MinionReturnChoiceRequired"),Is.False);}
 }
}
