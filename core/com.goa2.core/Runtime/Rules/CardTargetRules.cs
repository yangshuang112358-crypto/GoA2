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
            if(program.HeroTarget==HeroTargetKind.OtherEnemyInSkillRange)
            {
                int radius=(catalog.Card(execution.CardId).SubtypeValue??0)+state.Players[execution.ControllerSeat].RangeBonus;
                return state.Units.Where(t=>t.Kind=="hero" && t.Seat.HasValue && t.Team!=source.Team && t.Id!=execution.TargetUnitId && t.Position.Distance(source.Position)<=radius)
                    .Select(t=>t.Id).OrderBy(id=>id,System.StringComparer.Ordinal).ToList();
            }
            if(program.HeroTarget==HeroTargetKind.OtherAdjacentEnemy)
                return state.Units.Where(t=>t.Kind=="hero" && t.Seat.HasValue && t.Team!=source.Team && t.Id!=execution.TargetUnitId && t.Position.Distance(source.Position)==1)
                    .Select(t=>t.Id).OrderBy(id=>id,System.StringComparer.Ordinal).ToList();
            if(program.HeroTarget==HeroTargetKind.EnemyInSkillRangeNearFriendlyMinion)
            {
                if(!state.Units.Any(u=>u.Team==source.Team && (u.Kind=="melee" || u.Kind=="ranged" || u.Kind=="heavy") && u.Position.Distance(source.Position)==1))return new List<string>();
                int radius=(catalog.Card(execution.CardId).SubtypeValue??0)+state.Players[execution.ControllerSeat].RangeBonus;
                return state.Units.Where(t=>t.Kind=="hero" && t.Team!=source.Team && t.Position.Distance(source.Position)<=radius).Select(t=>t.Id).OrderBy(id=>id,System.StringComparer.Ordinal).ToList();
            }
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
        private static bool BeginTargetDiscard(GameState state,Command command,CardExecution execution,string? otherTarget=null)
        {
            var target=state.Units.SingleOrDefault(u=>u.Id==(otherTarget??execution.TargetUnitId) && u.Kind=="hero" && u.Seat.HasValue);
            if(target==null)return false;
            int seat=target.Seat!.Value;
            if(!state.Players[seat].Cards.Any(c=>c.Zone==CardZone.InHand)){Emit(state,command,"ForcedDiscardSkipped",seat,execution.CardId,detail:"empty_hand");return false;}
            state.Phase=Phase.EffectChoice;
            state.Pending=new PendingChoice{Id="forced-discard:"+(state.Events.Count+1),Kind="forced_discard",ChooserSeat=seat,Source=execution.CardId,UnitId=target.Id,ResumeAt=otherTarget==null ? "primary_discard" : "attack_before_discard",Optional=false};
            Emit(state,command,"ForcedDiscardRequired",seat,execution.CardId);return true;
        }
        public static List<string> LegalEffectTargets(ContentCatalog catalog,GameState state,int seat)
        {
            if(state.Pending?.Kind=="effect_minion") return LegalEffectMinionRemovals(catalog,state,seat);
            var execution=state.Execution;
            if(state.Phase!=Phase.EffectChoice || state.Pending?.Kind!="effect_target" || state.Pending.ChooserSeat!=seat ||
                execution==null || execution.ControllerSeat!=seat || state.ActiveSeat!=seat) return new List<string>();
            if(state.Pending.ResumeAt=="before_attack_other_move")return OtherMoveTargets(catalog,state);
            var program=CardPrograms.Primary(catalog.Card(execution.CardId),state.EngineVersion);
            return program!=null && program.Id==execution.ProgramId && program.Version==execution.ProgramVersion && execution.Cursor>=0 &&
                execution.Cursor<program.Instructions.Count && (program.Instructions[execution.Cursor]==InstructionKind.ChooseHeroTarget || program.Instructions[execution.Cursor]==InstructionKind.OptionalOtherHeroDiscard)
                ? HeroTargets(catalog,state,execution,program) : new List<string>();
        }
        private static bool BeginHeroTarget(ContentCatalog catalog,GameState state,Command command,CardExecution execution,PrimaryProgram program)
        {
            var targets=HeroTargets(catalog,state,execution,program);
            if(targets.Count==0)return false;
            bool optional=program.Instructions[execution.Cursor]==InstructionKind.OptionalOtherHeroDiscard;
            state.Phase=Phase.EffectChoice;
            state.Pending=new PendingChoice {Id="effect-target:"+(state.Events.Count+1),Kind="effect_target",ChooserSeat=execution.ControllerSeat,
                Source=execution.CardId,CandidateUnits=targets,ResumeAt=optional ? "attack_before_optional_discard" : "card_effect_target",Optional=optional};
            Emit(state,command,"EffectTargetChoiceRequired",execution.ControllerSeat,execution.CardId);return true;
        }
        private static void ChooseEffectTarget(ContentCatalog catalog,GameState state,Command command)
        {
            if(state.Pending?.ResumeAt=="before_attack_other_move"){ChooseOtherMoveTarget(catalog,state,command);return;}
            if(state.Pending?.Kind=="effect_minion") {ChooseEffectMinionRemoval(catalog,state,command);return;}
            if(state.EngineVersion>=35 && state.Pending?.Kind=="effect_target" && state.Pending.ResumeAt=="attack_before_optional_discard")
            {
                Require(state.Pending.ChooserSeat==command.ActorSeat && state.Pending.Optional && state.Execution!=null &&
                    (command.Value=="skip" || LegalEffectTargets(catalog,state,command.ActorSeat).Contains(command.Value)),"invalid_effect_target","请选择另一名合法敌方英雄，或跳过额外弃牌。");
                var attack=state.Execution!;state.Pending=null;state.Phase=Phase.Action;
                Emit(state,command,command.Value=="skip" ? "EffectTargetSkipped" : "EffectTargetChosen",command.ActorSeat,attack.CardId,detail:command.Value);
                if(command.Value!="skip" && BeginTargetDiscard(state,command,attack,command.Value))return;
                attack.Cursor++;ContinueCard(catalog,state,command);return;
            }
            Require(LegalEffectTargets(catalog,state,command.ActorSeat).Contains(command.Value),"invalid_effect_target","请由来源英雄选择当前合法的牌文目标。");
            var execution=state.Execution!;execution.TargetUnitId=command.Value;execution.Cursor++;
            state.Pending=null;state.Phase=Phase.Action;
            Emit(state,command,"EffectTargetChosen",command.ActorSeat,execution.CardId,detail:command.Value);
            ContinueCard(catalog,state,command);
        }
    }
}
