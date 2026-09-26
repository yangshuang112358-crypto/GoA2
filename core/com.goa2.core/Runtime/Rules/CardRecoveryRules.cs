#nullable enable
using System.Collections.Generic;
using System.Linq;
using Goa2.Domain;
using Goa2.Rules.Cards;

namespace Goa2.Rules
{
    public sealed partial class GameRules
    {
        private static bool RecoverableZone(CardInstance card,PrimaryProgram program) => card.Zone==CardZone.Discarded || program.RecoverResolved && card.Zone==CardZone.PlayedResolved;
        private static int? RecoveryRecipient(GameState state,CardExecution execution,PrimaryProgram program) => program.HeroTarget==HeroTargetKind.None ?
            execution.ControllerSeat : state.Units.SingleOrDefault(u=>u.Id==execution.TargetUnitId && u.Kind=="hero")?.Seat;
        private static bool RecoveryCondition(ContentCatalog catalog,GameState state,int seat,CardExecution execution,PrimaryProgram program)
        {
            var source=state.Units.SingleOrDefault(u=>u.Seat==seat);
            return source!=null && (program.HeroTarget==HeroTargetKind.None || HeroTargets(catalog,state,execution,program).Contains(source.Id)) &&
                (!program.RecoveryRequiresAdjacentMinion || state.Units.Any(u=>
                (u.Kind=="melee" || u.Kind=="ranged" || u.Kind=="heavy") && u.Position.Distance(source.Position)==1));
        }
        private static PrimaryProgram? RecoveryProgram(ContentCatalog catalog,GameState state,int seat)
        {
            var execution=state.Execution;
            if(state.Phase!=Phase.EffectChoice || state.Pending?.Kind!="recover_discard" || state.Pending.ChooserSeat!=seat ||
                !state.Pending.Optional || execution==null || state.ActiveSeat!=execution.ControllerSeat) return null;
            var program=CardPrograms.Primary(catalog.Card(execution.CardId),state.EngineVersion);
            return program!=null && RecoveryRecipient(state,execution,program)==seat && program.Id==execution.ProgramId && program.Version==execution.ProgramVersion && execution.Cursor>=0 &&
                execution.Cursor<program.Instructions.Count && program.Instructions[execution.Cursor]==InstructionKind.OptionalRecoverDiscard ? program : null;
        }
        public static List<string> LegalRecoveries(ContentCatalog catalog,GameState state,int seat)
        {
            var program=RecoveryProgram(catalog,state,seat);
            return program!=null && RecoveryCondition(catalog,state,seat,state.Execution!,program) ? state.Players[seat].Cards.Where(c=>RecoverableZone(c,program)).Select(c=>c.CardId).ToList() : new List<string>();
        }
        private static bool BeginRecovery(ContentCatalog catalog,GameState state,Command command,CardExecution execution,PrimaryProgram program)
        {
            int? recipient=RecoveryRecipient(state,execution,program);
            if(!recipient.HasValue || !RecoveryCondition(catalog,state,recipient.Value,execution,program) || !state.Players[recipient.Value].Cards.Any(c=>RecoverableZone(c,program)))
            {
                Emit(state,command,"RecoverDiscardSkipped",execution.ControllerSeat,execution.CardId,detail:"no_candidates");return false;
            }
            state.Phase=Phase.EffectChoice;
            state.Pending=new PendingChoice { Id="recover-discard:"+(state.Events.Count+1),Kind="recover_discard",ChooserSeat=recipient.Value,
                Source=execution.CardId,UnitId="hero:"+recipient.Value,ResumeAt="card_recovery",Optional=true };
            Emit(state,command,"RecoverDiscardRequired",recipient.Value,execution.CardId);return true;
        }
        private static void ChooseRecoveredCard(ContentCatalog catalog,GameState state,Command command)
        {
            Require(RecoveryProgram(catalog,state,command.ActorSeat)!=null && (command.Value=="skip" || LegalRecoveries(catalog,state,command.ActorSeat).Contains(command.Value)),
                "invalid_recovery_choice","请由本人选择牌文允许取回的卡牌，或明确不取回。");
            var execution=state.Execution!;
            if(command.Value=="skip") Emit(state,command,"RecoverDiscardSkipped",command.ActorSeat,execution.CardId,detail:"declined");
            else
            {
                var card=state.Players[command.ActorSeat].Cards.Single(c=>c.CardId==command.Value);
                card.Zone=CardZone.InHand;
                if(state.EngineVersion>=64)execution.RecoveredCard=true;
                CancelRetrievedCardEffects(state,command,command.ActorSeat,card.CardId);
                Emit(state,command,"CardRecovered",command.ActorSeat,card.CardId,command.ActorSeat);
                Emit(state,command,"RecoveredColorShown",command.ActorSeat,detail:catalog.Card(card.CardId).Color);
                Emit(state,command,"RecoverDiscardCompleted",command.ActorSeat,execution.CardId);
            }
            execution.Cursor++;state.Pending=null;state.Phase=Phase.Action;ContinueCard(catalog,state,command);
        }
    }
}
