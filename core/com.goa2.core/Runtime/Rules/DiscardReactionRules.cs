#nullable enable
using System.Collections.Generic;
using System.Linq;
using Goa2.Domain;
using Goa2.Rules.Cards;

namespace Goa2.Rules
{
    public sealed partial class GameRules
    {
        private static string NewActionInstance(GameState state) => "action:" + (state.Events.Count + 1);

        private static void DiscardCard(GameState state, Command command, CardInstance card, int owner, string reason)
        {
            card.Zone = CardZone.Discarded;
            Emit(state,command,"CardDiscarded",owner,card.CardId,owner);
            if (state.EngineVersion < 63) return;
            string cause = state.Execution?.ActionInstanceId ?? state.BeforeAction?.ParentExecution?.ActionInstanceId ?? "command:" + command.Id;
            foreach (var effect in state.Effects.Where(e => e.ControllerSeat == owner && e.Kind == EffectKind.AttackFromDiscard &&
                EffectTimeline.Active(e.Window,state.Round,state.Turn)).OrderBy(e => e.CreationOrder))
            {
                var trigger = new DiscardReaction
                {
                    Id = "discard-reaction:" + (state.Events.Count + 1), CauseActionId = cause,
                    ControllerSeat = owner, SourceCardId = effect.SourceCardId, EffectId = effect.Id,
                    DiscardedCardId = card.CardId, Reason = reason
                };
                if (state.DiscardReactions == null) state.DiscardReactions = new List<DiscardReaction>();
                state.DiscardReactions.Add(trigger);
                Emit(state,command,"DiscardReactionQueued",owner,effect.SourceCardId,owner,detail:trigger.Id);
            }
        }

        private static List<string> DiscardAttackCandidates(ContentCatalog catalog, GameState state, int seat)
        {
            var source = state.Units.SingleOrDefault(u => u.Kind == "hero" && u.Seat == seat);
            if (source == null) return new List<string>();
            return state.Players[seat].Cards.Where(c => c.Zone == CardZone.Discarded).Select(c => catalog.Card(c.CardId))
                .Where(card =>
                {
                    var program = CardPrograms.Primary(card,state.EngineVersion);
                    return card.PrimaryFamily == "attack" && program != null && program.Instructions.Contains(InstructionKind.ChooseAttackTarget) &&
                        CombatRules.Targets(catalog,state,source,card,program,useExecution:false).Count > 0;
                }).Select(c => c.Id).ToList();
        }

        public static List<string> LegalDiscardAttacks(ContentCatalog catalog, GameState state, int seat)
        {
            var frame = state.DiscardReactionFrames?.LastOrDefault();
            return state.EngineVersion >= 63 && state.Phase == Phase.EffectChoice && state.Pending?.Kind == "discard_attack" &&
                state.Pending.ChooserSeat == seat && frame?.Reaction.ControllerSeat == seat && state.Execution == null && state.BeforeAction == null
                    ? DiscardAttackCandidates(catalog,state,seat) : new List<string>();
        }

        // Called only at a complete action-instance boundary, never inside DiscardCard.
        private static bool BeginDiscardReactions(ContentCatalog catalog, GameState state, Command command, string? cause = null)
        {
            if (state.EngineVersion < 63 || state.Phase == Phase.Finished || state.DiscardReactions == null) return false;
            cause = cause ?? state.Execution?.ActionInstanceId;
            if (cause == null) return false;
            while (true)
            {
                var reaction = state.DiscardReactions?.FirstOrDefault(r => r.CauseActionId == cause);
                if (reaction == null) return false;
                state.DiscardReactions!.Remove(reaction);
                if (state.DiscardReactions.Count == 0) state.DiscardReactions = null;
                var choices = DiscardAttackCandidates(catalog,state,reaction.ControllerSeat);
                if (choices.Count == 0)
                {
                    Emit(state,command,"DiscardReactionSkipped",reaction.ControllerSeat,reaction.SourceCardId,detail:"no_legal_attack");
                    continue;
                }
                var frame = new DiscardReactionFrame
                {
                    Reaction = reaction, ParentExecution = state.Execution, ParentPhase = state.Phase,
                    ParentActiveSeat = state.ActiveSeat, ParentPending = state.Pending
                };
                if (state.DiscardReactionFrames == null) state.DiscardReactionFrames = new List<DiscardReactionFrame>();
                state.DiscardReactionFrames.Add(frame);
                state.Execution = null; state.ActiveSeat = reaction.ControllerSeat; state.Phase = Phase.EffectChoice;
                state.Pending = new PendingChoice
                {
                    Id = reaction.Id + ":card", Kind = "discard_attack", ChooserSeat = reaction.ControllerSeat,
                    Source = reaction.SourceCardId, ResumeAt = "discard_attack_card", Optional = false
                };
                Emit(state,command,"DiscardAttackChoiceRequired",reaction.ControllerSeat,reaction.SourceCardId);
                return true;
            }
        }

        private static void ChooseDiscardAttack(ContentCatalog catalog, GameState state, Command command, bool allowBeforeAction = true)
        {
            Require(LegalDiscardAttacks(catalog,state,command.ActorSeat).Contains(command.Value),"invalid_discard_attack",
                "请由反击者本人选择弃牌堆中当前可执行的攻击牌。");
            if (allowBeforeAction && BeginBeforeAction(catalog,state,command)) return;
            var program = CardPrograms.Primary(catalog.Card(command.Value),state.EngineVersion)!;
            state.Execution = new CardExecution
            {
                CardId = command.Value, ControllerSeat = command.ActorSeat, ProgramId = program.Id, ProgramVersion = program.Version,
                ActionInstanceId = NewActionInstance(state), FromDiscard = true
            };
            state.Pending = null; state.Phase = Phase.Action;
            Emit(state,command,"DiscardAttackStarted",command.ActorSeat,command.Value);
            ContinueCard(catalog,state,command);
        }

        private static bool CompleteDiscardAttack(ContentCatalog catalog, GameState state, Command command)
        {
            if (state.Execution?.FromDiscard != true) return false;
            var execution = state.Execution;
            var frame = state.DiscardReactionFrames!.Last();
            state.DiscardReactionFrames.RemoveAt(state.DiscardReactionFrames.Count - 1);
            if (state.DiscardReactionFrames.Count == 0) state.DiscardReactionFrames = null;
            Emit(state,command,"DiscardAttackCompleted",execution.ControllerSeat,execution.CardId);
            state.Execution = frame.ParentExecution; state.ActiveSeat = frame.ParentActiveSeat;
            state.Phase = frame.ParentPhase; state.Pending = frame.ParentPending;
            if (state.Execution != null) ContinueCard(catalog,state,command);
            else BeginDiscardReactions(catalog,state,command,frame.Reaction.CauseActionId);
            return true;
        }
    }
}
