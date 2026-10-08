#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Goa2.Domain;

namespace Goa2.Rules.Cards
{
    public sealed class PublicEffectMeaning
    {
        public string Kind="",Operation="",Target="",Condition="",Sampling="";
        public int? Amount;
    }
    public static class PublicEffectSemantics
    {
        // Static semantics of the existing effect queries. Scope/expiry remain on each card/effect instance.
        private const string Rows=@"MovementBoundary|forbid_area_crossing|enemy_unit|movement_path_crosses_area_boundary|each_path_step|
SkillSuppression|forbid_skill|enemy_hero|inside_area_nonimmune_to_source|action_start|
NonAdjacentRangedImmunity|immune_ranged_attack|protected_hero|attacker_nonadjacent|each_attack|
FriendlyBasicMinionsRanged|combat_kind_ranged|friendly_minions_including_immune|owner_team_basic_attack|each_attack|
FriendlyAttackMinionsRanged|combat_kind_ranged|friendly_minions_including_immune|owner_team_attack|each_attack|
FriendlyAttackMinionsDual|combat_kind_melee_and_ranged|friendly_minions_including_immune|owner_team_attack|each_attack|
FriendlyNearMinionDefense|defense_bonus|self_and_friendly_heroes|adjacent_friendly_minion|each_defense|1
FriendlyDisplacementProtection|forbid_enemy_displacement|self_and_friendly_units|enemy_move_push_swap_place|each_displacement|
ImmunityAndUnitTraversal|immune_all_and_traverse_units_with_empty_legal_endpoint|self|effect_active|each_action|
OtherEnemyActionImmunity|immune_all_actions|protected_hero|enemy_controller_except_exempt|each_action|
FriendlyMeleeDefeatPrevention|optional_discard_prevents_defeat|friendly_melee_minion|source_controller_has_hand|before_defeat|1
FriendlyNonHeavyDefeatPrevention|optional_discard_prevents_defeat|friendly_nonheavy_minion|source_controller_has_hand|before_defeat|1
FriendlyMinionDefeatPrevention|optional_discard_prevents_defeat|friendly_minion_including_heavy|source_controller_has_hand|before_defeat|1
AttackFromDiscard|execute_discarded_attack_primary|self|one_trigger_per_discard_any_reason|after_whole_causing_action|1
AttackActionImmunity|immune_attack_action|self|attack_including_text|each_action|
OtherEnemyAttackImmunity|immune_attack_action|protected_hero|enemy_controller_except_exempt|each_action|
EnemyMeleeFriendlyForOwnDefense|treat_as_friendly_for_defense|enemy_melee_minion|only_owner_defending_nonimmune|each_defense|
EnemyLightMinionsFriendlyForOwnDefense|treat_as_friendly_for_defense|enemy_melee_or_ranged_minion|only_owner_defending_nonimmune|each_defense|
EnemyAllMinionsFriendlyForOwnDefense|treat_as_friendly_for_defense|enemy_minion_including_immune|only_owner_defending|each_defense|
EnemyActionMovementLimitOne|limit_move_distance|enemy_unit|only_primary_secondary_fast|movement_origin|1
EnemyMovementGoldOrRedOnly|forbid_move|adjacent_enemy_hero|actual_move_source_card_not_gold_or_red|each_move_origin|
EnemyMovementGoldOnly|forbid_move|adjacent_enemy_hero|actual_move_source_card_not_gold|each_move_origin|
PetrifyNearestEnemyHeroes|immune_immobile_terrain_preserve_hero|nearest_enemy_heroes_all_ties|nearest_before_immunity|after_whole_move|
PetrifyAllEnemyHeroes|immune_immobile_terrain_preserve_hero|all_enemy_heroes_in_area|nonimmune_to_source|after_whole_move|";
        public static List<PublicEffectMeaning> Describe()
        {
            var result=Rows.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries).Select(line=>line.Split('|')).Select(p=>new PublicEffectMeaning{Kind=p[0],Operation=p[1],Target=p[2],Condition=p[3],Sampling=p[4],Amount=p[5]==""?(int?)null:int.Parse(p[5],System.Globalization.CultureInfo.InvariantCulture)}).ToList();
            if(!result.Select(x=>x.Kind).OrderBy(x=>x,StringComparer.Ordinal).SequenceEqual(Enum.GetNames(typeof(EffectKind)).OrderBy(x=>x,StringComparer.Ordinal)))throw new InvalidOperationException("unreviewed_public_effect_semantics");
            return result;
        }
    }
}
