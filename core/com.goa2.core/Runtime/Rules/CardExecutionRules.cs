#nullable enable
using System.Linq;
using Goa2.Domain;
using Goa2.Rules.Cards;

namespace Goa2.Rules
{
    public sealed partial class GameRules
    {
        private static void BeginPrimary(ContentCatalog catalog, GameState state, Command command)
        {
            Require(state.Phase == Phase.Action && state.ActiveSeat == command.ActorSeat && state.Pending == null && state.Execution == null,
                "not_active", "当前不能开始主要行动。");
            Require(state.Units.Any(u => u.Seat == command.ActorSeat), "hero_absent", "英雄不在地图上。");
            var card = ActiveCard(state); var definition = catalog.Card(card.CardId); var program = CardPrograms.Primary(definition,state.EngineVersion);
            Require(program != null, "primary_not_implemented", "此卡主要行动尚未实装。");
            Require(EffectRules.SkillRestriction(catalog,state,command.ActorSeat,definition) == "", "primary_restricted", "当前技能受到打断施法限制。");
            state.Execution = new CardExecution { CardId = card.CardId, ControllerSeat = command.ActorSeat, ProgramId = program!.Id, ProgramVersion = program.Version };
            Emit(state, command, "PrimaryActionStarted", command.ActorSeat, card.CardId);
            ContinueCard(catalog, state, command);
        }
        private static void ContinueCard(ContentCatalog catalog, GameState state, Command command)
        {
            if (state.Execution == null || state.Phase == Phase.Finished) return;
            var execution = state.Execution; var card = catalog.Card(execution.CardId);
            var program = CardPrograms.Primary(card,state.EngineVersion) ?? throw new RuleViolation("incompatible_program", "卡牌程序版本不兼容。");
            Require(program.Id == execution.ProgramId && program.Version == execution.ProgramVersion, "incompatible_program", "卡牌程序版本不兼容。");
            if (execution.AwaitingAttackCompletion)
            {
                execution.AwaitingAttackCompletion = false;
                Emit(state, command, "AttackResolved", execution.ControllerSeat, execution.CardId, detail: execution.AttackOutcome + ":" + execution.TargetUnitId);
            }
            for (int step = 0; step < 32; step++)
            {
                Require(execution.Cursor >= 0 && execution.Cursor < program.Instructions.Count, "invalid_program_cursor", "卡牌步骤无效。");
                switch (program.Instructions[execution.Cursor])
                {
                    case InstructionKind.ChooseAttackTarget:
                        var source = state.Units.SingleOrDefault(u => u.Seat == execution.ControllerSeat);
                        var targets = source == null ? new System.Collections.Generic.List<string>() : CombatRules.Targets(catalog, state, source, card, program);
                        if (targets.Count == 0) { StopCard(catalog, state, command, "no_targets"); return; }
                        state.Phase = Phase.EffectChoice;
                        state.Pending = new PendingChoice
                        {
                            Id = "attack-target:" + (state.Events.Count + 1), Kind = "attack_target", ChooserSeat = execution.ControllerSeat,
                            CandidateUnits = targets, Source = execution.CardId, ResumeAt = "attack", Optional = false
                        };
                        Emit(state, command, "AttackTargetChoiceRequired", execution.ControllerSeat, execution.CardId);
                        return;
                    case InstructionKind.Attack:
                        StartAttack(catalog, state, command, program);
                        return;
                    case InstructionKind.End:
                        EndCardExecution(catalog, state, command);
                        return;
                    case InstructionKind.ApplyEffect:
                        ApplyTimedEffect(catalog,state,command,execution,program);
                        execution.Cursor++;
                        break;
                    case InstructionKind.CancelAdjacentSkillEffects:
                        CancelAdjacentSkillEffects(catalog,state,command,execution);
                        execution.Cursor++;
                        break;
                }
            }
            throw new RuleViolation("card_step_limit", "单次卡牌推进超过上限。");
        }
        private static void ChooseAttackTarget(ContentCatalog catalog, GameState state, Command command)
        {
            Require(state.Pending?.Kind == "attack_target" && state.Pending.ChooserSeat == command.ActorSeat && state.Execution != null &&
                CombatRules.AttackTargets(catalog, state, command.ActorSeat).Contains(command.Value), "invalid_attack_target", "请选择当前合法敌方目标。");
            state.Execution!.TargetUnitId = command.Value; state.Execution.Cursor++;
            state.Pending = null; state.Phase = Phase.Action;
            Emit(state, command, "AttackTargetChosen", command.ActorSeat, state.Execution.CardId, detail: command.Value);
            ContinueCard(catalog, state, command);
        }
        private static void StartAttack(ContentCatalog catalog, GameState state, Command command, PrimaryProgram program)
        {
            var execution = state.Execution!; var card = catalog.Card(execution.CardId);
            var source = state.Units.SingleOrDefault(u => u.Seat == execution.ControllerSeat);
            if (source == null || !CombatRules.Targets(catalog, state, source, card, program).Contains(execution.TargetUnitId))
            { StopCard(catalog, state, command, "target_invalid"); return; }
            var target = state.Units.Single(u => u.Id == execution.TargetUnitId);
            execution.Cursor++; execution.AwaitingAttackCompletion = true;
            Emit(state, command, "AttackDeclared", execution.ControllerSeat, card.Id, detail: target.Id);
            if (target.Kind != "hero")
            {
                execution.AttackOutcome = "minion_defeated";
                RemoveMinion(catalog, state, command, target.Id, card.Id, execution.ControllerSeat, resumeCardExecution: true);
                if (state.Frontline == null) ContinueCard(catalog, state, command);
                return;
            }
            execution.Attack = CombatMath.Attack(state, card, execution.ControllerSeat, target.Id);
            Emit(state, command, "AttackCalculated", execution.ControllerSeat, card.Id);
            state.Events.Last().AttackValues = execution.Attack;
            state.Phase = Phase.EffectChoice;
            state.Pending = new PendingChoice
            {
                Id = "defense:" + (state.Events.Count + 1), Kind = "defense", ChooserSeat = target.Seat!.Value,
                Source = card.Id, UnitId = target.Id, ResumeAt = "resolve_attack", Optional = true
            };
            int defender = target.Seat.Value;
            if (CombatRules.DefenseOptions(catalog, state, defender).Count == 0 && CombatRules.UnimplementedDefenses(catalog, state, defender).Count == 0)
            {
                Emit(state, command, "NoDefenseAvailable", defender);
                ResolveDefense(catalog, state, command, false);
            }
            else Emit(state, command, "DefenseChoiceRequired", defender, card.Id);
        }
        private static void Defend(ContentCatalog catalog, GameState state, Command command)
        {
            Require(state.Phase == Phase.EffectChoice && state.Pending?.Kind == "defense" && state.Pending.ChooserSeat == command.ActorSeat && state.Execution?.Attack != null,
                "invalid_defender", "当前不由你响应攻击。");
            Require(!CombatRules.UnimplementedDefenses(catalog, state, command.ActorSeat).Contains(command.Value), "response_not_implemented", "该主要防御的响应文字尚未实装。");
            var option = CombatRules.DefenseOptions(catalog, state, command.ActorSeat).SingleOrDefault(o => o.CardId == command.Value);
            Require(option != null, "invalid_defense", "只能使用本人手中的合法防御牌。");
            var instance = state.Players[command.ActorSeat].Cards.Single(c => c.CardId == command.Value && c.Zone == CardZone.InHand);
            instance.Zone = CardZone.Discarded;
            Emit(state, command, "CardDiscarded", command.ActorSeat, instance.CardId, command.ActorSeat);
            Emit(state, command, "DiscardColorShown", command.ActorSeat, detail: catalog.Card(instance.CardId).Color);
            Emit(state, command, "DefenseCalculated", command.ActorSeat, instance.CardId, command.ActorSeat,
                option!.Block ? "block" : option.Assessment.FinalDefense + ":" + option.Assessment.AttackCompared);
            var response=CardPrograms.Defense(catalog.Card(instance.CardId),state.EngineVersion);
            if (option.Primary && option.Assessment.Successful && response != null && response.Followup != DefenseFollowup.None)
            {
                var attack=state.Execution!.Attack!;
                state.Execution.DefenseResponse=new DefenseResponse
                {
                    SourceCardId=instance.CardId, SourceUnitId=attack.TargetUnitId, ControllerSeat=command.ActorSeat,
                    AttackerSeat=attack.AttackerSeat, AttackerUnitId="hero:"+attack.AttackerSeat, ProgramId=response.Id, ProgramVersion=response.Version
                };
            }
            ResolveDefense(catalog, state, command, option.Assessment.Successful);
        }
        private static void DeclineDefense(ContentCatalog catalog, GameState state, Command command)
        {
            Require(state.Phase == Phase.EffectChoice && state.Pending?.Kind == "defense" && state.Pending.ChooserSeat == command.ActorSeat && state.Execution?.Attack != null,
                "invalid_defender", "当前不由你响应攻击。");
            Emit(state, command, "DefenseDeclined", command.ActorSeat);
            ResolveDefense(catalog, state, command, false);
        }
        private static void ResolveDefense(ContentCatalog catalog, GameState state, Command command, bool successful)
        {
            var execution = state.Execution!; var attack = execution.Attack!;
            state.Pending = null; state.Phase = Phase.Action;
            Emit(state, command, "DefenseResolved", attack.DefenderSeat, detail: successful ? "success" : "failure");
            execution.AttackOutcome = successful ? "defended" : "hero_defeated";
            if (!successful) DefeatHero(state, command, attack.TargetUnitId, execution.ControllerSeat, execution.CardId);
            ContinueCard(catalog, state, command);
        }
        private static void StopCard(ContentCatalog catalog, GameState state, Command command, string reason)
        {
            Emit(state, command, "CardEffectStopped", state.Execution!.ControllerSeat, state.Execution.CardId, detail: reason);
            EndCardExecution(catalog, state, command);
        }
        private static void EndCardExecution(ContentCatalog catalog, GameState state, Command command)
        {
            if (state.Execution!.DefenseResponse != null && !ContinueDefenseResponse(catalog,state,command)) return;
            state.ActiveSeat = state.Execution!.ControllerSeat; state.Execution = null; state.Pending = null; state.Phase = Phase.Action;
            FinishAction(catalog, state, command);
        }
    }
}
