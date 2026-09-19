#nullable enable
using System.Linq;
using Goa2.Domain;
using Goa2.Rules.Cards;

namespace Goa2.Rules
{
    public sealed partial class GameRules
    {
        private static bool BeginDifferentAttackRepeat(ContentCatalog catalog,GameState state,Command command,CardExecution execution,CardDefinition card,PrimaryProgram program)
        {
            var source=state.Units.SingleOrDefault(u=>u.Seat==execution.ControllerSeat);
            var targets=source==null ? new System.Collections.Generic.List<string>() : CombatRules.DifferentRepeatTargets(catalog,state,source,card,program);
            if(targets.Count==0) return false;
            execution.Attack=null;
            state.Phase=Phase.EffectChoice;
            state.Pending=new PendingChoice
            {
                Id="attack-repeat:"+(state.Events.Count+1),Kind="attack_target",ChooserSeat=execution.ControllerSeat,
                CandidateUnits=targets,Source=execution.CardId,ResumeAt="repeat_once_different",Optional=true
            };
            Emit(state,command,"AttackRepeatChoiceRequired",execution.ControllerSeat,execution.CardId);
            return true;
        }
        private static bool BeginAttackRepeat(ContentCatalog catalog,GameState state,Command command,CardExecution execution,CardDefinition card,PrimaryProgram program)
        {
            if(execution.AttackOutcome!="hero_defeated") return false;
            var source=state.Units.SingleOrDefault(u=>u.Seat==execution.ControllerSeat);
            var targets=source==null ? new System.Collections.Generic.List<string>() : CombatRules.Targets(catalog,state,source,card,program);
            if(targets.Count==0)
            {
                Emit(state,command,"AttackRepeatUnavailable",execution.ControllerSeat,execution.CardId,detail:"no_targets");
                return false;
            }
            execution.Attack=null;
            state.Phase=Phase.EffectChoice;
            state.Pending=new PendingChoice
            {
                Id="attack-repeat:"+(state.Events.Count+1),Kind="attack_target",ChooserSeat=execution.ControllerSeat,
                CandidateUnits=targets,Source=execution.CardId,ResumeAt="repeat_attack",Optional=true
            };
            Emit(state,command,"AttackRepeatChoiceRequired",execution.ControllerSeat,execution.CardId);
            return true;
        }
        private static void RestartAttack(ContentCatalog catalog,GameState state,Command command)
        {
            var execution=state.Execution!;
            var program=CardPrograms.Primary(catalog.Card(execution.CardId),state.EngineVersion);
            Require(program!=null && program.Instructions[execution.Cursor]==InstructionKind.OptionalRepeatAttackAfterHeroDefeat,
                "invalid_program_cursor","当前不是重复攻击的步骤。");
            int attack=program!.Instructions.ToList().IndexOf(InstructionKind.Attack);
            Require(attack>=0 && attack<execution.Cursor,"invalid_program_cursor","找不到可重复的攻击步骤。");
            execution.Cursor=attack;execution.Attack=null;execution.AttackOutcome="";
            Emit(state,command,"AttackRepeated",execution.ControllerSeat,execution.CardId,detail:command.Value);
        }
    }
}
