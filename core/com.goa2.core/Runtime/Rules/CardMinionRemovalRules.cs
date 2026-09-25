#nullable enable
using System.Collections.Generic;
using System.Linq;
using Goa2.Domain;
using Goa2.Rules.Cards;

namespace Goa2.Rules
{
    public sealed partial class GameRules
    {
        private static List<string> EffectMinionRemovalTargets(ContentCatalog catalog,GameState state,CardExecution execution,PrimaryProgram program)
        {
            var source=state.Units.SingleOrDefault(u=>u.Seat==execution.ControllerSeat);
            if(source==null || execution.AttackOutcome!="minion_defeated") return new List<string>();
            int range=CombatRules.AttackDistance(catalog,state,catalog.Card(execution.CardId),program,execution.ControllerSeat);
            // Presence is independent of whether that enemy hero can currently be attacked.
            if(state.Units.Any(u=>u.Kind=="hero" && u.Team!=source.Team && u.Position.Distance(source.Position)<=range)) return new List<string>();
            var removable=new HashSet<string>(LegalMinionRemovals(state));
            int removalRange=program.ExtraRemovalUsesAttackRange ? range : 1;
            return state.Units.Where(u=>u.Team!=source.Team && u.Kind!="heavy" && removable.Contains(u.Id) &&
                u.Position.Distance(source.Position)>=1 && u.Position.Distance(source.Position)<=removalRange)
                .Select(u=>u.Id).OrderBy(id=>id,System.StringComparer.Ordinal).ToList();
        }
        private static PrimaryProgram? EffectMinionRemovalProgram(ContentCatalog catalog,GameState state,int seat)
        {
            var execution=state.Execution;
            if(state.Phase!=Phase.EffectChoice || state.Pending?.Kind!="effect_minion" || state.Pending.ChooserSeat!=seat ||
                execution==null || execution.ControllerSeat!=seat || state.ActiveSeat!=seat) return null;
            var program=CardPrograms.Primary(catalog.Card(execution.CardId),state.EngineVersion);
            return program!=null && program.Id==execution.ProgramId && program.Version==execution.ProgramVersion && execution.Cursor>=0 &&
                execution.Cursor<program.Instructions.Count && program.Instructions[execution.Cursor]==InstructionKind.OptionalMinionRemovalAfterDefeat ? program : null;
        }
        private static List<string> LegalEffectMinionRemovals(ContentCatalog catalog,GameState state,int seat)
        {
            var program=EffectMinionRemovalProgram(catalog,state,seat);
            return program==null ? new List<string>() : EffectMinionRemovalTargets(catalog,state,state.Execution!,program);
        }
        private static bool BeginEffectMinionRemoval(ContentCatalog catalog,GameState state,Command command,CardExecution execution,PrimaryProgram program)
        {
            var targets=EffectMinionRemovalTargets(catalog,state,execution,program);
            if(targets.Count==0)return false;
            state.Phase=Phase.EffectChoice;
            state.Pending=new PendingChoice {Id="effect-minion:"+(state.Events.Count+1),Kind="effect_minion",ChooserSeat=execution.ControllerSeat,
                Source=execution.CardId,CandidateUnits=targets,ResumeAt="remove_minion",Optional=true};
            Emit(state,command,"EffectMinionChoiceRequired",execution.ControllerSeat,execution.CardId);return true;
        }
        private static void ChooseEffectMinionRemoval(ContentCatalog catalog,GameState state,Command command)
        {
            Require(EffectMinionRemovalProgram(catalog,state,command.ActorSeat)!=null &&
                (command.Value=="skip" || LegalEffectMinionRemovals(catalog,state,command.ActorSeat).Contains(command.Value)),
                "invalid_effect_target","请由行动英雄选择合法小兵或不移除。");
            var execution=state.Execution!;execution.Cursor++;state.Pending=null;state.Phase=Phase.Action;
            if(command.Value=="skip") Emit(state,command,"EffectMinionRemovalSkipped",execution.ControllerSeat,execution.CardId);
            else
            {
                RemoveMinion(catalog,state,command,command.Value,execution.CardId);
                Emit(state,command,"EffectMinionRemoved",execution.ControllerSeat,execution.CardId,detail:command.Value);
            }
            ContinueCard(catalog,state,command);
        }
    }
}
