#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Goa2.Domain;

namespace Goa2.Rules.Cards
{
    // Static, public card knowledge only. This API accepts no state, seat, RNG or execution frame.
    // These are descriptive records, never a second interpreter or a source of legal commands.
    public sealed class PublicMechanicParameter
    {
        public string Key = "", Symbol = "";
        public int? Number;
        public bool? Flag;
    }
    public sealed class PublicCardMechanics
    {
        public const int Format = 1;
        public string Family = "";
        public List<PublicMechanicParameter> Parameters = new List<PublicMechanicParameter>();
        public List<string> Steps = new List<string>(), Constraints = new List<string>(), Limitations = new List<string>();
    }
    public static class PublicCardSemantics
    {
        // Exact field inventory is regression-tested against the internal static programs.
        public static readonly string[] PrimaryFields = "AttackSubtype GoldMaximum RearTargetDistance IgnoreHeavyImmunity UnitSwapTarget PlacementTarget PushTarget OptionalPushTarget FixedPushDistance TargetMoveKeepsDistance MinimumDistance Effect Duration AreaKind AdjacentAttack OnlyHeroes AttackBonusKind AttackBonusValue RangeBonusKind RangeBonusValue TextMoveDistance TextMoveMinimum RecoveryRequiresAdjacentMinion HeroTarget RecoverResolved SupportMakesUnblockable ExcludeStraightLine ExtraRemovalUsesAttackRange TextPushDistance".Split(' ');
        public static readonly string[] DefenseFields = "MinimumDistance TextMoveDistance Block IgnoresMinions RequiresAdjacentFriendlyMinion SwapAfterMove ProtectFromOtherEnemies ProtectFromOtherAttacks PersistImmunityThroughDefeat AttackKind Followup".Split(' ');
        public static readonly string[] UltimateFields = "Trigger BasicAttackBonus BasicAttackRangeBonus TraverseObstacles BattleMinionCount".Split(' ');
        public static PublicCardMechanics Describe(CardDefinition card,int engineVersion)
        {
            if(engineVersion!=GameState.CurrentEngineVersion)throw new InvalidOperationException("unsupported_semantic_engine");
            var result=new PublicCardMechanics {Family=card.PrimaryFamily};
            void N(string k,int v)=>result.Parameters.Add(new PublicMechanicParameter{Key=k,Number=v});
            void B(string k,bool v)=>result.Parameters.Add(new PublicMechanicParameter{Key=k,Flag=v});
            void S(string k,string v)=>result.Parameters.Add(new PublicMechanicParameter{Key=k,Symbol=v});
            var p=CardPrograms.Primary(card,engineVersion);
            var d=CardPrograms.Defense(card,engineVersion);
            var u=UltimatePrograms.Find(card,engineVersion);
            if(p!=null)
            {
                S("AttackSubtype",p.AttackSubtype=="远程"?"ranged":"melee");
                N("GoldMaximum",p.GoldMaximum);N("RearTargetDistance",p.RearTargetDistance);B("IgnoreHeavyImmunity",p.IgnoreHeavyImmunity);
                S("UnitSwapTarget",p.UnitSwapTarget.ToString());S("PlacementTarget",p.PlacementTarget.ToString());S("PushTarget",p.PushTarget.ToString());
                B("OptionalPushTarget",p.OptionalPushTarget);B("FixedPushDistance",p.FixedPushDistance);B("TargetMoveKeepsDistance",p.TargetMoveKeepsDistance);
                N("MinimumDistance",p.MinimumDistance);S("Effect",p.Effect?.ToString()??"");S("Duration",p.Duration.ToString());S("AreaKind",p.AreaKind.ToString());
                B("AdjacentAttack",p.AdjacentAttack);B("OnlyHeroes",p.OnlyHeroes);S("AttackBonusKind",p.AttackBonusKind.ToString());N("AttackBonusValue",p.AttackBonusValue);
                S("RangeBonusKind",p.RangeBonusKind.ToString());N("RangeBonusValue",p.RangeBonusValue);N("TextMoveDistance",p.TextMoveDistance);N("TextMoveMinimum",p.TextMoveMinimum);
                B("RecoveryRequiresAdjacentMinion",p.RecoveryRequiresAdjacentMinion);S("HeroTarget",p.HeroTarget.ToString());B("RecoverResolved",p.RecoverResolved);
                B("SupportMakesUnblockable",p.SupportMakesUnblockable);B("ExcludeStraightLine",p.ExcludeStraightLine);B("ExtraRemovalUsesAttackRange",p.ExtraRemovalUsesAttackRange);N("TextPushDistance",p.TextPushDistance);
                result.Steps=p.Instructions.Select(x=>x.ToString()).ToList();
                // The internal record has defaults for unrelated branches. They are not card facts.
                void Without(params string[] keys)=>result.Parameters.RemoveAll(x=>keys.Contains(x.Key));
                bool Has(params InstructionKind[] kinds)=>p.Instructions.Any(kinds.Contains);
                if(!Has(InstructionKind.Attack))Without("AttackSubtype","MinimumDistance","AdjacentAttack","OnlyHeroes","AttackBonusKind","AttackBonusValue","RangeBonusKind","RangeBonusValue","SupportMakesUnblockable","ExcludeStraightLine");
                if(!Has(InstructionKind.ChooseUnitSwapTarget,InstructionKind.ChooseOptionalUnitSwapTarget))Without("UnitSwapTarget");
                if(!Has(InstructionKind.ChooseSelfPlacement))Without("PlacementTarget");
                if(!Has(InstructionKind.ChooseUnitPush))Without("PushTarget","OptionalPushTarget","FixedPushDistance");
                if(!p.Effect.HasValue)Without("Effect","Duration","AreaKind");
                if(!Has(InstructionKind.OptionalGoldTransfer))Without("GoldMaximum");
                if(p.HeroTarget!=HeroTargetKind.EnemyBehindAttackTarget)Without("RearTargetDistance");
                if(!Has(InstructionKind.ChooseFriendlyMinionTarget))Without("IgnoreHeavyImmunity");
                if(!Has(InstructionKind.OptionalRecoverDiscard))Without("RecoveryRequiresAdjacentMinion","RecoverResolved");
                if(!Has(InstructionKind.OptionalMinionRemovalAfterDefeat))Without("ExtraRemovalUsesAttackRange");
                if(p.Instructions.Contains(InstructionKind.Attack))result.Constraints.Add("attack_default_enemy_legal_target");
                if(p.Instructions.Contains(InstructionKind.OptionalRecoverDiscard))result.Constraints.Add("recovery_card_chosen_by_recipient");
                if(p.Instructions.Contains(InstructionKind.ApplyEffectIfRecovered))result.Constraints.Add("immunity_requires_actual_recovery");
                if(p.Instructions.Contains(InstructionKind.ApplyPoison)||p.Instructions.Contains(InstructionKind.ApplyDefensePoison))result.Constraints.Add("poison_nonstacking_round_expiry_source_independent");
                if(p.Effect.HasValue)result.Constraints.Add("effect_source_recovery_or_defeat_cancels_unless_exception");
                if(p.Effect==EffectKind.AttackFromDiscard)result.Constraints.Add("one_reaction_per_discard_after_whole_causing_action");
                if(p.Effect==EffectKind.EnemyActionMovementLimitOne)result.Constraints.Add("limit_one_at_action_origin_not_card_text_movement");
                if(p.Effect==EffectKind.EnemyMovementGoldOnly||p.Effect==EffectKind.EnemyMovementGoldOrRedOnly)result.Constraints.Add("dynamic_adjacent_movement_source_card_color_not_push_or_place");
                if(p.Effect==EffectKind.PetrifyNearestEnemyHeroes)result.Constraints.Add("nearest_before_immunity_all_ties");
                if(p.Effect==EffectKind.PetrifyNearestEnemyHeroes||p.Effect==EffectKind.PetrifyAllEnemyHeroes)result.Constraints.Add("petrify_preserves_hero_actions_recompute_after_whole_move");
                if(p.Effect==EffectKind.EnemyMeleeFriendlyForOwnDefense||p.Effect==EffectKind.EnemyLightMinionsFriendlyForOwnDefense||p.Effect==EffectKind.EnemyAllMinionsFriendlyForOwnDefense)result.Constraints.Add("minion_team_view_for_owner_defense_only");
                if(p.PlacementTarget==PlacementTargetKind.EmptyNoSpawnAwayFromEmptySpawns)result.Constraints.Add("empty_spawn_test_after_placement_includes_minion_spawns");
                if(p.Instructions.Contains(InstructionKind.ChooseOrbitalTarget))result.Constraints.Add("orbital_excludes_self_nonadjacent_preserves_distance");
                if(p.Instructions.Contains(InstructionKind.OtherHeroDiscardOrDefeatIfAvailable))result.Constraints.Add("no_rear_target_continues_original_attack");
                if(p.Instructions.Any(x=>x.ToString().Contains("Repeat")||x==InstructionKind.OptionalDifferentFullAttack||x==InstructionKind.OptionalDifferentAttackIfAdjacentEnemy))result.Constraints.Add("repeat_serial_responses_returns_reactions_before_next");
                if(p.Instructions.Contains(InstructionKind.OptionalRepeatAttackAfterHeroDefeat))
                    result.Constraints.Add("repeat_after_each_hero_defeat_without_fixed_count_limit");
                if(p.GoldMaximum>0)result.Limitations.Add("Q09_final_if_able_move_current_engine_requires_route_not_user_ratified");
                if(p.PlacementTarget==PlacementTargetKind.SafeInSkillRangeNearObstacle)result.Limitations.Add("U017_obstacle_scope_current_engine_not_fully_ratified");
                if(p.Instructions.Any(x=>x==InstructionKind.ChooseUnitPlacement||x==InstructionKind.ChooseUnitPush||x==InstructionKind.ChooseOrbitalTarget)||p.UnitSwapTarget==UnitSwapTargetKind.AnyUnitInAttackRange)result.Limitations.Add("D050_map_markers_not_in_current_six_hero_content");
            }
            else if(d!=null)
            {
                N("MinimumDistance",d.MinimumDistance);N("TextMoveDistance",d.TextMoveDistance);B("Block",d.Block);B("IgnoresMinions",d.IgnoresMinions);
                B("RequiresAdjacentFriendlyMinion",d.RequiresAdjacentFriendlyMinion);B("SwapAfterMove",d.SwapAfterMove);B("ProtectFromOtherEnemies",d.ProtectFromOtherEnemies);B("ProtectFromOtherAttacks",d.ProtectFromOtherAttacks);B("PersistImmunityThroughDefeat",d.PersistImmunityThroughDefeat);
                S("AttackKind",d.AttackKind.ToString());S("Followup",d.Followup.ToString());result.Steps.Add("PrimaryDefense");
                if(d.PersistImmunityThroughDefeat)result.Constraints.Add("defense_immunity_even_on_failure_exempts_attacker");
            }
            else if(u!=null)
            {
                S("Trigger",u.Trigger.ToString());N("BasicAttackBonus",u.BasicAttackBonus);N("BasicAttackRangeBonus",u.BasicAttackRangeBonus);B("TraverseObstacles",u.TraverseObstacles);N("BattleMinionCount",u.BattleMinionCount);
                result.Steps.Add(u.Trigger.ToString());
                if(u.Trigger==UltimateTrigger.BeforeActionMoveAndRepeat)result.Constraints.AddRange(new[]{"basic_attack_repeat_once_different_target_no_recursion","immune_prelude_move_up_to_two_no_future_route_precheck"});
                if(u.Trigger==UltimateTrigger.BeforeActionAdjacentDiscard)result.Constraints.Add("prelude_enemy_may_have_empty_hand_traverse_units_and_obstacles");
                if(u.Trigger==UltimateTrigger.BeforeActionMoveAndRepeat||u.Trigger==UltimateTrigger.BeforeActionAdjacentDiscard)result.Constraints.Add("prelude_includes_primary_secondary_fast_defense_excludes_internal_move");
                if(u.Trigger==UltimateTrigger.MinionBattleParticipant)result.Constraints.Add("battle_hero_any_region_contributes_two_removal_stops_push");
                if(u.Trigger==UltimateTrigger.AfterBasicSkillMinionBattle)result.Constraints.Add("optional_battle_only_if_owner_in_current_combat_region");
            }
            else throw new InvalidOperationException("unbound_public_card_semantics:"+card.Id);
            result.Constraints.Add("next_turn_does_not_cross_round");
            return result;
        }
    }
}
