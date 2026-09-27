#nullable enable
using System.Collections.Generic;
using System.Linq;
using Goa2.Domain;
using Goa2.Rules.Cards;
namespace Goa2.Rules
{
 public sealed partial class GameRules
 {
  private static bool IsUnitPlacementStep(ContentCatalog catalog,GameState state)
  {
   var e=state.Execution;if(e==null || state.EngineVersion<78)return false;var p=CardPrograms.Primary(catalog.Card(e.CardId),state.EngineVersion);
   return p!=null && p.Id==e.ProgramId && p.Version==e.ProgramVersion && e.Cursor>=0 && e.Cursor<p.Instructions.Count && p.Instructions[e.Cursor]==InstructionKind.ChooseUnitPlacement;
  }
  private static List<Hex> UnitPlacementCells(ContentCatalog catalog,GameState state)
  {
   var source=state.Units.SingleOrDefault(u=>u.Seat==state.Execution?.ControllerSeat);if(source==null)return new List<Hex>();
   return source.Position.Neighbors().Where(p=>catalog.Cell(p)?.Obstacle==false && !state.Units.Any(u=>u.Position==p)).OrderBy(p=>p.X).ThenBy(p=>p.Y).ToList();
  }
  private static List<string> UnitPlacementTargets(ContentCatalog catalog,GameState state)
  {
   if(!IsUnitPlacementStep(catalog,state) || UnitPlacementCells(catalog,state).Count==0)return new List<string>();var e=state.Execution!;
   var source=state.Units.Single(u=>u.Seat==e.ControllerSeat);var minions=new HashSet<string>(LegalMinionRemovals(state));
   int range=(catalog.Card(e.CardId).SubtypeValue??0)+state.Players[e.ControllerSeat].RangedBonus;
   return state.Units.Where(u=>u.Id!=source.Id && (u.Kind=="hero" || minions.Contains(u.Id)) && u.Position.Distance(source.Position)<=range &&
    !u.Position.IsInStraightLineWith(source.Position) && EffectRules.CanDisplace(catalog,state,e.ControllerSeat,u)).Select(u=>u.Id).OrderBy(id=>id,System.StringComparer.Ordinal).ToList();
  }
  private static List<string> LegalUnitPlacementTargets(ContentCatalog catalog,GameState state,int seat)=>
   state.Phase==Phase.EffectChoice && state.Pending?.Kind=="effect_target" && state.Pending.ResumeAt=="unit_placement_target" && state.Pending.ChooserSeat==seat &&
   state.Execution?.ControllerSeat==seat && state.ActiveSeat==seat ? UnitPlacementTargets(catalog,state):new List<string>();
  private static List<Hex> LegalUnitPlacements(ContentCatalog catalog,GameState state,int seat)=>
   state.Phase==Phase.EffectChoice && state.Pending?.Kind=="placement" && state.Pending.ResumeAt=="unit_placement" && state.Pending.ChooserSeat==seat &&
   state.Execution?.ControllerSeat==seat && state.ActiveSeat==seat && UnitPlacementTargets(catalog,state).Contains(state.Pending.UnitId) ? UnitPlacementCells(catalog,state):new List<Hex>();
  private static bool BeginUnitPlacement(ContentCatalog catalog,GameState state,Command command)
  {
   var targets=UnitPlacementTargets(catalog,state);if(targets.Count==0)return false;var e=state.Execution!;
   state.Phase=Phase.EffectChoice;state.Pending=new PendingChoice{Id="unit-placement-target:"+(state.Events.Count+1),Kind="effect_target",Source=e.CardId,ChooserSeat=e.ControllerSeat,ResumeAt="unit_placement_target",CandidateUnits=targets};
   Emit(state,command,"EffectTargetChoiceRequired",e.ControllerSeat,e.CardId,detail:"unit_placement_target");return true;
  }
  private static void ChooseUnitPlacementTarget(ContentCatalog catalog,GameState state,Command command)
  {
   Require(LegalUnitPlacementTargets(catalog,state,command.ActorSeat).Contains(command.Value),"invalid_effect_target","请选择攻击距离内不在同一直线、且可被放置的单位。");
   var e=state.Execution!;e.TargetUnitId=command.Value;
   state.Pending=new PendingChoice{Id="unit-placement:"+(state.Events.Count+1),Kind="placement",Source=e.CardId,ChooserSeat=e.ControllerSeat,ResumeAt="unit_placement",UnitId=command.Value,CandidateCells=UnitPlacementCells(catalog,state)};
   Emit(state,command,"EffectTargetChosen",e.ControllerSeat,e.CardId,detail:command.Value);Emit(state,command,"PlacementChoiceRequired",e.ControllerSeat,e.CardId,detail:command.Value);
  }
  private static void ChooseUnitPlacement(ContentCatalog catalog,GameState state,Command command)
  {
   Require(command.Value=="" && command.MoveMode==MoveMode.Secondary && LegalUnitPlacements(catalog,state,command.ActorSeat).Contains(command.Destination),"invalid_placement","请选择来源英雄相邻的合法空格，放置不能跳过或替换为移动。");
   var e=state.Execution!;var unit=state.Units.Single(u=>u.Id==state.Pending!.UnitId);var origin=unit.Position;unit.Position=command.Destination;RecordDisplacedMinion(state,unit);
   Emit(state,command,"UnitPlaced",unit.Seat,e.CardId,detail:"by:"+e.ControllerSeat+"|unit:"+unit.Id);var placed=state.Events.Last();placed.From=origin;placed.To=unit.Position;
   e.Cursor++;state.Pending=null;state.Phase=Phase.Action;ContinueCard(catalog,state,command);
  }
 }
}
