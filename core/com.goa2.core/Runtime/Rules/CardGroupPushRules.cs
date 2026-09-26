#nullable enable
using System.Collections.Generic;
using System.Linq;
using Goa2.Domain;
using Goa2.Rules.Cards;
namespace Goa2.Rules
{
 public sealed partial class GameRules
 {
  private static PrimaryProgram? GroupPushProgram(ContentCatalog catalog,GameState state,InstructionKind instruction)
  {
   var e=state.Execution;if(state.EngineVersion<48 || e==null)return null;
   var p=CardPrograms.Primary(catalog.Card(e.CardId),state.EngineVersion);
   return p!=null && p.Id==e.ProgramId && p.Version==e.ProgramVersion && e.Cursor>=0 && e.Cursor<p.Instructions.Count && p.Instructions[e.Cursor]==instruction?p:null;
  }
  private static List<string> GroupPushTargets(ContentCatalog catalog,GameState state)
  {
   var e=state.Execution;if(e==null)return new List<string>();
   if(GroupPushProgram(catalog,state,InstructionKind.DiscardBlockedPushHeroesIfAble)!=null)
    return (e.AffectedHeroTargets??new List<string>()).Where(id=>state.Units.Any(u=>u.Id==id && u.Kind=="hero" && u.Seat.HasValue && EffectRules.CanAffect(state,e.ControllerSeat,u))).ToList();
   if(GroupPushProgram(catalog,state,InstructionKind.PushAllAdjacentEnemies)==null)return new List<string>();
   var source=state.Units.SingleOrDefault(u=>u.Seat==e.ControllerSeat);if(source==null)return new List<string>();
   var minions=new HashSet<string>(LegalMinionRemovals(state));
   return state.Units.Where(u=>u.Team!=source.Team && EffectRules.CanDisplace(catalog,state,e.ControllerSeat,u) && u.Position.Distance(source.Position)==1 && (u.Kind=="hero" || minions.Contains(u.Id)) && (e.RemainingUnitTargets==null || e.RemainingUnitTargets.Contains(u.Id))).Select(u=>u.Id).OrderBy(id=>id,System.StringComparer.Ordinal).ToList();
  }
  private static bool BeginGroupPush(ContentCatalog catalog,GameState state,Command command)
  {
   var e=state.Execution!;if(e.RemainingUnitTargets==null)e.RemainingUnitTargets=GroupPushTargets(catalog,state);
   var targets=GroupPushTargets(catalog,state);
   if(targets.Count==0){e.RemainingUnitTargets=null;Emit(state,command,"PushGroupCompleted",e.ControllerSeat,e.CardId);return false;}
   SetGroupPushChoice(state,command,targets,"push_all_adjacent");return true;
  }
  private static bool BeginBlockedPushDiscard(ContentCatalog catalog,GameState state,Command command)
  {
   var targets=GroupPushTargets(catalog,state);if(targets.Count==0){state.Execution!.AffectedHeroTargets=null;return false;}
   SetGroupPushChoice(state,command,targets,"blocked_push_discard_target");return true;
  }
  private static void SetGroupPushChoice(GameState state,Command command,List<string> targets,string resume)
  {
   var e=state.Execution!;state.Phase=Phase.EffectChoice;
   state.Pending=new PendingChoice{Id="group-push:"+(state.Events.Count+1),Kind="effect_target",ChooserSeat=e.ControllerSeat,Source=e.CardId,ResumeAt=resume,CandidateUnits=targets,Optional=false};
   Emit(state,command,"EffectTargetChoiceRequired",e.ControllerSeat,e.CardId,detail:resume);
  }
  private static void ChooseGroupPushTarget(ContentCatalog catalog,GameState state,Command command)
  {
   Require(LegalEffectTargets(catalog,state,command.ActorSeat).Contains(command.Value),"invalid_effect_target","请由来源英雄选择尚未处理的合法目标。");
   var e=state.Execution!;bool push=state.Pending!.ResumeAt=="push_all_adjacent";
   var target=state.Units.Single(u=>u.Id==command.Value);e.TargetUnitId=target.Id;
   if(push)
   {
    var source=state.Units.Single(u=>u.Seat==e.ControllerSeat);var program=GroupPushProgram(catalog,state,InstructionKind.PushAllAdjacentEnemies)!;
    var result=PushRules.AwayFromAdjacent(catalog,state,source,target,program.TextPushDistance)!;
    e.RemainingUnitTargets!.Remove(target.Id);target.Position=result.Path.Last();if(result.Path.Count>1)RecordDisplacedMinion(state,target);
    Emit(state,command,"UnitPushed",target.Seat,e.CardId,detail:"by:"+e.ControllerSeat+"|unit:"+target.Id);
    var pushed=state.Events.Last();pushed.From=result.Path.First();pushed.To=target.Position;pushed.Path=result.Path;
    if(result.StopReason!="")Emit(state,command,"PushStopped",target.Seat,e.CardId,detail:result.StopReason);
    if(target.Kind=="hero" && (result.StopReason=="obstacle" || result.StopReason=="occupied"))
    {if(e.AffectedHeroTargets==null)e.AffectedHeroTargets=new List<string>();e.AffectedHeroTargets.Add(target.Id);}
   }
   else e.AffectedHeroTargets!.Remove(target.Id);
   state.Pending=null;state.Phase=Phase.Action;
   if(!push && BeginTargetDiscard(catalog,state,command,e,resume:"push_blocked_discard"))return;
   ContinueCard(catalog,state,command);
  }
 }
}
