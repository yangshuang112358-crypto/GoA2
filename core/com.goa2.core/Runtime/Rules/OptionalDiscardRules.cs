#nullable enable
using System.Collections.Generic;
using System.Linq;
using Goa2.Domain;
using Goa2.Rules.Cards;

namespace Goa2.Rules
{
    public sealed partial class GameRules
    {
        private static bool CanChooseOptionalDiscard(ContentCatalog catalog,GameState state,int seat)
        {
            var execution=state.Execution;
            if(state.Phase!=Phase.EffectChoice || state.Pending?.Kind!="optional_discard" || state.Pending.ChooserSeat!=seat ||
                !state.Pending.Optional || execution==null || execution.ControllerSeat!=seat || state.ActiveSeat!=seat) return false;
            var program=CardPrograms.Primary(catalog.Card(execution.CardId),state.EngineVersion);
            return program!=null && program.Id==execution.ProgramId && program.Version==execution.ProgramVersion &&
                execution.Cursor>=0 && execution.Cursor<program.Instructions.Count && program.Instructions[execution.Cursor]==InstructionKind.ChooseOptionalDiscard;
        }
        public static List<string> LegalOptionalDiscards(ContentCatalog catalog,GameState state,int seat) =>
            CanChooseOptionalDiscard(catalog,state,seat) ? state.Players[seat].Cards.Where(c=>c.Zone==CardZone.InHand).Select(c=>c.CardId).ToList() : new List<string>();
        private static bool BeginOptionalDiscard(GameState state,Command command,CardExecution execution)
        {
            if(!state.Players[execution.ControllerSeat].Cards.Any(c=>c.Zone==CardZone.InHand))
            {
                Emit(state,command,"OptionalDiscardSkipped",execution.ControllerSeat,execution.CardId,detail:"empty_hand");
                return false;
            }
            state.Phase=Phase.EffectChoice;
            state.Pending=new PendingChoice
            {
                Id="optional-discard:"+(state.Events.Count+1),Kind="optional_discard",ChooserSeat=execution.ControllerSeat,
                Source=execution.CardId,UnitId="hero:"+execution.ControllerSeat,ResumeAt="attack_range",Optional=true
            };
            Emit(state,command,"OptionalDiscardRequired",execution.ControllerSeat,execution.CardId);
            return true;
        }
        private static void ChooseOptionalDiscard(ContentCatalog catalog,GameState state,Command command)
        {
            Require(CanChooseOptionalDiscard(catalog,state,command.ActorSeat) &&
                (command.Value=="skip" || LegalOptionalDiscards(catalog,state,command.ActorSeat).Contains(command.Value)),
                "invalid_optional_discard","请由本人选择一张当前手牌，或明确选择不弃牌。");
            var execution=state.Execution!;
            if(command.Value=="skip") Emit(state,command,"OptionalDiscardSkipped",command.ActorSeat,execution.CardId,detail:"declined");
            else
            {
                var instance=state.Players[command.ActorSeat].Cards.Single(c=>c.CardId==command.Value && c.Zone==CardZone.InHand);
                instance.Zone=CardZone.Discarded; execution.PreAttackDiscarded=true;
                Emit(state,command,"CardDiscarded",command.ActorSeat,instance.CardId,command.ActorSeat);
                Emit(state,command,"DiscardColorShown",command.ActorSeat,detail:catalog.Card(instance.CardId).Color);
                Emit(state,command,"OptionalDiscardCompleted",command.ActorSeat,execution.CardId);
            }
            execution.Cursor++; state.Pending=null; state.Phase=Phase.Action;
            ContinueCard(catalog,state,command);
        }
    }
}
