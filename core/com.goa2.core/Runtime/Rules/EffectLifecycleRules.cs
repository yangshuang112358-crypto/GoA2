#nullable enable
using System.Linq;
using Goa2.Domain;
using Goa2.Rules.Cards;

namespace Goa2.Rules
{
    public sealed partial class GameRules
    {
        private static void CancelRetrievedCardEffects(GameState state,Command command,int owner,string cardId)
        {
            if(state.EngineVersion<16)return;
            foreach(var effect in state.Effects.Where(e=>e.ControllerSeat==owner && e.SourceCardId==cardId).OrderBy(e=>e.CreationOrder).ToList())
            {
                state.Effects.Remove(effect);
                Emit(state,command,"EffectCancelled",owner,cardId,effect.SourcePrivateTo,detail:"card_retrieved|"+effect.Id);
                if(effect.SourcePrivateTo.HasValue)Emit(state,command,"ProtectionExpired",owner,detail:effect.Kind+":"+effect.Id);
            }
        }
        private static void ApplyTimedEffect(ContentCatalog catalog, GameState state, Command command, CardExecution execution, PrimaryProgram program)
        {
            Require(program.Effect.HasValue, "invalid_effect_program", "卡牌缺少持续效果定义。");
            var window = EffectTimeline.Create(state.Round,state.Turn,catalog.Rules.TurnsPerRound,program.Duration);
            if (window == null)
            {
                Emit(state,command,"EffectNotScheduled",execution.ControllerSeat,execution.CardId,detail:"next_turn_outside_round");
                return;
            }
            state.EffectSequence++;
            var effect = new ActiveEffect
            {
                Id = "effect:" + state.EffectSequence, SourceCardId = execution.CardId, SourceUnitId = "hero:" + execution.ControllerSeat,
                ControllerSeat = execution.ControllerSeat, CreatedRound = state.Round, CreatedTurn = state.Turn, CreationOrder = state.EffectSequence,
                Kind = program.Effect!.Value, Duration = program.Duration, AreaKind=program.AreaKind, Window = window
            };
            state.Effects.Add(effect);
            Emit(state,command,"EffectCreated",effect.ControllerSeat,effect.SourceCardId,detail:effect.Id);
            Emit(state,command,EffectTimeline.Active(window,state.Round,state.Turn) ? "EffectActivated" : "EffectScheduled",effect.ControllerSeat,effect.SourceCardId,detail:effect.Id);
        }
        private static void ExpireEffectsAtTurnBoundary(GameState state, Command command)
        {
            foreach (var effect in state.Effects.Where(e => EffectTimeline.EndsAtBoundary(e.Window,state.Round,state.Turn)).OrderBy(e => e.CreationOrder).ToList())
            {
                state.Effects.Remove(effect);
                Emit(state,command,"EffectExpired",effect.ControllerSeat,effect.SourceCardId,effect.SourcePrivateTo,detail:effect.Id);
                if (effect.SourcePrivateTo.HasValue) Emit(state,command,"ProtectionExpired",effect.ControllerSeat,detail:effect.Kind+":"+effect.Id);
            }
        }
        private static void CancelAdjacentSkillEffects(ContentCatalog catalog,GameState state,Command command,CardExecution execution)
        {
            foreach(string id in EffectRules.CancellableAdjacentSkills(catalog,state,execution.ControllerSeat))
            {
                var effect=state.Effects.Single(e => e.Id==id); state.Effects.Remove(effect);
                Emit(state,command,"EffectCancelled",execution.ControllerSeat,effect.SourceCardId,detail:execution.CardId+"|"+effect.Id);
            }
        }
        private static void ActivateScheduledEffects(GameState state, Command command)
        {
            foreach (var effect in state.Effects.Where(e => e.Window.StartRound == state.Round && e.Window.StartTurn == state.Turn &&
                (e.CreatedRound < state.Round || e.CreatedTurn < state.Turn)).OrderBy(e => e.CreationOrder))
                Emit(state,command,"EffectActivated",effect.ControllerSeat,effect.SourceCardId,effect.SourcePrivateTo,detail:effect.Id);
        }
    }
}
