#nullable enable
using System.Collections.Generic;
using System.Linq;
using Goa2.Domain;
using Goa2.Rules.Cards;

namespace Goa2.Rules
{
    public sealed partial class GameRules
    {
        private static List<string> HeroTargets(ContentCatalog catalog,GameState state,CardExecution execution,PrimaryProgram program)
        {
            var source=state.Units.SingleOrDefault(u=>u.Seat==execution.ControllerSeat);
            if(source==null)return new List<string>();
            if(program.HeroTarget==HeroTargetKind.AdjacentEnemyUsedAttack)
                return state.Units.Where(t=>t.Kind=="hero" && t.Seat.HasValue && t.Team!=source.Team && t.Position.Distance(source.Position)==1 &&
                    state.Players[t.Seat!.Value].Cards.Any(c=>c.PlayedRound==state.Round && c.PlayedTurn==state.Turn && catalog.Card(c.CardId).PrimaryFamily=="attack"))
                    .Select(t=>t.Id).OrderBy(id=>id,System.StringComparer.Ordinal).ToList();
            if(program.HeroTarget!=HeroTargetKind.AlliedNearEnemy)return new List<string>();
            var card=catalog.Card(execution.CardId);
            int range=(card.SubtypeValue??0)+state.Players[execution.ControllerSeat].RangedBonus;
            return state.Units.Where(target=>target.Kind=="hero" && target.Seat.HasValue && target.Team==source.Team &&
                target.Position.Distance(source.Position)<=range && state.Units.Any(enemy=>enemy.Team!=target.Team &&
                    (enemy.Kind=="hero" || enemy.Kind=="melee" || enemy.Kind=="ranged" || enemy.Kind=="heavy") && enemy.Position.Distance(target.Position)==1))
                .Select(u=>u.Id).OrderBy(id=>id,System.StringComparer.Ordinal).ToList();
        }
        private static bool BeginTargetDiscard(GameState state,Command command,CardExecution execution)
        {
            var target=state.Units.SingleOrDefault(u=>u.Id==execution.TargetUnitId && u.Kind=="hero" && u.Seat.HasValue);
            if(target==null)return false;
            int seat=target.Seat!.Value;
            if(!state.Players[seat].Cards.Any(c=>c.Zone==CardZone.InHand)){Emit(state,command,"ForcedDiscardSkipped",seat,execution.CardId,detail:"empty_hand");return false;}
            state.Phase=Phase.EffectChoice;
            state.Pending=new PendingChoice{Id="forced-discard:"+(state.Events.Count+1),Kind="forced_discard",ChooserSeat=seat,Source=execution.CardId,UnitId=target.Id,ResumeAt="primary_discard",Optional=false};
            Emit(state,command,"ForcedDiscardRequired",seat,execution.CardId);return true;
        }
        public static List<string> LegalEffectTargets(ContentCatalog catalog,GameState state,int seat)
        {
            if(state.Pending?.Kind=="effect_minion") return LegalEffectMinionRemovals(catalog,state,seat);
            var execution=state.Execution;
            if(state.Phase!=Phase.EffectChoice || state.Pending?.Kind!="effect_target" || state.Pending.ChooserSeat!=seat ||
                execution==null || execution.ControllerSeat!=seat || state.ActiveSeat!=seat) return new List<string>();
            var program=CardPrograms.Primary(catalog.Card(execution.CardId),state.EngineVersion);
            return program!=null && program.Id==execution.ProgramId && program.Version==execution.ProgramVersion && execution.Cursor>=0 &&
                execution.Cursor<program.Instructions.Count && program.Instructions[execution.Cursor]==InstructionKind.ChooseHeroTarget
                ? HeroTargets(catalog,state,execution,program) : new List<string>();
        }
        private static bool BeginHeroTarget(ContentCatalog catalog,GameState state,Command command,CardExecution execution,PrimaryProgram program)
        {
            var targets=HeroTargets(catalog,state,execution,program);
            if(targets.Count==0)return false;
            state.Phase=Phase.EffectChoice;
            state.Pending=new PendingChoice {Id="effect-target:"+(state.Events.Count+1),Kind="effect_target",ChooserSeat=execution.ControllerSeat,
                Source=execution.CardId,CandidateUnits=targets,ResumeAt="card_effect_target"};
            Emit(state,command,"EffectTargetChoiceRequired",execution.ControllerSeat,execution.CardId);return true;
        }
        private static void ChooseEffectTarget(ContentCatalog catalog,GameState state,Command command)
        {
            if(state.Pending?.Kind=="effect_minion") {ChooseEffectMinionRemoval(catalog,state,command);return;}
            Require(LegalEffectTargets(catalog,state,command.ActorSeat).Contains(command.Value),"invalid_effect_target","请由来源英雄选择当前合法的牌文目标。");
            var execution=state.Execution!;execution.TargetUnitId=command.Value;execution.Cursor++;
            state.Pending=null;state.Phase=Phase.Action;
            Emit(state,command,"EffectTargetChosen",command.ActorSeat,execution.CardId,detail:command.Value);
            ContinueCard(catalog,state,command);
        }
    }
}
