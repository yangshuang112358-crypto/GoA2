#nullable enable
using System.Collections.Generic;
using System.Linq;
using Goa2.Domain;
using Goa2.Rules.Cards;
namespace Goa2.Rules
{
 public sealed partial class GameRules
 {
  private static PrimaryProgram? PrimaryOptionProgram(ContentCatalog catalog,GameState state)
  {
   var e=state.Execution;if(state.EngineVersion<50 || e==null)return null;
   var p=CardPrograms.Primary(catalog.Card(e.CardId),state.EngineVersion);
   return p!=null && p.Id==e.ProgramId && p.Version==e.ProgramVersion && e.Cursor>=0 && e.Cursor<p.Instructions.Count && p.Instructions[e.Cursor]==InstructionKind.ChooseProtectionOrSelfRecovery?p:null;
  }
  public static List<string> LegalPrimaryOptions(ContentCatalog catalog,GameState state,int seat)
  {
   if(state.Pending?.ResumeAt==UltimateRepeatResume)return LegalUltimateRepeat(state,seat);
   if(state.Pending?.ResumeAt==ActionBattleOfferResume)return LegalActionBattleOptions(state,seat);
   if(state.Phase!=Phase.EffectChoice || state.Pending?.Kind!="primary_option" || state.Pending.ChooserSeat!=seat || state.Execution?.ControllerSeat!=seat || state.ActiveSeat!=seat || PrimaryOptionProgram(catalog,state)==null)return new List<string>();
   var result=new List<string>{"protect"};if(!state.Players[seat].Cards.Any(c=>c.Zone==CardZone.Discarded))result.Add("recover");return result;
  }
  private static void BeginPrimaryOption(ContentCatalog catalog,GameState state,Command command)
  {
   var e=state.Execution!;state.Phase=Phase.EffectChoice;
   state.Pending=new PendingChoice{Id="primary-option:"+(state.Events.Count+1),Kind="primary_option",ChooserSeat=e.ControllerSeat,Source=e.CardId,ResumeAt="protection_or_self_recovery",Optional=false};
   Emit(state,command,"PrimaryOptionRequired",e.ControllerSeat,e.CardId);
  }
  private static void ChoosePrimaryOption(ContentCatalog catalog,GameState state,Command command)
  {
   if(state.Pending?.ResumeAt==UltimateRepeatResume){ChooseUltimateRepeat(catalog,state,command);return;}
   if(state.Pending?.ResumeAt==ActionBattleOfferResume){ChooseActionBattleOption(catalog,state,command);return;}
   Require(LegalPrimaryOptions(catalog,state,command.ActorSeat).Contains(command.Value),"invalid_primary_option","请由来源英雄选择一项当前可用效果；弃牌堆非空时不能取回此牌。");
   var e=state.Execution!;var program=PrimaryOptionProgram(catalog,state)!;
   Emit(state,command,"PrimaryOptionChosen",command.ActorSeat,e.CardId,detail:command.Value);
   if(command.Value=="protect")ApplyTimedEffect(catalog,state,command,e,program);
   else e.ReturnSourceAtEnd=true;
   e.Cursor++;state.Pending=null;state.Phase=Phase.Action;ContinueCard(catalog,state,command);
  }
 }
}
