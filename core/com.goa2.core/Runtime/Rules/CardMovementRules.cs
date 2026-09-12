#nullable enable
using System.Collections.Generic;
using System.Linq;
using Goa2.Domain;
using Goa2.Rules.Cards;

namespace Goa2.Rules
{
    public sealed partial class GameRules
    {
        private static PrimaryProgram? TextMoveProgram(ContentCatalog catalog,GameState state)
        {
            var execution=state.Execution;
            if(state.EngineVersion<11 || execution==null) return null;
            var program=CardPrograms.Primary(catalog.Card(execution.CardId),state.EngineVersion);
            return program!=null && program.Id==execution.ProgramId && program.Version==execution.ProgramVersion &&
                execution.Cursor>=0 && execution.Cursor<program.Instructions.Count &&
                program.Instructions[execution.Cursor]==InstructionKind.OptionalTextMove ? program : null;
        }
        public static List<MoveOption> LegalEffectMoves(ContentCatalog catalog,GameState state,int seat)
        {
            if(state.Phase!=Phase.EffectChoice || state.Pending?.Kind!="effect_move" || state.Pending.ChooserSeat!=seat ||
                state.Execution?.ControllerSeat!=seat) return new List<MoveOption>();
            var program=TextMoveProgram(catalog,state);
            var source=state.Units.SingleOrDefault(u=>u.Seat==seat && u.Id==state.Pending.UnitId);
            return program==null || source==null ? new List<MoveOption>() : MovementRules.Reachable(catalog,state,source,program.TextMoveDistance);
        }
        private static bool BeginTextMove(ContentCatalog catalog,GameState state,Command command,CardExecution execution,PrimaryProgram program)
        {
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
                Source=execution.CardId,UnitId=source!.Id,ResumeAt="card_text_move",Optional=true,
                CandidateCells=options.Select(o=>o.Destination).ToList()
            };
            Emit(state,command,"EffectMoveChoiceRequired",execution.ControllerSeat,execution.CardId,detail:program.TextMoveDistance.ToString());
            return true;
        }
        private static void ChooseEffectMove(ContentCatalog catalog,GameState state,Command command)
        {
            Require(state.Phase==Phase.EffectChoice && state.Pending?.Kind=="effect_move" && state.Pending.ChooserSeat==command.ActorSeat &&
                state.Pending.Optional && state.Execution?.ControllerSeat==command.ActorSeat && TextMoveProgram(catalog,state)!=null &&
                (command.Value=="" || command.Value=="skip") && command.MoveMode==MoveMode.Secondary,
                "invalid_effect_move","请由牌文移动的操作者选择合法格或不移动；不能替换为快速移动。");
            var execution=state.Execution!;
            if(command.Value=="skip") Emit(state,command,"EffectMoveSkipped",command.ActorSeat,execution.CardId,detail:"declined");
            else
            {
                var option=LegalEffectMoves(catalog,state,command.ActorSeat).SingleOrDefault(o=>o.Destination==command.Destination);
                Require(option!=null,"invalid_effect_move","该格不是当前牌文移动的合法落点。");
                var unit=state.Units.Single(u=>u.Seat==command.ActorSeat);
                var origin=unit.Position;unit.Position=command.Destination;
                Emit(state,command,"UnitMoved",command.ActorSeat,execution.CardId,detail:"CardText");
                var moved=state.Events.Last();moved.From=origin;moved.To=unit.Position;moved.Path=option!.Path;
            }
            execution.Cursor++;state.Pending=null;state.Phase=Phase.Action;
            ContinueCard(catalog,state,command);
        }
    }
}
