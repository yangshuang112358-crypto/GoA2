#nullable enable
using System.Collections.Generic;
using System.Linq;
using Goa2.Domain;
using Goa2.Rules.Cards;

namespace Goa2.Rules
{
    public sealed partial class GameRules
    {
        private const string BeforeActionTargetResume = "before_action_target", BeforeActionDiscardResume = "before_action_discard";

        // The original action must be legal before calling this boundary. Queries never call it.
        private static bool BeginBeforeAction(ContentCatalog catalog, GameState state, Command command)
        {
            var program = UltimateRules.OwnedProgram(catalog, state, command.ActorSeat);
            if (program?.Trigger != UltimateTrigger.BeforeActionAdjacentDiscard) return false;
            Require(state.BeforeAction == null, "pending_before_action", "请先完成当前行动前选择。");
            string source = state.Players[command.ActorSeat].PurpleCardId!;
            Emit(state, command, "UltimateTriggered", command.ActorSeat, source, detail: "before:" + command.Kind);
            var targets = AdjacentPreludeTargets(state, command.ActorSeat);
            if (targets.Count == 0)
            {
                Emit(state, command, "UltimateCompleted", command.ActorSeat, source, detail: "no_targets");
                return false;
            }
            var frame = new BeforeActionFrame
            {
                Id = "before-action:" + (state.Events.Count + 1), SourceCardId = source, ProgramId = program.Id,
                ProgramVersion = program.Version, ControllerSeat = command.ActorSeat,
                ResumeKind = command.Kind, ResumeValue = command.Value, ResumeDestination = command.Destination, ResumeMoveMode = command.MoveMode,
                ParentPhase = state.Phase, ParentActiveSeat = state.ActiveSeat, ParentPending = state.Pending, ParentExecution = state.Execution
            };
            // There is one authoritative copy of the suspended execution, owned by the frame.
            state.BeforeAction = frame; state.Execution = null;
            state.Phase = Phase.EffectChoice;
            state.Pending = new PendingChoice
            {
                Id = frame.Id + ":target", Kind = "effect_target", ChooserSeat = frame.ControllerSeat,
                Source = source, ResumeAt = BeforeActionTargetResume, CandidateUnits = targets, Optional = false
            };
            Emit(state, command, "EffectTargetChoiceRequired", frame.ControllerSeat, source);
            return true;
        }

        private static List<string> AdjacentPreludeTargets(GameState state, int seat)
        {
            var source = state.Units.SingleOrDefault(u => u.Kind == "hero" && u.Seat == seat);
            if (source == null) return new List<string>();
            return state.Units.Where(u => u.Kind == "hero" && u.Seat.HasValue && u.Team != source.Team &&
                u.Position.Distance(source.Position) == 1 && EffectRules.CanAffect(state, seat, u))
                .Select(u => u.Id).OrderBy(id => id, System.StringComparer.Ordinal).ToList();
        }

        private static List<string> BeforeActionTargets(GameState state, int seat) =>
            state.EngineVersion >= 57 && state.Phase == Phase.EffectChoice && state.BeforeAction?.Stage == "target" &&
            state.BeforeAction.ControllerSeat == seat && state.Pending?.Kind == "effect_target" &&
            state.Pending.ResumeAt == BeforeActionTargetResume && state.Pending.ChooserSeat == seat
                ? AdjacentPreludeTargets(state, seat) : new List<string>();

        private static void ChooseBeforeActionTarget(ContentCatalog catalog, GameState state, Command command)
        {
            Require(BeforeActionTargets(state, command.ActorSeat).Contains(command.Value), "invalid_effect_target", "请选择相邻且非免疫的敌方英雄。");
            var frame = state.BeforeAction!; frame.TargetUnitId = command.Value;
            int victim = state.Units.Single(u => u.Id == command.Value).Seat!.Value;
            Emit(state, command, "EffectTargetChosen", frame.ControllerSeat, frame.SourceCardId, detail: command.Value);
            if (!state.Players[victim].Cards.Any(c => c.Zone == CardZone.InHand))
            {
                Emit(state, command, "ForcedDiscardSkipped", victim, frame.SourceCardId, detail: "empty_hand");
                FinishBeforeAction(catalog, state, command); return;
            }
            frame.Stage = "discard";
            state.Pending = new PendingChoice
            {
                Id = frame.Id + ":discard", Kind = "forced_discard", ChooserSeat = victim,
                Source = frame.SourceCardId, UnitId = command.Value, ResumeAt = BeforeActionDiscardResume, Optional = false
            };
            Emit(state, command, "ForcedDiscardRequired", victim, frame.SourceCardId);
        }

        private static bool IsBeforeActionDiscard(GameState state, int seat) =>
            state.EngineVersion >= 57 && state.Phase == Phase.EffectChoice && state.BeforeAction?.Stage == "discard" &&
            state.Pending?.Kind == "forced_discard" && state.Pending.ResumeAt == BeforeActionDiscardResume && state.Pending.ChooserSeat == seat &&
            state.Pending.Source == state.BeforeAction.SourceCardId && state.Pending.UnitId == state.BeforeAction.TargetUnitId &&
            state.Units.Any(u => u.Kind == "hero" && u.Id == state.Pending.UnitId && u.Seat == seat);

        private static void FinishBeforeAction(ContentCatalog catalog, GameState state, Command command)
        {
            var frame = state.BeforeAction!;
            var program = UltimateRules.OwnedProgram(catalog, state, frame.ControllerSeat);
            Require(program?.Id == frame.ProgramId && program.Version == frame.ProgramVersion, "incompatible_program", "行动前程序不兼容。");
            Emit(state, command, "UltimateCompleted", frame.ControllerSeat, frame.SourceCardId, detail: frame.Id);
            state.BeforeAction = null; state.Execution = frame.ParentExecution;
            state.Pending = frame.ParentPending; state.Phase = frame.ParentPhase; state.ActiveSeat = frame.ParentActiveSeat;
            var resume = AsActor(command, frame.ControllerSeat, frame.ResumeValue, destination: frame.ResumeDestination);
            resume.Kind = frame.ResumeKind; resume.MoveMode = frame.ResumeMoveMode;
            switch (frame.ResumeKind)
            {
                case CommandKind.BeginPrimary: BeginPrimary(catalog, state, resume, false); break;
                case CommandKind.Move: Move(catalog, state, resume, false); break;
                case CommandKind.Defend: Defend(catalog, state, resume, false); break;
                case CommandKind.ChooseAttackTarget: ChooseAttackTarget(catalog, state, resume, false); break;
                default: throw new RuleViolation("invalid_before_action", "未知的行动恢复入口。");
            }
        }
    }
}
