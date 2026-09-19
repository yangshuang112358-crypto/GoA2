#nullable enable
using System.Collections.Generic;
using System.Linq;
using Goa2.Domain;
using Goa2.Rules.Cards;
namespace Goa2.Rules
{
 public sealed partial class GameRules
 {
  private static bool IsUnitSwapStep(ContentCatalog catalog,GameState state)
  {
   var e=state.Execution;if(state.EngineVersion<46 || e==null)return false;var p=CardPrograms.Primary(catalog.Card(e.CardId),state.EngineVersion);
   return p!=null && p.Id==e.ProgramId && p.Version==e.ProgramVersion && e.Cursor>=0 && e.Cursor<p.Instructions.Count && (p.Instructions[e.Cursor]==InstructionKind.ChooseUnitSwapTarget || p.Instructions[e.Cursor]==InstructionKind.SwapTargetUnits);
  }
  private static List<string> UnitSwapTargets(ContentCatalog catalog,GameState state)
  {
   if(!IsUnitSwapStep(catalog,state))return new List<string>();var e=state.Execution!;var source=state.Units.SingleOrDefault(u=>u.Seat==e.ControllerSeat);if(source==null)return new List<string>();
   int range=(catalog.Card(e.CardId).SubtypeValue??0)+state.Players[e.ControllerSeat].RangedBonus;var minions=new HashSet<string>(LegalMinionRemovals(state));
   return state.Units.Where(u=>u.Id!=source.Id && u.Position.Distance(source.Position)<=range && (u.Kind=="hero" && u.Team==source.Team || minions.Contains(u.Id))).Select(u=>u.Id).OrderBy(id=>id,System.StringComparer.Ordinal).ToList();
  }
  private static bool BeginUnitSwapTarget(ContentCatalog catalog,GameState state,Command command)
  {
   var targets=UnitSwapTargets(catalog,state);if(targets.Count==0)return false;var e=state.Execution!;
   state.Phase=Phase.EffectChoice;state.Pending=new PendingChoice{Id="unit-swap-target:"+(state.Events.Count+1),Kind="effect_target",ChooserSeat=e.ControllerSeat,Source=e.CardId,ResumeAt="unit_swap",CandidateUnits=targets};
   Emit(state,command,"UnitSwapChoiceRequired",e.ControllerSeat,e.CardId);return true;
  }
  private static void SwapTargetUnits(ContentCatalog catalog,GameState state,Command command)
  {
   var e=state.Execution!;Require(UnitSwapTargets(catalog,state).Contains(e.TargetUnitId),"invalid_swap_target","换位目标已不合法。");
   var source=state.Units.Single(u=>u.Seat==e.ControllerSeat);var target=state.Units.Single(u=>u.Id==e.TargetUnitId);var origin=source.Position;var destination=target.Position;
   // Both coordinates change in the same rule command; no intermediate occupied destination is exposed.
   source.Position=destination;target.Position=origin;RecordDisplacedMinion(state,target);
   Emit(state,command,"UnitsSwapped",e.ControllerSeat,e.CardId,detail:target.Id);var swapped=state.Events.Last();swapped.From=origin;swapped.To=destination;
  }
 }
}
