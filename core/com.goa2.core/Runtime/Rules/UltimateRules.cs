#nullable enable
using System.Collections.Generic;
using System.Linq;
using Goa2.Domain;
using Goa2.Rules.Cards;

namespace Goa2.Rules
{
    public static class UltimateRules
    {
        public static bool HasProgram(CardDefinition card, int engine = GameState.CurrentEngineVersion) =>
            UltimatePrograms.Find(card, engine) != null;
    }

    public sealed partial class GameRules
    {
        private const string UltimateTargetResume = "primary_completion_target";
        private const string UltimateDiscardResume = "primary_completion_discard";

        private static bool BeginPrimaryCompletion(ContentCatalog catalog, GameState state, Command command)
        {
            var execution = state.Execution!;
            if (state.EngineVersion < 55 || execution.ActionStopped) return false;
            if (execution.Completion != null) return execution.Completion.Stage != "done";
            var owner = state.Players[execution.ControllerSeat];
            var ability = catalog.Cards.FirstOrDefault(c => c.Id == owner.PurpleCardId && c.HeroId == owner.HeroId);
            var program = ability == null ? null : UltimatePrograms.Find(ability, state.EngineVersion);
            if (program == null || program.Trigger != UltimateTrigger.AfterBasicSkillDiscard ||
                catalog.Card(execution.CardId).PrimaryCategory != "基础技能" ||
                !state.Units.Any(u => u.Kind == "hero" && u.Seat == execution.ControllerSeat)) return false;

            execution.Completion = new ActionCompletionProgress
            {
                SourceCardId = ability!.Id, ProgramId = program.Id, ProgramVersion = program.Version
            };
            Emit(state, command, "UltimateTriggered", execution.ControllerSeat, ability.Id, detail: execution.CardId);
            var targets = CompletionTargets(state);
            if (targets.Count == 0)
            {
                CompleteUltimate(state, command, "no_targets");
                return false;
            }
            state.Phase = Phase.EffectChoice;
            state.Pending = new PendingChoice
            {
                Id = "completion-target:" + (state.Events.Count + 1), Kind = "effect_target",
                ChooserSeat = execution.ControllerSeat, Source = ability.Id, ResumeAt = UltimateTargetResume,
                CandidateUnits = targets, Optional = false
            };
            Emit(state, command, "EffectTargetChoiceRequired", execution.ControllerSeat, ability.Id);
            return true;
        }

        private static List<string> CompletionTargets(GameState state)
        {
            var execution = state.Execution;
            if (state.EngineVersion < 55 || execution?.Completion?.Stage != "target") return new List<string>();
            var source = state.Units.SingleOrDefault(u => u.Kind == "hero" && u.Seat == execution.ControllerSeat);
            if (source == null) return new List<string>();
            // D-034: empty hands remain legal; immunity and team still filter the target.
            return state.Units.Where(u => u.Kind == "hero" && u.Seat.HasValue && u.Team != source.Team &&
                    EffectRules.CanAffect(state, execution.ControllerSeat, u))
                .Select(u => u.Id).OrderBy(id => id, System.StringComparer.Ordinal).ToList();
        }

        private static void ChooseCompletionTarget(ContentCatalog catalog, GameState state, Command command)
        {
            Require(state.Phase == Phase.EffectChoice && state.Pending?.Kind == "effect_target" &&
                state.Pending.ResumeAt == UltimateTargetResume && state.Pending.ChooserSeat == command.ActorSeat &&
                state.Execution?.ControllerSeat == command.ActorSeat && CompletionTargets(state).Contains(command.Value),
                "invalid_effect_target", "请由来源英雄选择场上合法的敌方英雄。");
            var execution = state.Execution!; var progress = execution.Completion!;
            progress.TargetUnitId = command.Value;
            Emit(state, command, "EffectTargetChosen", command.ActorSeat, progress.SourceCardId, detail: command.Value);
            int victim = state.Units.Single(u => u.Id == command.Value).Seat!.Value;
            if (!state.Players[victim].Cards.Any(c => c.Zone == CardZone.InHand))
            {
                Emit(state, command, "ForcedDiscardSkipped", victim, progress.SourceCardId, detail: "empty_hand");
                CompleteUltimate(state, command, "empty_hand");
                EndCardExecution(catalog, state, command);
                return;
            }
            progress.Stage = "discard";
            state.Pending = new PendingChoice
            {
                Id = "completion-discard:" + (state.Events.Count + 1), Kind = "forced_discard", ChooserSeat = victim,
                UnitId = command.Value, Source = progress.SourceCardId, ResumeAt = UltimateDiscardResume, Optional = false
            };
            Emit(state, command, "ForcedDiscardRequired", victim, progress.SourceCardId);
        }

        private static bool IsCompletionDiscard(GameState state, int seat) => state.EngineVersion >= 55 &&
            state.Phase == Phase.EffectChoice && state.Pending?.Kind == "forced_discard" &&
            state.Pending.ResumeAt == UltimateDiscardResume && state.Pending.ChooserSeat == seat &&
            state.Execution?.Completion?.Stage == "discard" &&
            state.Execution.Completion.SourceCardId == state.Pending.Source &&
            state.Execution.Completion.TargetUnitId == state.Pending.UnitId &&
            state.Units.Any(u => u.Kind == "hero" && u.Seat == seat && u.Id == state.Pending.UnitId);

        private static void CompleteUltimate(GameState state, Command command, string outcome)
        {
            var execution = state.Execution!; var progress = execution.Completion!;
            progress.Stage = "done";
            Emit(state, command, "UltimateCompleted", execution.ControllerSeat, progress.SourceCardId, detail: outcome);
            state.Pending = null; state.Phase = Phase.Action;
        }
    }
}
