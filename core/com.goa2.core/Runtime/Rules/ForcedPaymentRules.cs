#nullable enable
using System.Linq;
using Goa2.Domain;
using Goa2.Rules.Cards;

namespace Goa2.Rules
{
    public sealed partial class GameRules
    {
        private const string ForcedPaymentResume = "card_forced_payment";

        // All new push instructions must notify this boundary only after a legal push was performed.
        private static void AfterHeroPushed(ContentCatalog catalog, GameState state, Command command, CardExecution execution, UnitState target)
        {
            var program = UltimateRules.OwnedProgram(catalog, state, execution.ControllerSeat);
            if (program?.Trigger != UltimateTrigger.AfterPushDiscardOrDefeat || target.Kind != "hero" || !target.Seat.HasValue ||
                target.Team == state.Players[execution.ControllerSeat].Team || !EffectRules.CanAffect(state, execution.ControllerSeat, target)) return;
            string source = state.Players[execution.ControllerSeat].PurpleCardId!;
            Emit(state, command, "UltimateTriggered", execution.ControllerSeat, source, detail: target.Id);
            BeginForcedPayment(state, command, execution.ControllerSeat, source, target);
        }

        private static void BeginForcedPayment(GameState state, Command command, int controller, string source, UnitState target)
        {
            int chooser = target.Seat!.Value;
            if (!state.Players[chooser].Cards.Any(c => c.Zone == CardZone.InHand))
            {
                Emit(state, command, "ForcedPaymentDefeat", chooser, source, detail: "empty_hand");
                DefeatHero(state, command, target.Id, controller, source);
                return;
            }
            Require(state.Execution != null && state.Execution.ForcedPayment == null, "nested_payment", "先完成当前弃牌或被击败选择。");
            state.Execution!.ForcedPayment = new ForcedPaymentProgress { ControllerSeat = controller, SourceCardId = source, TargetUnitId = target.Id };
            state.Phase = Phase.EffectChoice;
            state.Pending = new PendingChoice
            {
                Id = "forced-payment:" + (state.Events.Count + 1), Kind = "forced_discard", ChooserSeat = chooser,
                Source = source, UnitId = target.Id, ResumeAt = ForcedPaymentResume, Optional = false
            };
            Emit(state, command, "ForcedDiscardRequired", chooser, source);
        }

        private static bool IsForcedPayment(GameState state, int seat) => state.EngineVersion >= 56 &&
            state.Phase == Phase.EffectChoice && state.Pending?.Kind == "forced_discard" &&
            state.Pending.ResumeAt == ForcedPaymentResume && state.Pending.ChooserSeat == seat &&
            state.Execution?.ForcedPayment != null && state.Execution.ForcedPayment.TargetUnitId == state.Pending.UnitId &&
            state.Execution.ForcedPayment.SourceCardId == state.Pending.Source &&
            state.Units.Any(u => u.Id == state.Pending.UnitId && u.Kind == "hero" && u.Seat == seat);

        private static void FinishForcedPayment(ContentCatalog catalog, GameState state, Command command, bool defeat)
        {
            var payment = state.Execution!.ForcedPayment!;
            state.Execution.ForcedPayment = null;
            state.Pending = null; state.Phase = Phase.Action;
            Emit(state, command, defeat ? "ForcedPaymentDefeat" : "ForcedPaymentPaid", command.ActorSeat, payment.SourceCardId,
                detail: defeat ? "declined" : "discarded");
            if (defeat) DefeatHero(state, command, payment.TargetUnitId, payment.ControllerSeat, payment.SourceCardId);
            if (state.Phase != Phase.Finished) ContinueCard(catalog, state, command);
        }
    }
}
