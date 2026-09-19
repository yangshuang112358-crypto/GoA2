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
 public sealed class TidalPowerTests
 {
  internal const string Tidal="arien-11-潮汐之力";
  private static CommandKind Placement => (CommandKind)Enum.Parse(typeof(CommandKind),"ChoosePlacement");
  internal static GameSession Setup(ContentCatalog cat,bool boundary=false)
  {
   var g=LocalGameFactory.Create(cat,"tidal",new[]{"A","B","C","D"},42,true);Apply(g,0,CommandKind.DebugPrepare,"arien,wasp,brogan,sabina");Apply(g,0,CommandKind.DebugEquipCard,Tidal,target:0);
   Apply(g,0,CommandKind.DebugTeleport,"hero:0",cell:new Hex(6,-8));Apply(g,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(7,-8));Apply(g,0,CommandKind.DebugTeleport,"hero:2",cell:new Hex(5,-8));Apply(g,0,CommandKind.DebugTeleport,"hero:3",cell:new Hex(6,-9));
   string[] cards={Tidal,"wasp-06-静电封锁","brogan-06-铜墙铁壁","sabina-07-指挥"};for(int s=0;s<4;s++)Apply(g,s,CommandKind.SelectCard,cards[s]);Apply(g,1,boundary?CommandKind.BeginPrimary:CommandKind.Pass);Apply(g,2,CommandKind.Pass);Apply(g,3,CommandKind.Pass);Assert.That(g.View(0).ActiveSeat,Is.EqualTo(0));return g;
  }
  [Test] public void ContractAndEngineGate()
  {var c=BattlefieldTests.Catalog().Card(Tidal);Assert.That(c.Initiative,Is.EqualTo(3));Assert.That(c.SubtypeValue,Is.EqualTo(3));Assert.That(c.SecondaryMovement,Is.EqualTo(2));Assert.That(c.SecondaryDefense,Is.EqualTo(4));Assert.That(CombatRules.HasPrimaryProgram(c),Is.True);Assert.That(CombatRules.HasPrimaryProgram(c,40),Is.False);c.Text+="移动一格";Assert.That(CombatRules.HasPrimaryProgram(c),Is.False);}
  [Test] public void PlacesAcrossMovementBoundaryWithoutMovementEventOrPath()
  {
   var cat=BattlefieldTests.Catalog();var g=Setup(cat,true);Apply(g,0,CommandKind.BeginPrimary);g=ChargeTests.Restore(cat,g);var cell=new Hex(6,-6);Assert.That(g.View(0).Pending!.CandidateCells,Does.Contain(cell));
   var cmd=Cmd(g,0,Placement,destination:cell);Assert.That(g.Execute(0,cmd).Accepted,Is.True);string save=g.ExportSave();Assert.That(g.Execute(0,cmd).Duplicate,Is.True);Assert.That(g.ExportSave(),Is.EqualTo(save));g=ChargeTests.Restore(cat,g);
   Assert.That(g.View(0).Units.Single(u=>u.Seat==0).Position,Is.EqualTo(cell));Assert.That(g.View(0).Events.Any(e=>e.Kind=="UnitMoved"),Is.False);var placed=g.View(0).Events.Single(e=>e.Kind=="UnitPlaced");Assert.That(placed.From,Is.EqualTo(new Hex(6,-8)));Assert.That(placed.To,Is.EqualTo(cell));Assert.That(placed.Path,Is.Empty);Assert.That(placed.CardId,Is.EqualTo(Tidal));Assert.That(g.View(0).Events.Count(e=>e.Kind=="CardResolved" && e.CardId==Tidal),Is.EqualTo(1));
  }
  [Test] public void RejectsOwnCellTerrainOccupiedSpawnAndOutsideRange()
  {
   var cat=BattlefieldTests.Catalog();var g=Setup(cat);Apply(g,0,CommandKind.BeginPrimary);var view=g.View(0);var cells=view.Pending!.CandidateCells;
   Assert.That(cells,Is.Not.Empty);Assert.That(cells.All(p=>p.Distance(new Hex(6,-8))>=1 && p.Distance(new Hex(6,-8))<=3 && cat.Cell(p)!.Spawn=="empty" && !cat.Cell(p)!.Obstacle && view.Units.All(u=>u.Position!=p)),Is.True);
   var invalid=cat.Cells.Where(c=>c.Obstacle || c.Spawn!="empty").Select(c=>c.Position).Concat(view.Units.Select(u=>u.Position)).Concat(new[]{new Hex(99,99)}).Distinct();string before=g.ExportSave();
   foreach(var cell in invalid){Assert.That(g.Execute(0,Cmd(g,0,Placement,destination:cell)).Accepted,Is.False);Assert.That(g.ExportSave(),Is.EqualTo(before));}
  }
  [Test] public void WrongActorSkipAndFastModeAreAtomic()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);Apply(g,0,CommandKind.BeginPrimary);var cell=g.View(0).Pending!.CandidateCells.First();string before=g.ExportSave();foreach(var cmd in new[]{Cmd(g,1,Placement,destination:cell),Cmd(g,0,Placement,"skip",destination:cell),Cmd(g,0,Placement,destination:cell,mode:MoveMode.Fast)}){Assert.That(g.Execute(cmd.ActorSeat,cmd).Accepted,Is.False);Assert.That(g.ExportSave(),Is.EqualTo(before));}}
  [TestCase(0,0,0)] [TestCase(1,0,0)] [TestCase(0,3,3)] public void OnlyRangedBonusExtendsPlacement(int ranged,int radius,int movement)
  {
   var cat=BattlefieldTests.Catalog();var g=Setup(cat);var state=new JsonStateCodec().Read(g.ExportSave());state.Players[0].RangedBonus=ranged;state.Players[0].RangeBonus=radius;state.Players[0].MovementBonus=movement;new GameRules().Apply(cat,state,new Command{ActorSeat=0,Kind=CommandKind.BeginPrimary});var source=state.Units.Single(u=>u.Seat==0);
   var expected=cat.Cells.Where(c=>!c.Obstacle && c.Spawn=="empty" && c.Position.Distance(source.Position)>0 && c.Position.Distance(source.Position)<=3+ranged && state.Units.All(u=>u.Position!=c.Position)).Select(c=>c.Position);Assert.That(state.Pending!.CandidateCells,Is.EquivalentTo(expected));Assert.That(state.Pending.CandidateCells.Any(p=>p.Distance(source.Position)==3+ranged),Is.True);
  }
  [Test] public void NoEmptyDestinationEndsWithoutChoice()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);var state=new JsonStateCodec().Read(g.ExportSave());foreach(var c in cat.Cells.Where(c=>!c.Obstacle && c.Spawn=="empty" && c.Position.Distance(new Hex(6,-8))<=3 && state.Units.All(u=>u.Position!=c.Position)).ToArray())state.Units.Add(new UnitState{Id="block:"+c.Position,Kind="melee",Team=Team.Red,Position=c.Position});new GameRules().Apply(cat,state,new Command{ActorSeat=0,Kind=CommandKind.BeginPrimary});Assert.That(state.Pending?.Kind,Is.Not.EqualTo("placement"));Assert.That(state.Events.Any(e=>e.Kind=="CardEffectStopped" && e.Detail=="no_destinations"),Is.True);Assert.That(state.Events.Any(e=>e.Kind=="UnitPlaced"),Is.False);}
  [Test] public void SkillSuppressionStillPreventsBeginningThePlacement()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);var state=new JsonStateCodec().Read(g.ExportSave());state.Effects.Add(new ActiveEffect{Id="suppression",SourceCardId="arien-06-打断施法",SourceUnitId="hero:1",ControllerSeat=1,Kind=EffectKind.SkillSuppression,AreaKind=EffectAreaKind.SkillRange,Window=EffectTimeline.Create(1,1,4,EffectDuration.ThisTurn)!});string before=new JsonStateCodec().Write(state);var ex=Assert.Throws<RuleViolation>(()=>new GameRules().Apply(cat,state,new Command{ActorSeat=0,Kind=CommandKind.BeginPrimary}));Assert.That(ex!.Code,Is.EqualTo("primary_restricted"));Assert.That(new JsonStateCodec().Write(state),Is.EqualTo(before));}
  [Test] public void PreviousPikeWindowKeepsItsOriginalBytesAndRepeat()
  {var cat=BattlefieldTests.Catalog();string save=File.ReadAllText(Path.Combine(ContentTests.Root(),"tests/fixtures/engine40-pike-repeat.json"));var g=LocalGameFactory.Restore(cat,save);Assert.That(g.ExportSave(),Is.EqualTo(save));Assert.That(g.View(0).SupportedPrimaryCards,Does.Not.Contain(Tidal));Apply(g,0,CommandKind.ChooseAttackTarget,"minion:-1,-3");ChargeTests.Restore(cat,g);Assert.That(g.View(0).Pending,Is.Null);}
  [Test] public void FullSurroundDoesNotBlockDistantPlacement()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);var state=new JsonStateCodec().Read(g.ExportSave());foreach(var p in new Hex(6,-8).Neighbors().Where(p=>cat.Cell(p)?.Obstacle==false && state.Units.All(u=>u.Position!=p)))state.Units.Add(new UnitState{Id="surround:"+p,Kind="melee",Team=Team.Red,Position=p});new GameRules().Apply(cat,state,new Command{ActorSeat=0,Kind=CommandKind.BeginPrimary});Assert.That(GameRules.LegalPlacements(cat,state,0),Does.Contain(new Hex(6,-6)));new GameRules().Apply(cat,state,new Command{ActorSeat=0,Kind=Placement,Destination=new Hex(6,-6)});Assert.That(state.Units.Single(u=>u.Seat==0).Position,Is.EqualTo(new Hex(6,-6)));Assert.That(state.Events.Any(e=>e.Kind=="UnitMoved"),Is.False);}
  [Test] public void AdjacentEmptySpawnIsAllowedAndOnlyOwnerReceivesActionOptions()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);Apply(g,0,CommandKind.BeginPrimary);var view=g.View(0);Assert.That(view.Placements,Is.EquivalentTo(view.Pending!.CandidateCells));Assert.That(view.Placements.Any(p=>p.Neighbors().Any(n=>cat.Cell(n)!=null && cat.Cell(n)!.Spawn.EndsWith("Spawn") && view.Units.All(u=>u.Position!=n))),Is.True);Assert.That(g.View(1).Placements,Is.Empty);Assert.That(g.View(null).Placements,Is.Empty);view.Placements.Clear();Assert.That(g.View(0).Placements,Is.Not.Empty);}
 }
}
