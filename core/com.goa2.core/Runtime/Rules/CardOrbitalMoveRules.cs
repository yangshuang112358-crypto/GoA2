#nullable enable
using System.Collections.Generic;
using System.Linq;
using Goa2.Domain;
using Goa2.Rules.Cards;
namespace Goa2.Rules
{
 public sealed partial class GameRules
 {
  private static List<string> OrbitalTargets(ContentCatalog catalog,GameState state)
  {
   if(state.EngineVersion<83 || MinionMoveProgram(catalog,state,InstructionKind.ChooseOrbitalTarget)==null)return new List<string>();
   var e=state.Execution!;var source=state.Units.SingleOrDefault(u=>u.Seat==e.ControllerSeat);if(source==null)return new List<string>();
   int radius=(catalog.Card(e.CardId).SubtypeValue??0)+state.Players[e.ControllerSeat].RangeBonus;
   var minions=new HashSet<string>(LegalMinionRemovals(state));
   return state.Units.Where(u=>u.Id!=source.Id && (u.Kind=="hero" || minions.Contains(u.Id)) && u.Position.Distance(source.Position)>1 &&
    u.Position.Distance(source.Position)<=radius && EffectRules.CanDisplace(catalog,state,e.ControllerSeat,u)).Select(u=>u.Id).OrderBy(id=>id,System.StringComparer.Ordinal).ToList();
  }
  private static List<string> LegalOrbitalTargets(ContentCatalog catalog,GameState state,int seat)=>
   state.Phase==Phase.EffectChoice && state.Pending?.Kind=="effect_target" && state.Pending.ResumeAt=="orbital_target" &&
   state.Pending.ChooserSeat==seat && state.ActiveSeat==seat && state.Execution?.ControllerSeat==seat ? OrbitalTargets(catalog,state):new List<string>();
  private static bool BeginOrbitalTarget(ContentCatalog catalog,GameState state,Command command)
  {
   var targets=OrbitalTargets(catalog,state);if(targets.Count==0)return false;var e=state.Execution!;
   state.Phase=Phase.EffectChoice;state.Pending=new PendingChoice{Id="orbital-target:"+(state.Events.Count+1),Kind="effect_target",Source=e.CardId,ChooserSeat=e.ControllerSeat,ResumeAt="orbital_target",CandidateUnits=targets};
   Emit(state,command,"EffectTargetChoiceRequired",e.ControllerSeat,e.CardId);return true;
  }
  private static void ChooseOrbitalTarget(ContentCatalog catalog,GameState state,Command command)
  {
   Require(LegalOrbitalTargets(catalog,state,command.ActorSeat).Contains(command.Value),"invalid_effect_target","请选择范围内其他非相邻且可以被移动的单位。");
   var e=state.Execution!;e.TargetUnitId=command.Value;e.Cursor++;state.Pending=null;state.Phase=Phase.Action;
   Emit(state,command,"EffectTargetChosen",e.ControllerSeat,e.CardId,detail:command.Value);ContinueCard(catalog,state,command);
  }
 }
}
