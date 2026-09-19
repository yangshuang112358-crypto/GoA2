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
 public sealed class CommandMinionTests
 {
  internal const string Card="sabina-07-指挥",Melee="minion:-3,1",Ranged="minion:-1,2",Heavy="minion:3,1";
  internal static GameSession Setup(ContentCatalog cat,bool boundary=false,bool suppression=false)
  {
   var g=LocalGameFactory.Create(cat,"command-minion",new[]{"A","B","C","D"},42,true);Apply(g,0,CommandKind.DebugPrepare,"sabina,wasp,brogan,arien");
   Apply(g,0,CommandKind.DebugTeleport,"hero:0",cell:new Hex(-2,0));Apply(g,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(-2,-1));Apply(g,0,CommandKind.DebugTeleport,"hero:2",cell:new Hex(6,-8));Apply(g,0,CommandKind.DebugTeleport,"hero:3",cell:suppression?new Hex(-1,-1):new Hex(7,-8));
   Apply(g,0,CommandKind.DebugTeleport,Ranged,cell:new Hex(-2,1));Apply(g,0,CommandKind.DebugTeleport,Heavy,cell:new Hex(-1,0));
   string[] cards={Card,"wasp-06-静电封锁","brogan-06-铜墙铁壁",suppression?"arien-06-打断施法":"arien-07-潮水"};for(int s=0;s<4;s++)Apply(g,s,CommandKind.SelectCard,cards[s]);
   if(suppression)Apply(g,3,CommandKind.BeginPrimary);Apply(g,1,boundary?CommandKind.BeginPrimary:CommandKind.Pass);Apply(g,2,CommandKind.Pass);Assert.That(g.View(0).ActiveSeat,Is.EqualTo(0));return g;
  }
  [Test] public void ContractAndOldEngineGate()
  {var c=BattlefieldTests.Catalog().Card(Card);Assert.That(c.Initiative,Is.EqualTo(4));Assert.That(c.SubtypeValue,Is.EqualTo(2));Assert.That(c.SecondaryMovement,Is.EqualTo(2));Assert.That(c.SecondaryDefense,Is.EqualTo(2));Assert.That(CombatRules.HasPrimaryProgram(c),Is.True);Assert.That(CombatRules.HasPrimaryProgram(c,42),Is.False);c.Text+="同时";Assert.That(CombatRules.HasPrimaryProgram(c),Is.False);}
  [TestCase(Melee)] [TestCase(Ranged)] [TestCase(Heavy)] public void AllFriendlyMinionKindsMoveWithSavedChoices(string id)
  {
   var cat=BattlefieldTests.Catalog();var g=Setup(cat);Apply(g,0,CommandKind.BeginPrimary);g=ChargeTests.Restore(cat,g);Assert.That(g.View(0).EffectTargets,Does.Contain(id));Apply(g,0,CommandKind.ChooseEffectTarget,id);g=ChargeTests.Restore(cat,g);
   var move=g.View(0).EffectMoves.First(m=>m.Path.Count==3 && cat.Cell(m.Destination)!.Region=="mid");var cmd=Cmd(g,0,CommandKind.ChooseEffectMove,destination:move.Destination);Assert.That(g.Execute(0,cmd).Accepted,Is.True);string save=g.ExportSave();Assert.That(g.Execute(0,cmd).Duplicate,Is.True);Assert.That(g.ExportSave(),Is.EqualTo(save));g=ChargeTests.Restore(cat,g);
   Assert.That(g.View(0).Units.Single(u=>u.Id==id).Position,Is.EqualTo(move.Destination));Assert.That(g.View(0).Events.Count(e=>e.Kind=="UnitMoved" && e.CardId==Card),Is.EqualTo(1));Assert.That(g.View(0).Events.Count(e=>e.Kind=="CardResolved" && e.CardId==Card),Is.EqualTo(1));Assert.That(g.View(0).Players.All(p=>p.Gold==0),Is.True);
  }
  [Test] public void ZeroMovementDoesNotDisplaceMinionOrStartReturn()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);Apply(g,0,CommandKind.BeginPrimary);Apply(g,0,CommandKind.ChooseEffectTarget,Melee);Apply(g,0,CommandKind.ChooseEffectMove,"skip");ChargeTests.Restore(cat,g);Assert.That(g.View(0).Pending,Is.Null);Assert.That(g.View(0).Events.Any(e=>e.Kind=="UnitMoved" || e.Kind=="MinionReturnChoiceRequired"),Is.False);}
  [Test] public void EnemyHeroDistantMinionAndWrongSeatAreRejectedAtomically()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);Apply(g,0,CommandKind.BeginPrimary);string before=g.ExportSave();foreach(var cmd in new[]{Cmd(g,0,CommandKind.ChooseEffectTarget,"hero:0"),Cmd(g,0,CommandKind.ChooseEffectTarget,"hero:2"),Cmd(g,0,CommandKind.ChooseEffectTarget,"minion:-1,-3"),Cmd(g,0,CommandKind.ChooseEffectTarget,"minion:4,0"),Cmd(g,2,CommandKind.ChooseEffectTarget,Melee),Cmd(g,0,CommandKind.ChooseEffectTarget,"skip")}){Assert.That(g.Execute(cmd.ActorSeat,cmd).Accepted,Is.False);Assert.That(g.ExportSave(),Is.EqualTo(before));}Assert.That(g.View(2).EffectTargets,Is.Empty);Assert.That(g.View(null).EffectTargets,Is.Empty);}
  [Test] public void FixedMoveBudgetAndOwnerCannotBeReplacedByFastMove()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);Apply(g,0,CommandKind.BeginPrimary);Apply(g,0,CommandKind.ChooseEffectTarget,Melee);var state=new JsonStateCodec().Read(g.ExportSave());state.Players[0].MovementBonus=10;Assert.That(GameRules.LegalEffectMoves(cat,state,0).All(m=>m.Path.Count<=3),Is.True);var dest=g.View(0).EffectMoves.First().Destination;string before=g.ExportSave();foreach(var cmd in new[]{Cmd(g,2,CommandKind.ChooseEffectMove,destination:dest),Cmd(g,0,CommandKind.ChooseEffectMove,destination:dest,mode:MoveMode.Fast),Cmd(g,0,CommandKind.ChooseEffectMove,destination:new Hex(99,99)),Cmd(g,0,CommandKind.ChooseEffectMove,destination:new Hex(-2,0))}){Assert.That(g.Execute(cmd.ActorSeat,cmd).Accepted,Is.False);Assert.That(g.ExportSave(),Is.EqualTo(before));}}
  [Test] public void ActualStaticBoundaryRestrictsOrdinaryMinionButNotProtectedHeavy()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat,true);Apply(g,0,CommandKind.BeginPrimary);Apply(g,0,CommandKind.ChooseEffectTarget,Melee);Assert.That(g.View(0).EffectMoves.Any(m=>m.Destination==new Hex(-3,2)),Is.False);var heavyGame=Setup(cat,true);Apply(heavyGame,0,CommandKind.BeginPrimary);Apply(heavyGame,0,CommandKind.ChooseEffectTarget,Heavy);var outside=heavyGame.View(0).EffectMoves.First(m=>m.Destination.Distance(new Hex(-2,-1))>2);Apply(heavyGame,0,CommandKind.ChooseEffectMove,cell:outside.Destination);ChargeTests.Restore(cat,heavyGame);}
  [Test] public void ActualSkillSuppressionPreventsBeginningCommand()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat,false,true);string before=g.ExportSave();var result=g.Execute(0,Cmd(g,0,CommandKind.BeginPrimary));Assert.That(result.Accepted,Is.False);Assert.That(g.ExportSave(),Is.EqualTo(before));}
  [Test] public void OnlySkillRangeBonusExtendsTargetRange()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);Apply(g,0,CommandKind.BeginPrimary);var state=new JsonStateCodec().Read(g.ExportSave());var source=state.Units.Single(u=>u.Seat==0);var target=state.Units.Single(u=>u.Id==Melee);target.Position=cat.Cells.First(c=>!c.Obstacle && c.Position.Distance(source.Position)==3 && state.Units.All(u=>u.Position!=c.Position)).Position;state.Players[0].RangedBonus=5;Assert.That(GameRules.LegalEffectTargets(cat,state,0),Does.Not.Contain(Melee));state.Players[0].RangeBonus=1;Assert.That(GameRules.LegalEffectTargets(cat,state,0),Does.Contain(Melee));target.Position=cat.Cells.First(c=>!c.Obstacle && c.Position.Distance(source.Position)==4 && state.Units.All(u=>u.Position!=c.Position)).Position;Assert.That(GameRules.LegalEffectTargets(cat,state,0),Does.Not.Contain(Melee));}
  [Test] public void UnprotectedHeavyObeysStaticBoundaryAndEngine42RetainsOldBehavior()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat,true);Apply(g,0,CommandKind.BeginPrimary);Apply(g,0,CommandKind.ChooseEffectTarget,Heavy);var state=new JsonStateCodec().Read(g.ExportSave());var heavy=state.Units.Single(u=>u.Id==Heavy);var outside=GameRules.LegalEffectMoves(cat,state,0).First(m=>m.Destination.Distance(new Hex(-2,-1))>2);state.Units.RemoveAll(u=>u.Team==heavy.Team && u.Kind!="hero" && u.Id!=Heavy);Assert.That(GameRules.LegalEffectMoves(cat,state,0).Any(m=>m.Destination==outside.Destination),Is.False);
   var old=new JsonStateCodec().Read(g.ExportSave());old.EngineVersion=42;var oldHeavy=old.Units.Single(u=>u.Id==Heavy);Assert.That(Enumerable.Range(1,outside.Path.Count-1).Any(i=>!EffectRules.CanMoveAcross(cat,old,oldHeavy,outside.Path[i-1],outside.Path[i])),Is.True);}
  [Test] public void NoFriendlyMinionInRangeStopsWithoutChoiceOrReward()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);var state=new JsonStateCodec().Read(g.ExportSave());state.Units.RemoveAll(u=>u.Team==Team.Blue && u.Kind!="hero");new GameRules().Apply(cat,state,new Command{ActorSeat=0,Kind=CommandKind.BeginPrimary});Assert.That(state.Pending,Is.Null);Assert.That(state.Execution,Is.Null);Assert.That(state.Events.Any(e=>e.Kind=="CardEffectStopped" && e.Detail=="no_targets"),Is.True);Assert.That(state.Players.All(p=>p.Gold==0),Is.True);}
  [Test] public void FullyBlockedTargetStillCanBeChosenAndCompletesWithZeroMovement()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);Apply(g,0,CommandKind.BeginPrimary);var state=new JsonStateCodec().Read(g.ExportSave());var unit=state.Units.Single(u=>u.Id==Melee);foreach(var p in unit.Position.Neighbors().Where(p=>cat.Cell(p)?.Obstacle==false && state.Units.All(u=>u.Position!=p)).ToArray())state.Units.Add(new UnitState{Id="block:"+p,Kind="melee",Team=Team.Red,Position=p});Assert.That(GameRules.LegalEffectTargets(cat,state,0),Does.Contain(Melee));new GameRules().Apply(cat,state,new Command{ActorSeat=0,Kind=CommandKind.ChooseEffectTarget,Value=Melee});Assert.That(state.Pending,Is.Null);Assert.That(state.Execution,Is.Null);Assert.That(state.Events.Any(e=>e.Kind=="UnitMoved"),Is.False);}
  [Test] public void PreviousLockedReturnSaveRemainsByteStableAndCompletes()
  {var cat=BattlefieldTests.Catalog();string save=File.ReadAllText(Path.Combine(ContentTests.Root(),"tests/fixtures/engine42-flashing-return.json"));var g=LocalGameFactory.Restore(cat,save);Assert.That(g.ExportSave(),Is.EqualTo(save));Assert.That(g.View(0).SupportedPrimaryCards,Does.Not.Contain(Card));int steps=0;while(g.View(1).Pending?.Kind=="minion_return" && steps++<30){var o=g.View(1).MinionReturns.First();Apply(g,1,CommandKind.ChooseMinionReturn,o.UnitId,cell:o.Destination);g=ChargeTests.Restore(cat,g);}Assert.That(steps,Is.InRange(1,29));Assert.That(g.View(0).Events.Count(e=>e.Kind=="CardResolved" && e.CardId==FlashingBladeTests.Blade),Is.EqualTo(1));}
  [Test] public void SourceChoosesMovementButDifferentCaptainChoosesReturnWithJournalRestoration()
  {
   var cat=BattlefieldTests.Catalog();var g=LocalGameFactory.Create(cat,"command-other-captain",new[]{"A","B","C","D"},42,true);Apply(g,0,CommandKind.DebugPrepare,"brogan,wasp,sabina,arien");Apply(g,0,CommandKind.DebugTeleport,"hero:0",cell:new Hex(5,-8));Apply(g,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(7,-8));Apply(g,0,CommandKind.DebugTeleport,"hero:2",cell:new Hex(6,-8));Apply(g,0,CommandKind.DebugTeleport,"hero:3",cell:new Hex(6,-9));Apply(g,0,CommandKind.DebugTeleport,Melee,cell:new Hex(7,-9));
   string[] cards={"brogan-06-铜墙铁壁","wasp-06-静电封锁",Card,"arien-07-潮水"};for(int i=0;i<4;i++)Apply(g,i,CommandKind.SelectCard,cards[i]);Apply(g,1,CommandKind.Pass);Apply(g,0,CommandKind.Pass);Apply(g,2,CommandKind.BeginPrimary);Apply(g,2,CommandKind.ChooseEffectTarget,Melee);Assert.That(g.View(0).EffectMoves,Is.Empty);Apply(g,2,CommandKind.ChooseEffectMove,cell:new Hex(8,-9));g=ChargeTests.Restore(cat,g);Assert.That(g.View(0).Pending!.ChooserSeat,Is.EqualTo(0));Assert.That(g.View(2).MinionReturns,Is.Empty);int steps=0;
   while(g.View(0).Pending?.Kind=="minion_return" && steps++<30){var o=g.View(0).MinionReturns.First();Apply(g,0,CommandKind.ChooseMinionReturn,o.UnitId,cell:o.Destination);g=ChargeTests.Restore(cat,g);}
   Assert.That(steps,Is.InRange(2,29));Assert.That(g.View(0).Events.Count(e=>e.Kind=="CardResolved" && e.CardId==Card),Is.EqualTo(1));Assert.That(g.View(0).Players.All(p=>p.Gold==0),Is.True);
  }
 }
}
