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
 public sealed class StrategicAdvantageTests
 {
  internal const string Card="sabina-11-战略优势";
  internal static GameSession Setup(ContentCatalog cat,bool boundary=false)=>StrategicControlTests.Setup(cat,boundary,Card);
  private static void First(GameSession g,string id=CommandMinionTests.Melee){Apply(g,0,CommandKind.BeginPrimary);Apply(g,0,CommandKind.ChooseEffectTarget,id);}
  [Test] public void ContractAndVersionGate()
  {var c=BattlefieldTests.Catalog().Card(Card);Assert.That(c.Initiative,Is.EqualTo(3));Assert.That(c.SubtypeValue,Is.EqualTo(2));Assert.That(c.SecondaryDefense,Is.EqualTo(3));Assert.That(CombatRules.HasPrimaryProgram(c),Is.True);Assert.That(CombatRules.HasPrimaryProgram(c,44),Is.False);c.Text=c.Text.Replace("一次","两次");Assert.That(CombatRules.HasPrimaryProgram(c),Is.False);}
  [TestCase(true)] [TestCase(false)] public void RepeatMaySelectSameOrDifferentMinionAndNeverOffersThirdAction(bool same)
  {
   var cat=BattlefieldTests.Catalog();var g=Setup(cat);First(g);Apply(g,0,CommandKind.ChooseEffectMove,"skip");g=ChargeTests.Restore(cat,g);Assert.That(g.View(0).Pending!.Optional,Is.True);Assert.That(g.View(0).Pending!.ResumeAt,Is.EqualTo("friendly_minion_repeat"));Apply(g,0,CommandKind.ChooseEffectTarget,same?CommandMinionTests.Melee:CommandMinionTests.Ranged);g=ChargeTests.Restore(cat,g);var move=g.View(0).EffectMoves.First(m=>m.Path.Count==4 && cat.Cell(m.Destination)!.Region=="mid");Apply(g,0,CommandKind.ChooseEffectMove,cell:move.Destination);g=ChargeTests.Restore(cat,g);Assert.That(g.View(0).Pending,Is.Null);Assert.That(g.View(0).Events.Count(e=>e.Kind=="ActionRepeated" && e.CardId==Card),Is.EqualTo(1));Assert.That(g.View(0).Events.Count(e=>e.Kind=="PrimaryActionStarted" && e.CardId==Card),Is.EqualTo(1));Assert.That(g.View(0).Events.Count(e=>e.Kind=="CardResolved" && e.CardId==Card),Is.EqualTo(1));
   string before=g.ExportSave();Assert.That(g.Execute(0,Cmd(g,0,CommandKind.ChooseEffectTarget,CommandMinionTests.Melee)).Accepted,Is.False);Assert.That(g.ExportSave(),Is.EqualTo(before));
  }
  [Test] public void RepeatCanBeSkippedOnlyBySourceAndFirstTargetCannotBeSkipped()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);Apply(g,0,CommandKind.BeginPrimary);string before=g.ExportSave();Assert.That(g.Execute(0,Cmd(g,0,CommandKind.ChooseEffectTarget,"skip")).Accepted,Is.False);Assert.That(g.ExportSave(),Is.EqualTo(before));Apply(g,0,CommandKind.ChooseEffectTarget,CommandMinionTests.Melee);Apply(g,0,CommandKind.ChooseEffectMove,"skip");before=g.ExportSave();Assert.That(g.Execute(2,Cmd(g,2,CommandKind.ChooseEffectTarget,"skip")).Accepted,Is.False);Assert.That(g.ExportSave(),Is.EqualTo(before));Assert.That(g.View(2).EffectTargets,Is.Empty);var command=Cmd(g,0,CommandKind.ChooseEffectTarget,"skip");Assert.That(g.Execute(0,command).Accepted,Is.True);before=g.ExportSave();Assert.That(g.Execute(0,command).Duplicate,Is.True);Assert.That(g.ExportSave(),Is.EqualTo(before));ChargeTests.Restore(cat,g);Assert.That(g.View(0).Events.Count(e=>e.Kind=="ActionRepeated"),Is.Zero);}
  [Test] public void FirstMoveChangesRepeatRangeAndCannotBecomeSixStepMovement()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);First(g);var dest=new Hex(-4,-1);Assert.That(g.View(0).EffectMoves.Any(m=>m.Destination==dest),Is.True);Apply(g,0,CommandKind.ChooseEffectMove,cell:dest);g=ChargeTests.Restore(cat,g);Assert.That(g.View(0).EffectTargets,Does.Not.Contain(CommandMinionTests.Melee));string before=g.ExportSave();Assert.That(g.Execute(0,Cmd(g,0,CommandKind.ChooseEffectTarget,CommandMinionTests.Melee)).Accepted,Is.False);Assert.That(g.ExportSave(),Is.EqualTo(before));Apply(g,0,CommandKind.ChooseEffectTarget,CommandMinionTests.Ranged);Assert.That(g.View(0).EffectMoves.All(m=>m.Path.Count<=4),Is.True);Apply(g,0,CommandKind.ChooseEffectMove,"skip");ChargeTests.Restore(cat,g);}
  [Test] public void EachMoveReturnsMinionBeforeNextActionOrFinalCardResolution()
  {
   var cat=BattlefieldTests.Catalog();var g=Setup(cat);Apply(g,0,CommandKind.DebugTeleport,"hero:0",cell:new Hex(0,-2));Apply(g,0,CommandKind.DebugTeleport,"hero:3",cell:new Hex(6,-9));Apply(g,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(7,-8));Apply(g,0,CommandKind.DebugTeleport,FlashingBladeTests.Minion,cell:new Hex(8,-9));Apply(g,0,CommandKind.DebugTeleport,CommandMinionTests.Melee,cell:new Hex(-1,-3));
   First(g);for(int action=0;action<2;action++)
   {
    Apply(g,0,CommandKind.ChooseEffectMove,cell:new Hex(-1,-4));g=ChargeTests.Restore(cat,g);Assert.That(g.View(0).Pending!.Kind,Is.EqualTo("minion_return"));Assert.That(g.View(0).Events.Count(e=>e.Kind=="CardResolved" && e.CardId==Card),Is.Zero);Apply(g,0,CommandKind.ChooseMinionReturn,CommandMinionTests.Melee,cell:new Hex(-1,-3));g=ChargeTests.Restore(cat,g);
    if(action==0){Assert.That(g.View(0).Pending!.ResumeAt,Is.EqualTo("friendly_minion_repeat"));Apply(g,0,CommandKind.ChooseEffectTarget,CommandMinionTests.Melee);g=ChargeTests.Restore(cat,g);}
   }
   Assert.That(g.View(0).Pending,Is.Null);Assert.That(g.View(0).Events.Count(e=>e.Kind=="MinionReturnCompleted"),Is.EqualTo(2));Assert.That(g.View(0).Events.Count(e=>e.Kind=="CardResolved" && e.CardId==Card),Is.EqualTo(1));Assert.That(g.View(0).Players.All(p=>p.Gold==0),Is.True);
  }
  [Test] public void ProtectedHeavyCrossesBoundaryThenRepeatUsesUpdatedRange()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat,true);First(g,CommandMinionTests.Heavy);Apply(g,0,CommandKind.ChooseEffectMove,cell:new Hex(1,-2));g=ChargeTests.Restore(cat,g);Assert.That(g.View(0).EffectTargets,Does.Not.Contain(CommandMinionTests.Heavy));Assert.That(g.View(0).EffectTargets,Does.Contain(CommandMinionTests.Melee));Apply(g,0,CommandKind.ChooseEffectTarget,CommandMinionTests.Melee);Assert.That(g.View(0).EffectMoves.Any(m=>m.Destination==new Hex(-3,2)),Is.False);Apply(g,0,CommandKind.ChooseEffectMove,"skip");ChargeTests.Restore(cat,g);}
  [Test] public void NoInitialTargetDoesNotInventRepeat()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);var state=new JsonStateCodec().Read(g.ExportSave());state.Units.RemoveAll(u=>u.Team==Team.Blue && u.Kind!="hero");new GameRules().Apply(cat,state,new Command{ActorSeat=0,Kind=CommandKind.BeginPrimary});Assert.That(state.Pending,Is.Null);Assert.That(state.Execution,Is.Null);Assert.That(state.Events.Any(e=>e.Kind=="ActionRepeatChoiceRequired"),Is.False);}
  [Test] public void Engine44MoveChoiceRemainsByteStableWithoutNewRepeat()
  {var cat=BattlefieldTests.Catalog();string save=File.ReadAllText(Path.Combine(ContentTests.Root(),"tests/fixtures/engine44-control-move.json"));var g=LocalGameFactory.Restore(cat,save);Assert.That(g.ExportSave(),Is.EqualTo(save));Assert.That(g.View(0).SupportedPrimaryCards,Does.Not.Contain(Card));Apply(g,0,CommandKind.ChooseEffectMove,cell:new Hex(-4,-1));ChargeTests.Restore(cat,g);Assert.That(g.View(0).Pending,Is.Null);Assert.That(g.View(0).Events.Any(e=>e.Kind=="ActionRepeatChoiceRequired"),Is.False);}
  [Test] public void NoTargetsAfterFirstMovementFinishesWithoutEmptyRepeatChoice()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);First(g);var state=new JsonStateCodec().Read(g.ExportSave());state.Units.RemoveAll(u=>u.Team==Team.Blue && u.Kind!="hero" && u.Id!=CommandMinionTests.Melee);new GameRules().Apply(cat,state,new Command{ActorSeat=0,Kind=CommandKind.ChooseEffectMove,Destination=new Hex(-4,-1)});Assert.That(state.Pending,Is.Null);Assert.That(state.Execution,Is.Null);Assert.That(state.Events.Any(e=>e.Kind=="ActionRepeatChoiceRequired"),Is.False);Assert.That(state.Events.Count(e=>e.Kind=="CardResolved" && e.CardId==Card),Is.EqualTo(1));}
  [Test] public void BlockedMinionCanCompleteBothZeroMovesWithoutLooping()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);Apply(g,0,CommandKind.BeginPrimary);var state=new JsonStateCodec().Read(g.ExportSave());var unit=state.Units.Single(u=>u.Id==CommandMinionTests.Melee);foreach(var p in unit.Position.Neighbors().Where(p=>cat.Cell(p)?.Obstacle==false && state.Units.All(u=>u.Position!=p)).ToArray())state.Units.Add(new UnitState{Id="block:"+p,Kind="melee",Team=Team.Red,Position=p});var rules=new GameRules();rules.Apply(cat,state,new Command{ActorSeat=0,Kind=CommandKind.ChooseEffectTarget,Value=unit.Id});Assert.That(state.Pending!.ResumeAt,Is.EqualTo("friendly_minion_repeat"));rules.Apply(cat,state,new Command{ActorSeat=0,Kind=CommandKind.ChooseEffectTarget,Value=unit.Id});Assert.That(state.Pending,Is.Null);Assert.That(state.Execution,Is.Null);Assert.That(state.Events.Count(e=>e.Kind=="CardResolved" && e.CardId==Card),Is.EqualTo(1));Assert.That(state.Events.Any(e=>e.Kind=="UnitMoved"),Is.False);}
 }
}
