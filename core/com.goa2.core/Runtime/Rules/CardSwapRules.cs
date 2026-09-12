#nullable enable
using System.Collections.Generic;
using System.Linq;
using Goa2.Domain;
using Goa2.Rules.Cards;

namespace Goa2.Rules
{
    public sealed partial class GameRules
    {
        private static DefenseProgram? CardSwapProgram(ContentCatalog catalog,GameState state,int seat)
        {
            var response=state.Execution?.DefenseResponse;
            if(state.Phase!=Phase.EffectChoice || state.Pending?.Kind!="card_swap" || state.Pending.ResumeAt!="defense_response_swap" ||
                !state.Pending.Optional || state.Pending.ChooserSeat!=seat || response==null || response.ControllerSeat!=seat || response.Cursor!=1)return null;
            var program=CardPrograms.Defense(catalog.Card(response.SourceCardId),state.EngineVersion);
            return program!=null && program.Id==response.ProgramId && program.Version==response.ProgramVersion && program.SwapAfterMove ? program : null;
        }
        public static List<string> LegalCardSwaps(ContentCatalog catalog,GameState state,int seat)
        {
            if(CardSwapProgram(catalog,state,seat)==null)return new List<string>();
            var response=state.Execution!.DefenseResponse!;var cards=state.Players[seat].Cards;
            return cards.Any(c=>c.CardId==response.SourceCardId && c.Zone==CardZone.Discarded) ? cards.Where(c=>c.Zone==CardZone.InHand).Select(c=>c.CardId).ToList() : new List<string>();
        }
        private static bool BeginCardSwap(GameState state,Command command,DefenseResponse response)
        {
            var cards=state.Players[response.ControllerSeat].Cards;
            if(!cards.Any(c=>c.CardId==response.SourceCardId && c.Zone==CardZone.Discarded) || !cards.Any(c=>c.Zone==CardZone.InHand))
            {
                Emit(state,command,"CardSwapSkipped",response.ControllerSeat,response.SourceCardId,response.ControllerSeat,"no_candidates");return false;
            }
            state.Phase=Phase.EffectChoice;
            state.Pending=new PendingChoice{Id="card-swap:"+(state.Events.Count+1),Kind="card_swap",ChooserSeat=response.ControllerSeat,
                Source=response.SourceCardId,SourcePrivateTo=response.ControllerSeat,UnitId=response.SourceUnitId,ResumeAt="defense_response_swap",Optional=true};
            Emit(state,command,"CardSwapChoiceRequired",response.ControllerSeat,response.SourceCardId,response.ControllerSeat);return true;
        }
        private static void ChooseCardSwap(ContentCatalog catalog,GameState state,Command command)
        {
            Require(CardSwapProgram(catalog,state,command.ActorSeat)!=null && (command.Value=="skip" || LegalCardSwaps(catalog,state,command.ActorSeat).Contains(command.Value)),
                "invalid_card_swap","请由防御者本人选择一张当前手牌交换，或明确不交换。");
            var response=state.Execution!.DefenseResponse!;
            if(command.Value=="skip")Emit(state,command,"CardSwapSkipped",response.ControllerSeat,response.SourceCardId,response.ControllerSeat,"declined");
            else
            {
                var cards=state.Players[command.ActorSeat].Cards;var source=cards.Single(c=>c.CardId==response.SourceCardId);var hand=cards.Single(c=>c.CardId==command.Value);
                // Card-zone exchange preserves each physical card's play history and emits its own fact.
                var oldSource=source.Zone;source.Zone=hand.Zone;hand.Zone=oldSource;
                CancelCardStateEffects(state,command,command.ActorSeat,source.CardId,"cards_swapped");CancelCardStateEffects(state,command,command.ActorSeat,hand.CardId,"cards_swapped");
                Emit(state,command,"CardsSwapped",command.ActorSeat,source.CardId,command.ActorSeat,hand.CardId);
                Emit(state,command,"CardSwapColorsShown",command.ActorSeat,detail:catalog.Card(source.CardId).Color+":"+catalog.Card(hand.CardId).Color);
            }
            response.Cursor++;state.Pending=null;state.Phase=Phase.Action;EndCardExecution(catalog,state,command);
        }
    }
}
