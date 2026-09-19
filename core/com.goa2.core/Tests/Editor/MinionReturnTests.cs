using System.Linq;
using Goa2.Domain;
using Goa2.Infrastructure;
using Goa2.Rules;
using NUnit.Framework;
using static Goa2.Tests.SessionTests;
using static Goa2.Tests.TurnFlowTests;
namespace Goa2.Tests
{
 public sealed class MinionReturnTests
 {
  [Test] public void MultiStepReturnRestoresLockedMinionAndCommandJournalAtEveryStep()
  {
   var cat=BattlefieldTests.Catalog();var g=FlashingBladeTests.Setup(cat);
   Apply(g,0,CommandKind.DebugTeleport,"hero:0",cell:new Hex(6,-8));Apply(g,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(7,-8));Apply(g,0,CommandKind.DebugTeleport,"hero:2",cell:new Hex(5,-8));Apply(g,0,CommandKind.DebugTeleport,"hero:3",cell:new Hex(6,-9));Apply(g,0,CommandKind.DebugTeleport,FlashingBladeTests.Minion,cell:new Hex(7,-9));
   FlashingBladeTests.Target(g);Apply(g,0,CommandKind.ChooseEffectTarget,FlashingBladeTests.Minion);Apply(g,0,CommandKind.ChooseEffectMove,cell:new Hex(8,-9));Apply(g,1,CommandKind.Defend,"wasp-13-控物");
   int steps=0;while(g.View(1).Pending?.Kind=="minion_return" && steps++<30)
   {
    var option=g.View(1).MinionReturns.First();Apply(g,1,CommandKind.ChooseMinionReturn,option.UnitId,cell:option.Destination);g=ChargeTests.Restore(cat,g);
    if(steps==1)Assert.That(new JsonStateCodec().Read(g.ExportSave()).Execution!.ReturningMinionId,Is.EqualTo(FlashingBladeTests.Minion));
   }
   Assert.That(steps,Is.InRange(2,29));Assert.That(g.View(0).Events.Count(e=>e.Kind=="CardResolved" && e.CardId==FlashingBladeTests.Blade),Is.EqualTo(1));Assert.That(g.View(0).Players[0].Gold,Is.Zero);
  }
  [Test] public void ReturnQueueResumesThunderRepeatInsteadOfEndingOriginalCard()
  {
   var cat=BattlefieldTests.Catalog();var g=ThunderBoomerangTests.Setup(cat);Apply(g,0,CommandKind.BeginPrimary);Apply(g,0,CommandKind.ChooseAttackTarget,"hero:1");var state=new JsonStateCodec().Read(g.ExportSave());
   // Isolated continuation contract: a future attached action has displaced this minion.
   var minion=state.Units.Single(u=>u.Id=="minion:1,0");minion.Position=new Hex(-2,-5);state.Execution!.DisplacedMinions=new System.Collections.Generic.List<string>{minion.Id};
   var rules=new GameRules();rules.Apply(cat,state,new Command{ActorSeat=1,Kind=CommandKind.DeclineDefense});Assert.That(state.Pending!.Kind,Is.EqualTo("minion_return"));int steps=0;
   while(state.Pending?.Kind=="minion_return" && steps++<30){int seat=state.Pending.ChooserSeat;var option=GameRules.LegalMinionReturns(cat,state,seat).First();rules.Apply(cat,state,new Command{ActorSeat=seat,Kind=CommandKind.ChooseMinionReturn,Value=option.UnitId,Destination=option.Destination});}
   Assert.That(steps,Is.LessThan(30));Assert.That(state.Pending?.ResumeAt,Is.EqualTo("repeat_attack"));Assert.That(state.Execution!.CardId,Is.EqualTo(ThunderBoomerangTests.Thunder));
  }
  private static GameState BeforeDefense(ContentCatalog cat)
  {var g=FlashingBladeTests.Setup(cat);FlashingBladeTests.Target(g);Apply(g,0,CommandKind.ChooseEffectTarget,FlashingBladeTests.Minion);Apply(g,0,CommandKind.ChooseEffectMove,cell:new Hex(-1,-4));return new JsonStateCodec().Read(g.ExportSave());}
  private static void Defend(ContentCatalog cat,GameState state)=>new GameRules().Apply(cat,state,new Command{ActorSeat=1,Kind=CommandKind.Defend,Value="wasp-13-控物"});
  [Test] public void AllEquallyShortestNextCellsAndCaptainOnlyOptions()
  {var cat=BattlefieldTests.Catalog();var g=FlashingBladeTests.Setup(cat);FlashingBladeTests.Target(g);Apply(g,0,CommandKind.ChooseEffectTarget,FlashingBladeTests.Minion);Apply(g,0,CommandKind.ChooseEffectMove,cell:new Hex(-1,-4));Apply(g,1,CommandKind.Defend,"wasp-13-控物");g=ChargeTests.Restore(cat,g);Assert.That(g.View(1).MinionReturns.Select(o=>o.Destination),Is.EquivalentTo(new[]{new Hex(-1,-3),new Hex(-2,-3)}));Assert.That(g.View(1).MinionReturns.All(o=>!o.Place && o.RemainingDistance==0),Is.True);Assert.That(g.View(0).MinionReturns,Is.Empty);Assert.That(g.View(null).MinionReturns,Is.Empty);string before=g.ExportSave();foreach(var command in new[]{Cmd(g,1,CommandKind.ChooseMinionReturn,FlashingBladeTests.Minion,destination:new Hex(-2,-4)),Cmd(g,1,CommandKind.ChooseMinionReturn,FlashingBladeTests.Minion,destination:new Hex(-1,-3),mode:MoveMode.Fast),Cmd(g,1,CommandKind.ChooseMinionReturn,"hero:1",destination:new Hex(-1,-3))}){Assert.That(g.Execute(1,command).Accepted,Is.False);Assert.That(g.ExportSave(),Is.EqualTo(before));}}
  [Test] public void CompletelyBlockedRouteUsesOnlyGeometricallyNearestEmptyBattleCells()
  {
   var cat=BattlefieldTests.Catalog();var state=BeforeDefense(cat);var minion=state.Units.Single(u=>u.Id==FlashingBladeTests.Minion);
   foreach(var p in minion.Position.Neighbors().Where(p=>cat.Cell(p)?.Obstacle==false && state.Units.All(u=>u.Position!=p)).ToArray())state.Units.Add(new UnitState{Id="block:"+p,Kind="melee",Team=Team.Red,Position=p});Defend(cat,state);
   var goals=cat.Cells.Where(c=>c.Region==state.CombatRegion && !c.Obstacle && state.Units.All(u=>u.Position!=c.Position)).Select(c=>c.Position).ToList();int nearest=goals.Min(p=>p.Distance(minion.Position));var options=GameRules.LegalMinionReturns(cat,state,1);Assert.That(options.All(o=>o.Place),Is.True);Assert.That(options.Select(o=>o.Destination),Is.EquivalentTo(goals.Where(p=>p.Distance(minion.Position)==nearest)));
   var chosen=options.First();new GameRules().Apply(cat,state,new Command{ActorSeat=1,Kind=CommandKind.ChooseMinionReturn,Value=chosen.UnitId,Destination=chosen.Destination});Assert.That(state.Events.Count(e=>e.Kind=="MinionReturnPlaced"),Is.EqualTo(1));Assert.That(state.Events.Single(e=>e.Kind=="MinionReturnPlaced").Path,Is.Empty);Assert.That(state.Events.Any(e=>e.Kind=="MinionReturnMoved"),Is.False);Assert.That(state.Execution,Is.Null);
  }
  [TestCase(Team.Blue)] [TestCase(Team.Red)] public void MultipleTeamsUseCoinWithoutFlippingAndFinishEachMinionBeforeSwitching(Team first)
  {
   var cat=BattlefieldTests.Catalog();var state=BeforeDefense(cat);state.DecisionCoin=first;var blue=state.Units.Single(u=>u.Id=="minion:1,0");blue.Position=new Hex(-2,-5);state.Execution!.DisplacedMinions!.Add(blue.Id);Defend(cat,state);
   Assert.That(state.Pending!.ChooserSeat,Is.EqualTo(first==Team.Blue?0:1));int steps=0;string? locked=null;
   while(state.Pending?.Kind=="minion_return" && steps++<30)
   {
    int captain=state.Pending.ChooserSeat;var options=GameRules.LegalMinionReturns(cat,state,captain);Assert.That(options,Is.Not.Empty);if(locked!=null)Assert.That(options.All(o=>o.UnitId==locked),Is.True);
    var chosen=options.First();new GameRules().Apply(cat,state,new Command{ActorSeat=captain,Kind=CommandKind.ChooseMinionReturn,Value=chosen.UnitId,Destination=chosen.Destination});locked=state.Execution?.ReturningMinionId;
   }
   Assert.That(steps,Is.LessThan(30));Assert.That(state.Execution,Is.Null);Assert.That(state.DecisionCoin,Is.EqualTo(first));Assert.That(state.Events.Count(e=>e.Kind=="MinionReturnCompleted"),Is.EqualTo(2));Assert.That(state.Events.Count(e=>e.Kind=="CardResolved" && e.CardId==FlashingBladeTests.Blade),Is.EqualTo(1));Assert.That(state.Players[0].Gold,Is.Zero);
  }
  [Test] public void StaticBoundaryCanForceReturnPlacementWithoutBeingIgnoredByNormalMovement()
  {
   var cat=BattlefieldTests.Catalog();var state=BeforeDefense(cat);var minion=state.Units.Single(u=>u.Id==FlashingBladeTests.Minion);var source=state.Units.Single(u=>u.Seat==2);
   source.Position=cat.Cells.First(c=>!c.Obstacle && c.Position.Distance(minion.Position)<=2 && cat.Cells.Where(b=>b.Region==state.CombatRegion && !b.Obstacle).All(b=>b.Position.Distance(c.Position)>2) && state.Units.All(u=>u.Position!=c.Position)).Position;
   // Isolated boundary geometry; actual card activation is covered by aura session tests.
   state.Effects.Add(new ActiveEffect{Id="return-boundary",Kind=EffectKind.MovementBoundary,SourceCardId="wasp-06-静电封锁",SourceUnitId=source.Id,ControllerSeat=2,AreaKind=EffectAreaKind.SkillRange,Window=EffectTimeline.Create(1,1,4,EffectDuration.ThisTurn)!});Defend(cat,state);
   var options=GameRules.LegalMinionReturns(cat,state,1);Assert.That(options,Is.Not.Empty);Assert.That(options.All(o=>o.Place),Is.True);Assert.That(options.All(o=>o.Destination.Distance(source.Position)>2),Is.True);
  }
  [Test] public void MinionAlreadyInsideDoesNotOpenReturnAndUnrelatedSandboxMinionIsUntouched()
  {var cat=BattlefieldTests.Catalog();var state=BeforeDefense(cat);state.Units.Single(u=>u.Id==FlashingBladeTests.Minion).Position=new Hex(-1,-3);var unrelated=state.Units.Single(u=>u.Id=="minion:1,-1");unrelated.Position=new Hex(6,-8);Defend(cat,state);Assert.That(state.Pending,Is.Null);Assert.That(state.Execution,Is.Null);Assert.That(unrelated.Position,Is.EqualTo(new Hex(6,-8)));Assert.That(state.Events.Any(e=>e.Kind=="MinionReturnChoiceRequired"),Is.False);}
 }
}
