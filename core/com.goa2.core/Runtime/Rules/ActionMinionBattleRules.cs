#nullable enable
using System.Collections.Generic;
using System.Linq;
using Goa2.Domain;
using Goa2.Rules.Cards;
namespace Goa2.Rules
{
    public sealed partial class GameRules
    {
        private const string ActionBattleOfferResume = "action_minion_battle_offer";
        private static bool BeginActionBattleCompletion(ContentCatalog catalog, GameState state, Command command, CardDefinition ability, UltimateProgram program)
        {
            var e=state.Execution!;var hero=state.Units.SingleOrDefault(u=>u.Kind=="hero" && u.Seat==e.ControllerSeat);
            if(hero==null || catalog.Cell(hero.Position)?.Region!=state.CombatRegion)return false;
            e.Completion=new ActionCompletionProgress {SourceCardId=ability.Id,ProgramId=program.Id,ProgramVersion=program.Version,Stage="battle_offer"};
            state.Phase=Phase.EffectChoice;
            state.Pending=new PendingChoice {Id="action-battle-offer:"+(state.Events.Count+1),Kind="primary_option",ChooserSeat=e.ControllerSeat,Source=ability.Id,ResumeAt=ActionBattleOfferResume,Optional=true};
            Emit(state,command,"UltimateTriggered",e.ControllerSeat,ability.Id,detail:e.CardId);
            Emit(state,command,"ActionMinionBattleOffered",e.ControllerSeat,ability.Id);return true;
        }
        private static List<string> LegalActionBattleOptions(GameState state,int seat) =>
            state.EngineVersion>=60 && state.Phase==Phase.EffectChoice && state.Pending?.Kind=="primary_option" && state.Pending.ResumeAt==ActionBattleOfferResume &&
            state.Pending.ChooserSeat==seat && state.Execution?.ControllerSeat==seat && state.ActiveSeat==seat && state.Execution.Completion?.Stage=="battle_offer"
                ? new List<string>{"battle","skip"}:new List<string>();
        private static void ChooseActionBattleOption(ContentCatalog catalog,GameState state,Command command)
        {
            Require(LegalActionBattleOptions(state,command.ActorSeat).Contains(command.Value),"invalid_primary_option","请由基础技能执行者选择是否发动小兵战斗。");
            var e=state.Execution!;var completion=e.Completion!;
            if(command.Value=="skip")
            {
                Emit(state,command,"ActionMinionBattleSkipped",e.ControllerSeat,completion.SourceCardId);
                CompleteUltimate(state,command,"skipped");EndCardExecution(catalog,state,command);return;
            }
            var roles=MinionBattleParticipantRules.Capture(catalog,state);
            var battle=new ActionMinionBattleProgress {Region=state.CombatRegion,HeroContributions=roles,
                BlueMinions=MinionBattleParticipantRules.Count(state,roles,Team.Blue),RedMinions=MinionBattleParticipantRules.Count(state,roles,Team.Red)};
            battle.RemainingRemovals=System.Math.Abs(battle.BlueMinions-battle.RedMinions);
            if(battle.RemainingRemovals>0)battle.LosingTeam=battle.BlueMinions<battle.RedMinions?Team.Blue:Team.Red;
            completion.ActionBattle=battle;completion.Stage="battle";state.Pending=null;
            Emit(state,command,"ActionMinionBattleCounted",e.ControllerSeat,completion.SourceCardId,detail:battle.BlueMinions+":"+battle.RedMinions+":"+battle.RemainingRemovals);
            ContinueActionMinionBattle(catalog,state,command);
        }
        private static List<string> LegalActionMinionRemovals(GameState state,int seat)
        {
            var battle=state.Execution?.Completion?.ActionBattle;
            if(state.EngineVersion<60 || state.Phase!=Phase.EffectChoice || state.Pending?.Kind!="action_minion_removal" || state.Pending.ChooserSeat!=seat || state.Frontline!=null || state.Execution?.Completion?.Stage!="battle" || battle==null)return new List<string>();
            return MinionBattleParticipantRules.RemovalCandidates(state,battle.HeroContributions,battle.LosingTeam);
        }
        private static void ContinueActionMinionBattle(ContentCatalog catalog,GameState state,Command command)
        {
            if(state.Phase==Phase.Finished)return;
            var e=state.Execution!;var completion=e.Completion!;var battle=completion.ActionBattle!;
            if(battle.RemainingRemovals==0 || MinionBattleParticipantRules.RemovalCandidates(state,battle.HeroContributions,battle.LosingTeam).Count==0)
            {
                battle.RemainingRemovals=0;
                Emit(state,command,"ActionMinionBattleCompleted",e.ControllerSeat,completion.SourceCardId,detail:battle.Region);
                CompleteUltimate(state,command,"battle_completed");EndCardExecution(catalog,state,command);return;
            }
            state.Phase=Phase.EffectChoice;
            state.Pending=new PendingChoice {Id="action-minion:"+(state.Events.Count+1),Kind="action_minion_removal",ChooserSeat=Captain(state,battle.LosingTeam!.Value),Source=completion.SourceCardId,ResumeAt="action_minion_battle",Optional=false};
            state.Pending.CandidateUnits=LegalActionMinionRemovals(state,state.Pending.ChooserSeat);
            Emit(state,command,"RoundMinionChoiceRequired",state.Pending.ChooserSeat,completion.SourceCardId,detail:battle.RemainingRemovals.ToString());
        }
        private static void ChooseActionMinionRemoval(ContentCatalog catalog,GameState state,Command command)
        {
            Require(LegalActionMinionRemovals(state,command.ActorSeat).Contains(command.Value),"invalid_round_minion","请由少兵方队长选择可承担移除的本队参战单位。");
            var completion=state.Execution!.Completion!;var battle=completion.ActionBattle!;
            var role=MinionBattleParticipantRules.ForTeam(state,battle.HeroContributions,battle.LosingTeam).SingleOrDefault(r=>r.UnitId==command.Value);
            if(role!=null)
            {
                var program=UltimateRules.OwnedProgram(catalog,state,role.ControllerSeat);
                Require(program!=null && program.Id==role.ProgramId && program.Version==role.ProgramVersion && state.Players[role.ControllerSeat].PurpleCardId==role.SourceCardId,"invalid_battle_participant","保存的英雄参战来源不符。");
                Emit(state,command,"UltimateTriggered",role.ControllerSeat,role.SourceCardId,detail:role.UnitId);
                Emit(state,command,"MinionBattleStoppedByHero",command.ActorSeat,role.SourceCardId,detail:role.UnitId);
                battle.RemainingRemovals=0;state.Pending=null;ContinueActionMinionBattle(catalog,state,command);return;
            }
            bool heavy=state.Units.Single(u=>u.Id==command.Value).Kind=="heavy";
            battle.RemainingRemovals=heavy?0:battle.RemainingRemovals-1;state.Pending=null;
            RemoveMinion(catalog,state,command,command.Value,completion.SourceCardId,resumeActionMinionBattle:heavy);
            // A heavy removal always resumes through FrontlineTransition, even with no blocked spawns.
            if(!heavy && state.Phase!=Phase.Finished)ContinueActionMinionBattle(catalog,state,command);
        }
    }
}
