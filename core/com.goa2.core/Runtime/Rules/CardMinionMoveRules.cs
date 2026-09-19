#nullable enable
using System.Collections.Generic;
using System.Linq;
using Goa2.Domain;
using Goa2.Rules.Cards;
namespace Goa2.Rules
{
 public sealed partial class GameRules
 {
  private static PrimaryProgram? MinionMoveProgram(ContentCatalog catalog,GameState state,InstructionKind instruction)
  {
   var e=state.Execution;if(state.EngineVersion<43 || e==null)return null;var p=CardPrograms.Primary(catalog.Card(e.CardId),state.EngineVersion);
   return p!=null && p.Id==e.ProgramId && p.Version==e.ProgramVersion && e.Cursor>=0 && e.Cursor<p.Instructions.Count && p.Instructions[e.Cursor]==instruction?p:null;
  }
  private static List<string> FriendlyMinionTargets(ContentCatalog catalog,GameState state)
  {
   var program=MinionMoveProgram(catalog,state,InstructionKind.ChooseFriendlyMinionTarget) ?? MinionMoveProgram(catalog,state,InstructionKind.OptionalRepeatFriendlyMinionMove);if(program==null)return new List<string>();
   var e=state.Execution!;var source=state.Units.SingleOrDefault(u=>u.Seat==e.ControllerSeat);if(source==null)return new List<string>();
   int range=(catalog.Card(e.CardId).SubtypeValue??0)+state.Players[e.ControllerSeat].RangeBonus;var allowed=new HashSet<string>(LegalMinionRemovals(state,program.IgnoreHeavyImmunity));
   return state.Units.Where(u=>u.Team==source.Team && IsMinion(u) && allowed.Contains(u.Id) && u.Position.Distance(source.Position)<=range).Select(u=>u.Id).OrderBy(id=>id,System.StringComparer.Ordinal).ToList();
  }
  private static bool BeginFriendlyMinionTarget(ContentCatalog catalog,GameState state,Command command)
  {
   var targets=FriendlyMinionTargets(catalog,state);if(targets.Count==0)return false;var e=state.Execution!;
   bool repeat=MinionMoveProgram(catalog,state,InstructionKind.OptionalRepeatFriendlyMinionMove)!=null;
   state.Phase=Phase.EffectChoice;state.Pending=new PendingChoice{Id="minion-move-target:"+(state.Events.Count+1),Kind="effect_target",ChooserSeat=e.ControllerSeat,Source=e.CardId,ResumeAt=repeat?"friendly_minion_repeat":"friendly_minion_move",Optional=repeat,CandidateUnits=targets};
   Emit(state,command,repeat?"ActionRepeatChoiceRequired":"EffectTargetChoiceRequired",e.ControllerSeat,e.CardId);return true;
  }
  private static bool BeginTargetUnitMove(ContentCatalog catalog,GameState state,Command command)
  {
   var p=MinionMoveProgram(catalog,state,InstructionKind.OptionalTargetUnitMove);var e=state.Execution!;var unit=state.Units.SingleOrDefault(u=>u.Id==e.TargetUnitId);
   if(p==null || unit==null)return false;
   var moves=MovementRules.Reachable(catalog,state,unit,p.TextMoveDistance);
   if(moves.Count==0){Emit(state,command,"EffectMoveSkipped",e.ControllerSeat,e.CardId,detail:"no_destinations");return false;}
   state.Phase=Phase.EffectChoice;state.Pending=new PendingChoice{Id="target-unit-move:"+(state.Events.Count+1),Kind="effect_move",ChooserSeat=e.ControllerSeat,Source=e.CardId,UnitId=unit.Id,ResumeAt="target_unit_move",Optional=true,CandidateCells=moves.Select(m=>m.Destination).ToList()};
   Emit(state,command,"EffectMoveChoiceRequired",e.ControllerSeat,e.CardId,detail:p.TextMoveDistance.ToString());return true;
  }
  private static List<MoveOption> LegalTargetUnitMoves(ContentCatalog catalog,GameState state,int seat)
  {
   if(state.Phase!=Phase.EffectChoice || state.Pending?.Kind!="effect_move" || state.Pending.ResumeAt!="target_unit_move" || state.Pending.ChooserSeat!=seat || state.Execution?.ControllerSeat!=seat)return new List<MoveOption>();
   var p=MinionMoveProgram(catalog,state,InstructionKind.OptionalTargetUnitMove);var unit=state.Units.SingleOrDefault(u=>u.Id==state.Pending.UnitId && u.Id==state.Execution.TargetUnitId);
   return p!=null && unit!=null?MovementRules.Reachable(catalog,state,unit,p.TextMoveDistance):new List<MoveOption>();
  }
  private static void ChooseTargetUnitMove(ContentCatalog catalog,GameState state,Command command)
  {
   Require(state.Phase==Phase.EffectChoice && state.Pending?.Kind=="effect_move" && state.Pending.ResumeAt=="target_unit_move" && state.Pending.ChooserSeat==command.ActorSeat && state.Execution?.ControllerSeat==command.ActorSeat && MinionMoveProgram(catalog,state,InstructionKind.OptionalTargetUnitMove)!=null && (command.Value=="" || command.Value=="skip") && command.MoveMode==MoveMode.Secondary,"invalid_effect_move","请由技能来源英雄选择小兵普通移动落点，或不移动。");
   var e=state.Execution!;
   if(command.Value=="skip")Emit(state,command,"EffectMoveSkipped",command.ActorSeat,e.CardId,detail:"declined");
   else
   {
    var move=LegalTargetUnitMoves(catalog,state,command.ActorSeat).SingleOrDefault(m=>m.Destination==command.Destination);Require(move!=null,"invalid_effect_move","该格不是当前小兵的合法移动落点。");
    var unit=state.Units.Single(u=>u.Id==state.Pending!.UnitId);var origin=unit.Position;unit.Position=command.Destination;RecordDisplacedMinion(state,unit);
    Emit(state,command,"UnitMoved",unit.Seat,e.CardId,detail:"by:"+command.ActorSeat+"|unit:"+unit.Id);var moved=state.Events.Last();moved.From=origin;moved.To=unit.Position;moved.Path=move!.Path;
   }
   e.Cursor++;state.Pending=null;state.Phase=Phase.Action;ContinueCard(catalog,state,command);
  }
 }
}
