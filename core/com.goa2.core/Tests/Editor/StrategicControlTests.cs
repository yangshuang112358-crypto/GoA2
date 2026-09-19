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
 public sealed class StrategicControlTests
 {
  internal const string Card="sabina-09-战略控制";
  internal static GameSession Setup(ContentCatalog cat,bool boundary=false,string card=Card)=>CommandMinionTests.Setup(cat,boundary,false,card);
  [Test] public void ContractIsThreeStepsAndIntroducedOnlyIn44()
  {var c=BattlefieldTests.Catalog().Card(Card);Assert.That(c.Initiative,Is.EqualTo(3));Assert.That(c.SubtypeValue,Is.EqualTo(2));Assert.That(c.SecondaryMovement,Is.EqualTo(2));Assert.That(c.SecondaryDefense,Is.EqualTo(3));Assert.That(CombatRules.HasPrimaryProgram(c),Is.True);Assert.That(CombatRules.HasPrimaryProgram(c,43),Is.False);c.Text=c.Text.Replace("3格","4格");Assert.That(CombatRules.HasPrimaryProgram(c),Is.False);}
  [TestCase(CommandMinionTests.Melee)] [TestCase(CommandMinionTests.Ranged)] [TestCase(CommandMinionTests.Heavy)] public void EachFriendlyKindCanMoveExactlyThreeStepsAndFinishOnce(string id)
  {
   var cat=BattlefieldTests.Catalog();var g=Setup(cat);Apply(g,0,CommandKind.BeginPrimary);g=ChargeTests.Restore(cat,g);Apply(g,0,CommandKind.ChooseEffectTarget,id);g=ChargeTests.Restore(cat,g);var move=g.View(0).EffectMoves.First(m=>m.Path.Count==4 && cat.Cell(m.Destination)!.Region=="mid");Apply(g,0,CommandKind.ChooseEffectMove,cell:move.Destination);g=ChargeTests.Restore(cat,g);Assert.That(g.View(0).Events.Single(e=>e.Kind=="UnitMoved" && e.CardId==Card).Path.Count,Is.EqualTo(4));Assert.That(g.View(0).Events.Count(e=>e.Kind=="CardResolved" && e.CardId==Card),Is.EqualTo(1));Assert.That(g.View(0).Pending,Is.Null);Assert.That(g.View(0).Players.All(p=>p.Gold==0),Is.True);
  }
  [Test] public void FourStepsCannotBeBoughtWithMovementBonusAndShorterMovesRemainLegal()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);Apply(g,0,CommandKind.BeginPrimary);Apply(g,0,CommandKind.ChooseEffectTarget,CommandMinionTests.Melee);var state=new JsonStateCodec().Read(g.ExportSave());state.Players[0].MovementBonus=10;var moves=GameRules.LegalEffectMoves(cat,state,0);Assert.That(moves.All(m=>m.Path.Count<=4),Is.True);Assert.That(moves.Any(m=>m.Path.Count==2),Is.True);Assert.That(moves.Any(m=>m.Path.Count==3),Is.True);var unit=state.Units.Single(u=>u.Id==CommandMinionTests.Melee);var far=cat.Cells.First(c=>!c.Obstacle && c.Position.Distance(unit.Position)==4 && state.Units.All(u=>u.Position!=c.Position)).Position;string before=g.ExportSave();Assert.That(g.Execute(0,Cmd(g,0,CommandKind.ChooseEffectMove,destination:far)).Accepted,Is.False);Assert.That(g.ExportSave(),Is.EqualTo(before));Apply(g,0,CommandKind.ChooseEffectMove,"skip");ChargeTests.Restore(cat,g);}
  [Test] public void ProtectedHeavyCanCrossActualStaticBoundaryWithThreeStepBudget()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat,true);Apply(g,0,CommandKind.BeginPrimary);Apply(g,0,CommandKind.ChooseEffectTarget,CommandMinionTests.Heavy);var move=g.View(0).EffectMoves.First(m=>m.Destination.Distance(new Hex(-2,-1))>2 && cat.Cell(m.Destination)!.Region=="mid");Apply(g,0,CommandKind.ChooseEffectMove,cell:move.Destination);ChargeTests.Restore(cat,g);Assert.That(g.View(0).Pending,Is.Null);}
  [Test] public void TargetRangeRemainsTwoAndChoiceRemainsOwnedBySource()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);Apply(g,0,CommandKind.BeginPrimary);var state=new JsonStateCodec().Read(g.ExportSave());var unit=state.Units.Single(u=>u.Id==CommandMinionTests.Melee);var source=state.Units.Single(u=>u.Seat==0);unit.Position=cat.Cells.First(c=>!c.Obstacle && c.Position.Distance(source.Position)==3 && state.Units.All(u=>u.Position!=c.Position)).Position;Assert.That(GameRules.LegalEffectTargets(cat,state,0),Does.Not.Contain(unit.Id));state.Players[0].RangeBonus=1;Assert.That(GameRules.LegalEffectTargets(cat,state,0),Does.Contain(unit.Id));string before=g.ExportSave();Assert.That(g.Execute(2,Cmd(g,2,CommandKind.ChooseEffectTarget,CommandMinionTests.Melee)).Accepted,Is.False);Assert.That(g.ExportSave(),Is.EqualTo(before));}
  [Test] public void Engine43HeavyChoiceRemainsByteStableAndCanCrossBoundary()
  {var cat=BattlefieldTests.Catalog();string save=File.ReadAllText(Path.Combine(ContentTests.Root(),"tests/fixtures/engine43-command-heavy.json"));var g=LocalGameFactory.Restore(cat,save);Assert.That(g.ExportSave(),Is.EqualTo(save));Assert.That(g.View(0).SupportedPrimaryCards,Does.Not.Contain(Card));Apply(g,0,CommandKind.ChooseEffectMove,cell:new Hex(1,-2));ChargeTests.Restore(cat,g);Assert.That(g.View(0).Pending,Is.Null);}
 }
}
