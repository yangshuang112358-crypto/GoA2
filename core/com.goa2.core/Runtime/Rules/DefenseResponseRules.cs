#nullable enable
using System.Collections.Generic;
using System.Linq;
using Goa2.Domain;
using Goa2.Rules.Cards;

namespace Goa2.Rules
{
    public sealed partial class GameRules
    {
        public static bool CanDeclineRetaliationDiscard(ContentCatalog catalog,GameState state,int seat)
        {
            // The saved engine version owns this ruling; old response windows keep their options.
            if(state.EngineVersion<10 || LegalForcedDiscards(state,seat).Count==0) return false;
            var response=state.Execution!.DefenseResponse!;
            var program=CardPrograms.Defense(catalog.Card(response.SourceCardId),state.EngineVersion);
            return program!=null && program.Id==response.ProgramId && program.Version==response.ProgramVersion &&
                program.Followup==DefenseFollowup.DiscardAttackerOrDefeat &&
                state.Units.Any(u=>u.Id==response.AttackerUnitId && u.Kind=="hero" && u.Seat==seat);
        }
        private static void DeclineRetaliationDiscard(ContentCatalog catalog,GameState state,Command command)
        {
            Require(CanDeclineRetaliationDiscard(catalog,state,command.ActorSeat),"invalid_retaliation_choice","当前不能选择不弃牌而被击败；请由对应反制的原攻击者本人选择。");
            var response=state.Execution!.DefenseResponse!;
            Emit(state,command,"RetaliationDiscardDeclined",command.ActorSeat);
            response.Cursor++;
            state.Pending=null;state.Phase=Phase.Action;
            DefeatHero(state,command,response.AttackerUnitId,response.ControllerSeat,response.SourceCardId,response.ControllerSeat);
            if(state.Phase!=Phase.Finished) EndCardExecution(catalog,state,command);
        }
        public static List<string> LegalForcedDiscards(GameState state,int seat)
        {
            var response=state.Execution?.DefenseResponse;
            if (state.Phase!=Phase.EffectChoice || state.Pending?.Kind!="forced_discard" || state.Pending.ChooserSeat!=seat ||
                response==null || response.AttackerSeat!=seat || response.Cursor!=0) return new List<string>();
            return state.Players[seat].Cards.Where(c => c.Zone==CardZone.InHand).Select(c => c.CardId).ToList();
        }
        private static bool ContinueDefenseResponse(ContentCatalog catalog,GameState state,Command command)
        {
            var response=state.Execution!.DefenseResponse!;
            var program=CardPrograms.Defense(catalog.Card(response.SourceCardId),state.EngineVersion);
            Require(program != null && program.Id==response.ProgramId && program.Version==response.ProgramVersion,
                "incompatible_program", "防御后处理程序版本不兼容。");
            Require(response.Cursor>=0 && response.Cursor<=1, "invalid_program_cursor", "防御后处理步骤无效。");
            if(response.Cursor==0 && program!.Followup==DefenseFollowup.OptionalStraightMove)
            {
                if(BeginDefenseMove(catalog,state,command,response,program))return false;
                response.Cursor++;
            }
            else if (response.Cursor==0)
            {
                if (state.Players[response.AttackerSeat].Cards.Any(c => c.Zone==CardZone.InHand))
                {
                    state.Phase=Phase.EffectChoice;
                    state.Pending=new PendingChoice
                    {
                        Id="forced-discard:"+(state.Events.Count+1), Kind="forced_discard", ChooserSeat=response.AttackerSeat,
                        Source=response.SourceCardId, SourcePrivateTo=response.ControllerSeat, UnitId=response.AttackerUnitId,
                        ResumeAt="defense_response", Optional=false
                    };
                    Emit(state,command,"ForcedDiscardRequired",response.AttackerSeat);
                    return false;
                }
                Emit(state,command,"ForcedDiscardSkipped",response.AttackerSeat,detail:"empty_hand");
                response.Cursor++;
                if (program!.Followup==DefenseFollowup.DiscardAttackerOrDefeat)
                {
                    DefeatHero(state,command,response.AttackerUnitId,response.ControllerSeat,response.SourceCardId,response.ControllerSeat);
                    if (state.Phase==Phase.Finished) return false;
                }
            }
            if (program!.Followup==DefenseFollowup.DiscardAttackerThenImmunity)
                ApplyDefenseImmunity(catalog,state,command,response);
            Emit(state,command,"DefenseResponseCompleted",response.ControllerSeat,response.SourceCardId,response.ControllerSeat);
            state.Execution.DefenseResponse=null;
            return true;
        }
        private static void ForcedDiscard(ContentCatalog catalog,GameState state,Command command)
        {
            Require(LegalForcedDiscards(state,command.ActorSeat).Contains(command.Value), "invalid_forced_discard", "请由弃牌者本人选择一张当前手牌；此选择不能跳过。");
            var instance=state.Players[command.ActorSeat].Cards.Single(c => c.CardId==command.Value && c.Zone==CardZone.InHand);
            instance.Zone=CardZone.Discarded;
            Emit(state,command,"CardDiscarded",command.ActorSeat,instance.CardId,command.ActorSeat);
            Emit(state,command,"DiscardColorShown",command.ActorSeat,detail:catalog.Card(instance.CardId).Color);
            Emit(state,command,"ForcedDiscardCompleted",command.ActorSeat);
            state.Execution!.DefenseResponse!.Cursor++;
            state.Pending=null; state.Phase=Phase.Action;
            EndCardExecution(catalog,state,command);
        }
        private static void ApplyDefenseImmunity(ContentCatalog catalog,GameState state,Command command,DefenseResponse response)
        {
            state.EffectSequence++;
            var effect=new ActiveEffect
            {
                Id="effect:"+state.EffectSequence, SourceCardId=response.SourceCardId, SourceUnitId=response.SourceUnitId,
                ProtectedUnitId=response.SourceUnitId, SourcePrivateTo=response.ControllerSeat, ControllerSeat=response.ControllerSeat,
                CreatedRound=state.Round, CreatedTurn=state.Turn, CreationOrder=state.EffectSequence,
                Kind=EffectKind.NonAdjacentRangedImmunity, Duration=EffectDuration.ThisTurn, AreaKind=EffectAreaKind.None,
                Window=EffectTimeline.Create(state.Round,state.Turn,catalog.Rules.TurnsPerRound,EffectDuration.ThisTurn)!
            };
            state.Effects.Add(effect);
            Emit(state,command,"EffectCreated",effect.ControllerSeat,effect.SourceCardId,effect.SourcePrivateTo,detail:effect.Id);
            Emit(state,command,"EffectActivated",effect.ControllerSeat,effect.SourceCardId,effect.SourcePrivateTo,detail:effect.Id);
            Emit(state,command,"ProtectionActivated",effect.ControllerSeat,detail:effect.Kind+":"+effect.Id);
        }
    }
}
