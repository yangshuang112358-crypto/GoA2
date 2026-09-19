#nullable enable
using System.Collections.Generic;
using System.Linq;
using Goa2.Domain;
namespace Goa2.Rules
{
 public sealed partial class GameRules
 {
  public static List<string> LegalMinionProtectionCards(GameState state,int seat)
  {
   var pending=state.MinionDefeat;
   if(state.EngineVersion<53 || state.Phase!=Phase.EffectChoice || state.Pending?.Kind!="minion_protection" || state.Pending.ChooserSeat!=seat || pending==null || pending.ProtectorSeat!=seat)return new List<string>();
   return state.Players[seat].Cards.Where(c=>c.Zone==CardZone.InHand).Select(c=>c.CardId).ToList();
  }
  private static bool BeginMinionProtection(ContentCatalog catalog,GameState state,Command command,UnitState unit,string source,int rewardSeat,bool bypassProtection,bool finishActionOnResume,bool resumeCardExecution,bool resumeRoundEnd)
  {
   if(state.EngineVersion<53)return false;
   var effects=EffectRules.MinionDefeatProtectors(catalog,state,unit);
   Require(effects.Count<=1,"competing_prevention_unresolved","多个来源竞争防止击败的顺序尚待裁定。");
   var effect=effects.SingleOrDefault();
   if(effect==null || !state.Players[effect.ControllerSeat].Cards.Any(c=>c.Zone==CardZone.InHand))return false;
   Require(state.MinionDefeat==null,"pending_minion_defeat","请先完成当前小兵保护选择。");
   state.MinionDefeat=new PendingMinionDefeat{UnitId=unit.Id,Source=source,RewardSeat=rewardSeat,ProtectorSeat=effect.ControllerSeat,ProtectionCardId=effect.SourceCardId,
    BypassProtection=bypassProtection,FinishActionOnResume=finishActionOnResume,ResumeCardExecution=resumeCardExecution,ResumeRoundEnd=resumeRoundEnd,
    ResumePhase=state.Phase,ResumeActiveSeat=state.ActiveSeat,ResumePending=state.Pending};
   state.Phase=Phase.EffectChoice;
   state.Pending=new PendingChoice{Id="minion-protection:"+(state.Events.Count+1),Kind="minion_protection",ChooserSeat=effect.ControllerSeat,Source=effect.SourceCardId,UnitId=unit.Id,ResumeAt="minion_defeat",Optional=true};
   Emit(state,command,"MinionProtectionChoiceRequired",effect.ControllerSeat,effect.SourceCardId,detail:unit.Id);
   return true;
  }
  private static void ChooseMinionProtection(ContentCatalog catalog,GameState state,Command command)
  {
   var saved=state.MinionDefeat;
   Require(state.EngineVersion>=53 && state.Phase==Phase.EffectChoice && state.Pending?.Kind=="minion_protection" && state.Pending.ChooserSeat==command.ActorSeat && saved!=null && saved.ProtectorSeat==command.ActorSeat &&
    (command.Value=="skip" || LegalMinionProtectionCards(state,command.ActorSeat).Contains(command.Value)),"invalid_minion_protection","请由保护者选择一张手牌弃置，或明确选择不保护。");
   state.MinionDefeat=null;state.Pending=saved!.ResumePending;state.Phase=saved.ResumePhase;state.ActiveSeat=saved.ResumeActiveSeat;
   if(command.Value!="skip")
   {
    var card=state.Players[command.ActorSeat].Cards.Single(c=>c.CardId==command.Value && c.Zone==CardZone.InHand);card.Zone=CardZone.Discarded;
    Emit(state,command,"CardDiscarded",command.ActorSeat,card.CardId,command.ActorSeat);
    Emit(state,command,"DiscardColorShown",command.ActorSeat,detail:catalog.Card(card.CardId).Color);
    Emit(state,command,"MinionDefeatPrevented",command.ActorSeat,saved.ProtectionCardId,detail:saved.UnitId);
    if(saved.ResumeCardExecution && state.Execution!=null)state.Execution.AttackOutcome="minion_saved";
    if(saved.FinishActionOnResume)FinishAction(catalog,state,command);
    // Sandbox attacks can consume the final hand card while the other players wait in planning.
    if(state.Phase==Phase.Planning && !state.Players[command.ActorSeat].Cards.Any(c=>c.Zone==CardZone.InHand || c.Zone==CardZone.Selected))
    {
     state.Players[command.ActorSeat].Confirmed=true;
     Emit(state,command,"EmptyHandSkipped",command.ActorSeat);
     TryReveal(catalog,state,command);
    }
   }
   else
   {
    Emit(state,command,"MinionProtectionDeclined",command.ActorSeat,saved.ProtectionCardId,detail:saved.UnitId);
    RemoveMinion(catalog,state,command,saved.UnitId,saved.Source,saved.RewardSeat,saved.BypassProtection,saved.FinishActionOnResume,saved.ResumeCardExecution,saved.ResumeRoundEnd,preventionResolved:true);
   }
   if(saved.ResumeCardExecution && state.Frontline==null && state.Pending==null && state.Phase!=Phase.Finished)ContinueCard(catalog,state,command);
  }
 }
}
