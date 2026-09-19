#nullable enable
using System.Collections.Generic;
using System.Linq;
using Goa2.Domain;
using Goa2.Rules.Cards;
namespace Goa2.Rules
{
    public sealed partial class GameRules
    {
        private static bool IsOtherMoveStep(ContentCatalog catalog,GameState state)
        {
            var execution=state.Execution;if(state.EngineVersion<42 || execution==null)return false;
            var program=CardPrograms.Primary(catalog.Card(execution.CardId),state.EngineVersion);
            return program!=null && program.Id==execution.ProgramId && program.Version==execution.ProgramVersion && execution.Cursor>=0 &&
                execution.Cursor<program.Instructions.Count && program.Instructions[execution.Cursor]==InstructionKind.OptionalMoveOtherAdjacentToTarget;
        }
        private static List<MoveOption> OtherMoveOptions(ContentCatalog catalog,GameState state,UnitState unit) =>
            MovementRules.Reachable(catalog,state,unit,CardPrograms.Primary(catalog.Card(state.Execution!.CardId),state.EngineVersion)!.TextMoveDistance);
        private static List<string> OtherMoveTargets(ContentCatalog catalog,GameState state)
        {
            if(!IsOtherMoveStep(catalog,state))return new List<string>();
            var execution=state.Execution!;var source=state.Units.SingleOrDefault(u=>u.Seat==execution.ControllerSeat);
            var target=state.Units.SingleOrDefault(u=>u.Id==execution.TargetUnitId);
            if(source==null || target==null)return new List<string>();
            var movableMinions=new HashSet<string>(LegalMinionRemovals(state));
            return state.Units.Where(u=>u.Id!=source.Id && u.Id!=target.Id && u.Position.Distance(target.Position)==1 &&
                (u.Kind=="hero" || movableMinions.Contains(u.Id)) && EffectRules.CanBeAttacked(state,source,u,catalog.Card(execution.CardId).Subtype=="远程") &&
                OtherMoveOptions(catalog,state,u).Count>0).Select(u=>u.Id).OrderBy(id=>id,System.StringComparer.Ordinal).ToList();
        }
        private static bool BeginOtherMove(ContentCatalog catalog,GameState state,Command command)
        {
            var targets=OtherMoveTargets(catalog,state);if(targets.Count==0)return false;
            var execution=state.Execution!;state.Phase=Phase.EffectChoice;
            state.Pending=new PendingChoice{Id="other-move-target:"+(state.Events.Count+1),Kind="effect_target",ChooserSeat=execution.ControllerSeat,
                Source=execution.CardId,ResumeAt="before_attack_other_move",Optional=true,CandidateUnits=targets};
            Emit(state,command,"OtherMoveTargetChoiceRequired",execution.ControllerSeat,execution.CardId);return true;
        }
        private static void ChooseOtherMoveTarget(ContentCatalog catalog,GameState state,Command command)
        {
            Require(state.Phase==Phase.EffectChoice && state.Pending?.Kind=="effect_target" && state.Pending.ResumeAt=="before_attack_other_move" &&
                state.Pending.ChooserSeat==command.ActorSeat && state.Execution?.ControllerSeat==command.ActorSeat && IsOtherMoveStep(catalog,state) &&
                (command.Value=="skip" || OtherMoveTargets(catalog,state).Contains(command.Value)),"invalid_effect_target","请选择与原攻击目标相邻的另一个可移动单位，或跳过。");
            var execution=state.Execution!;
            if(command.Value=="skip")
            {
                Emit(state,command,"EffectMoveSkipped",command.ActorSeat,execution.CardId,detail:"declined");
                execution.Cursor++;state.Pending=null;state.Phase=Phase.Action;ContinueCard(catalog,state,command);return;
            }
            var unit=state.Units.Single(u=>u.Id==command.Value);var moves=OtherMoveOptions(catalog,state,unit);
            state.Pending=new PendingChoice{Id="other-move:"+(state.Events.Count+1),Kind="effect_move",ChooserSeat=execution.ControllerSeat,
                Source=execution.CardId,UnitId=unit.Id,ResumeAt="other_unit_before_attack",Optional=true,CandidateCells=moves.Select(m=>m.Destination).ToList()};
            Emit(state,command,"OtherMoveTargetChosen",command.ActorSeat,execution.CardId,detail:unit.Id);
        }
        private static List<MoveOption> LegalOtherMoves(ContentCatalog catalog,GameState state,int seat)
        {
            if(state.Phase!=Phase.EffectChoice || state.Pending?.Kind!="effect_move" || state.Pending.ResumeAt!="other_unit_before_attack" ||
                state.Pending.ChooserSeat!=seat || state.Execution?.ControllerSeat!=seat || !IsOtherMoveStep(catalog,state))return new List<MoveOption>();
            var unit=state.Units.SingleOrDefault(u=>u.Id==state.Pending.UnitId);
            return unit!=null && OtherMoveTargets(catalog,state).Contains(unit.Id)?OtherMoveOptions(catalog,state,unit):new List<MoveOption>();
        }
        private static void ChooseOtherMove(ContentCatalog catalog,GameState state,Command command)
        {
            Require(state.Phase==Phase.EffectChoice && state.Pending?.Kind=="effect_move" && state.Pending.ResumeAt=="other_unit_before_attack" &&
                state.Pending.ChooserSeat==command.ActorSeat && state.Execution?.ControllerSeat==command.ActorSeat && IsOtherMoveStep(catalog,state) &&
                (command.Value=="" || command.Value=="skip") && command.MoveMode==MoveMode.Secondary,"invalid_effect_move","请选择该单位的一格合法落点或跳过，不能快速移动。");
            var execution=state.Execution!;
            if(command.Value=="skip")Emit(state,command,"EffectMoveSkipped",command.ActorSeat,execution.CardId,detail:"declined");
            else
            {
                var move=LegalOtherMoves(catalog,state,command.ActorSeat).SingleOrDefault(m=>m.Destination==command.Destination);
                Require(move!=null,"invalid_effect_move","该格不是额外单位的一格合法移动落点。");
                var unit=state.Units.Single(u=>u.Id==state.Pending!.UnitId);var origin=unit.Position;unit.Position=command.Destination;
                RecordDisplacedMinion(state,unit);
                Emit(state,command,"UnitMoved",unit.Seat,execution.CardId,detail:"by:"+command.ActorSeat+"|unit:"+unit.Id);
                var e=state.Events.Last();e.From=origin;e.To=unit.Position;e.Path=move!.Path;
            }
            execution.Cursor++;state.Pending=null;state.Phase=Phase.Action;ContinueCard(catalog,state,command);
        }
    }
}
