#nullable enable
using System.Collections.Generic;
using System.Linq;
using Goa2.Domain;
using Goa2.Rules.Cards;

namespace Goa2.Rules
{
    public sealed partial class GameRules
    {
        private static List<MoveOption> ChargeMoves(ContentCatalog catalog,GameState state,UnitState source,PrimaryProgram program)
        {
            var card=catalog.Card(state.Execution!.CardId);
            return MovementRules.StraightExact(catalog,state,source,program.TextMoveDistance).Where(option=>
            {
                // Check the attack from a prospective endpoint without moving the live unit during a query.
                var projected=new UnitState{Id=source.Id,Kind=source.Kind,Team=source.Team,Seat=source.Seat,Position=option.Destination};
                return CombatRules.Targets(catalog,state,projected,card,program).Count>0;
            }).ToList();
        }
        private static bool BeginRequiredCharge(ContentCatalog catalog,GameState state,Command command,CardExecution execution,PrimaryProgram program)
        {
            var source=state.Units.SingleOrDefault(u=>u.Seat==execution.ControllerSeat);
            var options=source==null ? new List<MoveOption>() : ChargeMoves(catalog,state,source,program);
            if(options.Count==0)return false;
            state.Phase=Phase.EffectChoice;
            state.Pending=new PendingChoice
            {
                Id="effect-move:"+(state.Events.Count+1),Kind="effect_move",ChooserSeat=execution.ControllerSeat,
                Source=execution.CardId,UnitId=source!.Id,ResumeAt="charge_before_attack",Optional=false,
                CandidateCells=options.Select(o=>o.Destination).ToList()
            };
            Emit(state,command,"EffectMoveChoiceRequired",execution.ControllerSeat,execution.CardId,detail:program.TextMoveDistance.ToString());return true;
        }
        private static void MoveIntoAttackTargetCell(ContentCatalog catalog,GameState state,Command command,CardExecution execution,PrimaryProgram program)
        {
            var source=state.Units.SingleOrDefault(u=>u.Seat==execution.ControllerSeat);
            Require(execution.AttackTargetCell.HasValue,"missing_attack_position","攻击目标的位置记录缺失。");
            var destination=execution.AttackTargetCell!.Value;
            var option=source==null ? null : MovementRules.Reachable(catalog,state,source,program.TextMoveDistance).SingleOrDefault(o=>o.Destination==destination);
            if(option==null)
            {
                string reason=source==null ? "source_absent" : state.Units.Any(u=>u.Position==destination) ? "target_position_occupied" : "target_position_unreachable";
                Emit(state,command,"EffectMoveSkipped",execution.ControllerSeat,execution.CardId,detail:reason);return;
            }
            var origin=source!.Position;source.Position=destination;
            Emit(state,command,"UnitMoved",execution.ControllerSeat,execution.CardId,detail:"CardText");
            var moved=state.Events.Last();moved.From=origin;moved.To=destination;moved.Path=option.Path;
        }
        private static PrimaryProgram? TextMoveProgram(ContentCatalog catalog,GameState state)
        {
            var execution=state.Execution;
            if(state.EngineVersion<11 || execution==null) return null;
            var program=CardPrograms.Primary(catalog.Card(execution.CardId),state.EngineVersion);
            return program!=null && program.Id==execution.ProgramId && program.Version==execution.ProgramVersion &&
                execution.Cursor>=0 && execution.Cursor<program.Instructions.Count &&
                (program.Instructions[execution.Cursor]==InstructionKind.OptionalTextMove ||
                 program.Instructions[execution.Cursor]==InstructionKind.OptionalPreAttackTextMove ||
                 program.Instructions[execution.Cursor]==InstructionKind.OptionalTextMoveIfNoPreMove ||
                 program.Instructions[execution.Cursor]==InstructionKind.RequiredStraightMoveToAttack) ? program : null;
        }
        public static List<MoveOption> LegalEffectMoves(ContentCatalog catalog,GameState state,int seat)
        {
            if(state.Pending?.ResumeAt=="defense_response_move")return LegalDefenseMoves(catalog,state,seat);
            if(state.Phase!=Phase.EffectChoice || state.Pending?.Kind!="effect_move" || state.Pending.ChooserSeat!=seat ||
                state.Execution?.ControllerSeat!=seat) return new List<MoveOption>();
            var program=TextMoveProgram(catalog,state);
            var source=state.Units.SingleOrDefault(u=>u.Seat==seat && u.Id==state.Pending.UnitId);
            if(program==null || source==null)return new List<MoveOption>();
            return program.Instructions[state.Execution!.Cursor]==InstructionKind.RequiredStraightMoveToAttack
                ? ChargeMoves(catalog,state,source,program) : MovementRules.Reachable(catalog,state,source,program.TextMoveDistance);
        }
        private static bool BeginTextMove(ContentCatalog catalog,GameState state,Command command,CardExecution execution,PrimaryProgram program)
        {
            var instruction=program.Instructions[execution.Cursor];
            if(instruction==InstructionKind.OptionalTextMoveIfNoPreMove && execution.PreAttackMoved)
            {
                Emit(state,command,"EffectMoveSkipped",execution.ControllerSeat,execution.CardId,detail:"pre_attack_move_used");
                return false;
            }
            // A card-text step continues the original action, without movement-icon bonuses or Fast Travel.
            var source=state.Units.SingleOrDefault(u=>u.Seat==execution.ControllerSeat);
            var options=source==null ? new List<MoveOption>() : MovementRules.Reachable(catalog,state,source,program.TextMoveDistance);
            if(options.Count==0)
            {
                Emit(state,command,"EffectMoveSkipped",execution.ControllerSeat,execution.CardId,detail:source==null ? "source_absent" : "no_destinations");
                return false;
            }
            state.Phase=Phase.EffectChoice;
            state.Pending=new PendingChoice
            {
                Id="effect-move:"+(state.Events.Count+1),Kind="effect_move",ChooserSeat=execution.ControllerSeat,
                Source=execution.CardId,UnitId=source!.Id,ResumeAt=instruction==InstructionKind.OptionalPreAttackTextMove ? "before_attack" : "card_text_move",Optional=true,
                CandidateCells=options.Select(o=>o.Destination).ToList()
            };
            Emit(state,command,"EffectMoveChoiceRequired",execution.ControllerSeat,execution.CardId,detail:program.TextMoveDistance.ToString());
            return true;
        }
        private static void ChooseEffectMove(ContentCatalog catalog,GameState state,Command command)
        {
            if(state.Pending?.ResumeAt=="defense_response_move"){ChooseDefenseMove(catalog,state,command);return;}
            Require(state.Phase==Phase.EffectChoice && state.Pending?.Kind=="effect_move" && state.Pending.ChooserSeat==command.ActorSeat &&
                state.Execution?.ControllerSeat==command.ActorSeat && TextMoveProgram(catalog,state)!=null &&
                (command.Value=="" || command.Value=="skip" && state.Pending.Optional) && command.MoveMode==MoveMode.Secondary,
                "invalid_effect_move","请由牌文移动的操作者选择合法格或不移动；不能替换为快速移动。");
            var execution=state.Execution!;
            if(command.Value=="skip") Emit(state,command,"EffectMoveSkipped",command.ActorSeat,execution.CardId,detail:"declined");
            else
            {
                var option=LegalEffectMoves(catalog,state,command.ActorSeat).SingleOrDefault(o=>o.Destination==command.Destination);
                Require(option!=null,"invalid_effect_move","该格不是当前牌文移动的合法落点。");
                var unit=state.Units.Single(u=>u.Seat==command.ActorSeat);
                var origin=unit.Position;unit.Position=command.Destination;
                if(TextMoveProgram(catalog,state)!.Instructions[execution.Cursor]==InstructionKind.OptionalPreAttackTextMove) execution.PreAttackMoved=true;
                Emit(state,command,"UnitMoved",command.ActorSeat,execution.CardId,detail:"CardText");
                var moved=state.Events.Last();moved.From=origin;moved.To=unit.Position;moved.Path=option!.Path;
            }
            execution.Cursor++;state.Pending=null;state.Phase=Phase.Action;
            ContinueCard(catalog,state,command);
        }
    }
}
