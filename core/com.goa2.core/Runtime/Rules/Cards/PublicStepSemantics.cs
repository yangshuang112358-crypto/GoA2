#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Goa2.Rules.Cards
{
    public sealed class PublicMechanicStep
    {
        public string Code="",Operation="",Chooser="",Target="",Condition="",NoTarget="";
        public bool Optional;
    }
    public static class PublicStepSemantics
    {
        // Describes the current interpreter's public meaning; never executes a step.
        // Columns: opcode / operation / chooser / target / optional / condition / no-target behavior.
        private const string Rows=@"ChooseAttackTarget|select|controller|enemy_attack_target|false|card_target_parameters|stop_action
Attack|attack|defender_if_hero|selected_target|false|target_still_legal|stop_action
End|finish|none|action|false|responses_returns_reactions_then_completion|finish
ApplyEffect|effect|none|card_effect_area|false|duration_inside_round|continue
ApplyEffectIfRecovered|effect|none|self|false|actually_recovered|continue
CancelAdjacentSkillEffects|cancel_effect|none|adjacent_enemy_skill_sources|false|cancellable_source|continue
ChooseOptionalDiscard|discard|controller|own_hand|true|hand_nonempty|continue
DetermineAttackRange|calculate|none|attack_range|false|discard_condition_at_this_step|continue
OptionalTextMove|move|controller|self|true|text_distance|continue
OptionalRecoverDiscard|recover|recipient|recipient_allowed_zones|true|card_recovery_condition|continue
OptionalPreAttackTextMove|move|controller|self|true|text_distance|continue
OptionalTextMoveIfNoPreMove|move|controller|self|true|pre_move_not_used|continue
ChooseHeroTarget|select|controller|hero_target_parameter|false|target_parameters|stop_action
OptionalRepeatAttackAfterHeroDefeat|repeat_attack|controller|enemy_attack_target|true|previous_attack_defeated_hero_each_time|continue
OptionalMinionRemovalAfterDefeat|remove|controller|enemy_nonheavy_minion|true|minion_defeated_no_enemy_hero_in_attack_range|continue
PushAttackTargetIfAdjacent|push|none|selected_target|false|target_still_adjacent|continue
MoveIntoAttackTargetCell|move|none|self_to_attack_target_origin|false|target_cell_reachable|continue
RequiredStraightMoveToAttack|move|controller|self|false|straight_route_to_attack_required|stop_action
RequiredStraightMoveThroughEnemy|move|controller|self|false|straight_route_through_enemy_required|stop_action
PrimaryMovement|move|controller|self|true|primary_movement_value|continue
TargetDiscardIfAble|discard|target|target_hand|false|target_has_hand|continue
TargetDiscardOrDefeat|discard_or_defeat|target|target_hand_or_self|false|target_still_present|continue
OptionalOtherHeroDiscard|select_then_discard|controller_then_target|other_enemy_hero|true|hero_target_parameter|continue
OptionalGoldTransfer|transfer_gold|controller|adjacent_enemy_hero|true|available_gold_and_maximum|continue
RequiredStraightMoveIfAble|move|controller|self|false|legal_exact_straight_route|continue
OptionalDifferentAttackIfAdjacentEnemy|repeat_attack|controller|different_enemy_target|true|adjacent_enemy_hero|continue
ChooseSelfPlacement|place|controller|self|false|placement_target_parameter|stop_action
OptionalMoveOtherAdjacentToTarget|select_then_move|controller|unit_adjacent_to_target_except_self|true|target_still_valid|continue
ChooseFriendlyMinionTarget|select|controller|friendly_minion|false|skill_range_ignore_heavy_parameter|stop_action
OptionalTargetUnitMove|move|controller|selected_unit|true|text_distance_and_distance_constraint|continue
OptionalRepeatFriendlyMinionMove|select_repeat|controller|friendly_minion|true|skill_range_ignore_heavy_parameter|finish_repeat
ChooseUnitSwapTarget|select|controller|swap_target_parameter|false|legal_swap_pair|stop_action
SwapTargetUnits|swap|none|self_and_selected_unit|false|legal_swap_pair|stop_action
ChooseOptionalUnitSwapTarget|select|controller|swap_target_parameter|true|legal_swap_pair|continue_after_swap
PushAllAdjacentEnemies|push_serial|controller|adjacent_enemies|false|select_order_each_target_once|continue
DiscardBlockedPushHeroesIfAble|discard_serial|controller_then_each_target|pushed_heroes_blocked_by_obstacle_or_unit|false|target_has_hand|continue
ChooseProtectionOrSelfRecovery|choose_branch|controller|protect_or_recover_self|false|recovery_requires_empty_discard|continue
ChooseNearestApproachTarget|select|controller|nearest_legal_nonadjacent_enemy|false|attack_range_nonimmune|stop_action
OptionalApproachMove|move|controller|selected_unit|true|shortest_valid_path_toward_source|continue
OptionalRepeatApproach|select_repeat|controller|nearest_legal_nonadjacent_enemy|true|repeat_once|continue
OptionalOtherHeroDiscardOrDefeat|select_then_payment|controller_then_target|other_enemy_hero|true|hero_target_parameter|continue
ChooseUnitPush|select_then_push|controller|push_target_parameter|false|optional_target_and_fixed_distance_parameters|stop_unless_optional
ChooseUnitPlacement|select_then_place|controller|nonlinear_unit_in_attack_range|false|empty_adjacent_destination|stop_action
OptionalRepeatUnitPlacement|select_then_place|controller|nonlinear_unit_in_attack_range|true|repeat_once|continue
OtherHeroDiscardOrDefeatIfAvailable|select_then_payment|controller_then_target|enemy_hero_behind_attack_target|false|rear_target_distance|continue_original_attack
OptionalDifferentFullAttack|repeat_full_attack|controller|different_enemy_target|true|repeat_once|continue_after_repeat
ChooseOrbitalTarget|select|controller|other_nonadjacent_unit|false|skill_range|stop_action
OptionalRepeatOrbitalMove|select_repeat|controller|other_nonadjacent_unit|true|explicit_remaining_repeat_steps|finish_repeat
ApplyPoison|poison|none|selected_hero|false|attack_and_initiative_passives|continue
ApplyDefensePoison|poison|none|selected_hero|false|attack_initiative_defense_passives|continue
PrimaryDefense|defend|self|incoming_attack|false|defense_parameters|unavailable
AfterBasicSkillDiscard|discard|controller_then_target|enemy_hero_anywhere|false|after_basic_skill|continue
AfterPushDiscardOrDefeat|discard_or_defeat|pushed_hero|enemy_hero_hand_or_self|false|after_push_by_owner|continue
BeforeActionAdjacentDiscard|discard|controller_then_target|adjacent_enemy_hero|false|before_outer_action|continue
BeforeActionMoveAndRepeat|move_and_repeat|controller|self_then_different_target|true|owner_immune_before_outer_action|continue
MinionBattleParticipant|battle_contribution|team_captain|self|false|hero_present_any_region|stop_battle_on_removal
AfterBasicSkillMinionBattle|battle|controller_then_captains|current_combat_region|true|owner_in_current_combat_region_after_basic_skill|continue";
        public static List<PublicMechanicStep> Describe()
        {
            var rows=Rows.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries).Select(line=>line.Split('|')).Select(p=>new PublicMechanicStep{Code=p[0],Operation=p[1],Chooser=p[2],Target=p[3],Optional=bool.Parse(p[4]),Condition=p[5],NoTarget=p[6]}).ToList();
            var expected=Enum.GetNames(typeof(InstructionKind)).Concat(Enum.GetNames(typeof(UltimateTrigger))).Concat(new[]{"PrimaryDefense"}).OrderBy(x=>x,StringComparer.Ordinal);
            if(!rows.Select(x=>x.Code).OrderBy(x=>x,StringComparer.Ordinal).SequenceEqual(expected))throw new InvalidOperationException("unreviewed_public_step_semantics");
            return rows;
        }
    }
}
