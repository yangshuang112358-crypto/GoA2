using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Goa2.Domain;

namespace Goa2.Ai
{
    public static class PublicEventSemantics
    {
        // Closed vocabularies. Unexpected data fails before inference, rather than silently dropping it.
        public static readonly string[] Reasons = ("empty_hand declined skip immune no_target_selected no_candidates no_targets no_valid_path no_available_gold pre_attack_move_used source_absent no_destinations no_charge_route no_placement_targets no_push_targets target_invalid no_legal_attack next_turn_outside_round hero_defeated card_retrieved card_swapped round_end already_poisoned obstacle occupied map_edge target_absent not_adjacent source_or_target_absent target_cell_occupied target_not_defeated no_adjacent_minions " +
            "success failure moved paid defeated skipped completed discarded cards_swapped occupied_or_unreachable not_reachable target_position_occupied target_position_unreachable battle_completed unblockable crystal frontline frontline_marks fountain next_turn_expired skill_suppressed requires_ranged requires_non_ranged requires_minimum_distance requires_adjacent_friendly_minion").Split(' ');
        private static readonly HashSet<string> EmptyOnly = new HashSet<string>(("ActionMinionBattleOffered ActionMinionBattleSkipped ActionPassed ActionRepeatChoiceRequired ActionRepeatSkipped ActionStarted AttackCalculated AttackRepeatChoiceRequired AttackRepeatSkipped AttackTargetChoiceRequired CardResolved CardRevealed CardSelected CardSelectionCancelled CardSwapChoiceRequired DefenseChoiceRequired DefenseDeclined DefenseResponseCompleted DeploymentStarted DiscardAttackChoiceRequired DiscardAttackCompleted DiscardAttackStarted EmptyHandSkipped EffectMinionChoiceRequired EffectTargetChoiceRequired ForcedDiscardCompleted ForcedDiscardRequired GoldTransferChoiceRequired HeroRespawnChoiceRequired HeroRespawned InitiativeChoiceRequired InitiativeChosen MinionReturnChoiceRequired NoDefenseAvailable OptionalDiscardCompleted OptionalDiscardRequired OtherMoveTargetChoiceRequired PlacementChoiceRequired PrimaryActionStarted PrimaryOptionRequired PurpleCardGranted PushGroupCompleted RecoverDiscardCompleted RecoverDiscardRequired RetaliationDiscardDeclined SelectionConfirmed UnitSwapChoiceRequired CardDiscarded CardRecovered").Split(' '));
        private static readonly HashSet<string> ReasonEvents = new HashSet<string>(("AttackRepeatUnavailable CardEffectStopped CardSwapSkipped EffectMinionRemovalSkipped EffectMoveSkipped EffectNotScheduled EffectTargetSkipped ForcedDiscardSkipped ForcedPaymentDefeat GoldTransferSkipped OptionalDiscardSkipped PoisonExpired PoisonUnchanged PushSkipped PushStopped RecoverDiscardSkipped UnitSwapSkipped DefenseMoveResolved").Split(' '));
        public static ObservedAttack? Attack(AttackBreakdown? a,StableIds ids)=>a==null?null:new ObservedAttack {Card=ids.Card(a.SourceCardId),Reason=a.CardTextReason,Target=a.TargetUnitId,Attacker=a.AttackerSeat,Defender=a.DefenderSeat,Base=a.BaseAttack,Bonus=a.AttackBonus,Support=a.EnemySupport,Guard=a.FriendlyGuard,Final=a.FinalAttack,TextBonus=a.CardTextBonus,UltimateBonus=a.UltimateBonus,Ranged=a.Ranged,Unblockable=a.Unblockable,TextSources=a.CardTextSourceUnits.ToList(),SupportSources=a.EnemySupportSources.ToList(),GuardSources=a.FriendlyGuardSources.ToList()};
        public static void Fill(GameEvent e,ObservedEvent o,StableIds ids,Func<string,int> effectKey)
        {
            int N(string s)=>int.Parse(s,CultureInfo.InvariantCulture);
            string Reason(string s)=>Reasons.Contains(s)?s:throw new InvalidOperationException("unsupported_event_reason:"+e.Kind+":"+s);
            o.Attack=Attack(e.AttackValues,ids);
            if(EmptyOnly.Contains(e.Kind) && e.Detail=="")return;
            if(e.Kind=="EffectMinionRemovalSkipped" && e.Detail=="")return;
            if(ReasonEvents.Contains(e.Kind)){o.Value=Reason(e.Detail);return;}
            string[] p;
            switch(e.Kind)
            {
                case "MinionDefeated":case "MinionRemoved":case "MinionSpawned":case "AttackDeclared":case "AttackTargetChosen":
                case "EffectMinionRemoved":case "UnitPlaced":case "UnitsSwapped":case "UnitDisplacementPrevented":
                case "OtherMoveTargetChosen":case "MinionReturnPlaced":case "MinionReturnMoved":case "MinionReturnCompleted":
                case "MinionProtectionChoiceRequired":case "MinionDefeatPrevented":case "MinionProtectionDeclined":case "MinionBattleStoppedByHero":
                case "PlacementChoiceRequired":case "EffectTargetChosen":case "ActionRepeated":case "AttackRepeated":case "MinionSpawnChoiceRequired":case "SpawnOrderRulingRequired":case "HeroDefeatSource":
                    if(e.Detail.StartsWith("by:",StringComparison.Ordinal))goto case "UnitMoved";
                    if(e.Detail=="skip" || e.Detail=="ultimate")o.Value=e.Detail;else o.Unit=e.Detail;return;
                case "UnitMoved":case "UnitPushed":
                    p=e.Detail.Split('|');
                    if(p.Length==2 && p[0].StartsWith("by:",StringComparison.Ordinal)&&p[1].StartsWith("unit:",StringComparison.Ordinal)){o.OtherSeat=N(p[0].Substring(3));o.Unit=p[1].Substring(5);}
                    else if(p.Length==1 && e.Detail.StartsWith("by:",StringComparison.Ordinal))o.OtherSeat=N(e.Detail.Substring(3));
                    else if(new[]{"Secondary","Fast","Primary","CardText","BeforeAction","DefenseResponse"}.Contains(e.Detail))o.Value=e.Detail;
                    else throw new InvalidOperationException("unsupported_move_detail");return;
                case "EffectTargetChoiceRequired":case "ActionRepeatChoiceRequired":
                    o.Value=e.Detail switch {"push_all_adjacent"=>"choose_push_order","blocked_push_discard_target"=>"choose_forced_discard_order","single_push_target"=>"choose_push_target","unit_placement_target"=>"choose_unit_to_place","unit_placement_repeat"=>"choose_repeat_placement",_=>throw new InvalidOperationException("unknown_public_choice_detail:"+e.Detail)};return;
                case "EffectMoveChoiceRequired":if(e.Detail=="Secondary"||e.Detail=="Fast")o.Value=e.Detail;
                    else if(e.Detail.StartsWith("push_up_to:",StringComparison.Ordinal)){o.Value="push_distance";o.Amount=N(e.Detail.Substring(11));}
                    else o.Amount=N(e.Detail);return;
                case "HeroDeployed":p=e.Detail.Split(',');o.To=new Hex(N(p[0]),N(p[1]));return;
                case "HeroDefeated":o.OtherSeat=N(e.Detail.Substring(3));return;
                case "GoldAwarded":case "AssistGoldAwarded":case "CrystalDamaged":case "RoundCompensationGranted":case "CardsRecalled":case "MinionsCleared":
                case "AttackRangeDetermined":case "RoundMinionChoiceRequired":case "RoundEndStarted":case "RoundEnded":case "UpgradesStarted":case "MinionBattleCompleted":case "UpgradeChoiceRequired":o.Amount=N(e.Detail);return;
                case "GoldTransferred":p=e.Detail.Split('|');o.OtherSeat=N(p[0].Substring(7));o.Amount=N(p[1].Substring(7));return;
                case "HeroLeveled":case "MinionBattleCounted":case "ActionMinionBattleCounted":p=e.Detail.Split(':');o.Amount=N(p[0]);o.Amount2=N(p[1]);o.Amount3=N(p[2]);return;
                case "PlanningStarted":case "TurnEnded":p=e.Detail.Split(':');o.Amount=N(p[0]);o.Amount2=N(p[1]);return;
                case "AttackResolved":int at=e.Detail.IndexOf(':');o.Value=e.Detail.Substring(0,at);o.Unit=e.Detail.Substring(at+1);return;
                case "HeroChosen":case "FrontlineAdvanced":case "FrontlineCompleted":case "FrontlineMarkGained":case "ActionMinionBattleCompleted":
                case "DiscardColorShown":case "RecoveredColorShown":case "DefenseResolved":case "PrimaryOptionChosen":case "PoisonApplied":o.Value=e.Detail;return;
                case "CardSwapColorsShown":p=e.Detail.Split(':');o.Value=p[0];o.SecondaryValue=p[1];return;
                case "DecisionCoinFlipped":p=e.Detail.Split(new[]{" wins; now "},StringSplitOptions.None);o.Value=p[0];o.SecondaryValue=p[1];return;
                case "CoinTossSettled":o.Value=e.Detail.Split('|').Last();return;
                case "CoinTossStarted":case "CoinTossStuck":case "CoinRerollVote":case "DiscardReactionQueued":return; // transport/frame ID is not a game fact
                case "EffectCreated":case "EffectActivated":case "EffectScheduled":case "EffectExpired":o.EffectKey=effectKey(e.Detail);return;
                case "ProtectionActivated":case "ProtectionExpired":
                    at=e.Detail.IndexOf(':');o.Value=e.Detail.Substring(0,at);o.EffectKey=effectKey(e.Detail.Substring(at+1));return;
                case "EffectCancelled":
                    p=e.Detail.Split('|');o.EffectKey=effectKey(p[1]);
                    if(Reasons.Contains(p[0]))o.Value=p[0];else {o.OtherCard=ids.Card(p[0]);o.Value="skill_cancelled";}return;
                case "UltimateTriggered":
                    if(e.Detail.StartsWith("before:",StringComparison.Ordinal)){o.Value="before_action";o.SecondaryValue=e.Detail.Substring(7);}
                    else if(e.Detail.StartsWith("hero:",StringComparison.Ordinal)||e.Detail.StartsWith("minion:",StringComparison.Ordinal))o.Unit=e.Detail;
                    else o.OtherCard=ids.Card(e.Detail);return;
                case "UltimateCompleted":
                    if(e.Detail.StartsWith("before-action:",StringComparison.Ordinal)||e.Detail.StartsWith("before:",StringComparison.Ordinal))return;
                    o.Value=Reason(e.Detail);return;
                case "ForcedPaymentPaid":o.Value=Reason(e.Detail);return;
                case "DiscardReactionSkipped":o.Value=Reason(e.Detail);return;
                case "MatchWon":p=e.Detail.Split(':');o.Value=p[0];o.SecondaryValue=Reason(p[1]);return;
                case "CardUpgraded":o.OtherCard=ids.Card(e.Detail);return;
                case "UpgradeBonusGranted":p=e.Detail.Split(':');o.Value=ObservationProjector.BonusId(p[0]);o.Amount=N(p[1]);return;
                case "CardsSwapped":o.OtherCard=ids.Card(e.Detail);return;
                case "DefenseCalculated":if(e.Detail=="block"){o.Value="block";return;}p=e.Detail.Split(':');o.Amount=N(p[0]);o.Amount2=N(p[1]);return;
                case "UltimateRepeatOffered":if(e.Detail=="")return;break;
                case "RoundEndReached":if(e.Detail=="本切片到达轮末；轮末结算与升级尚未实装。")return;break; // legacy notice, no additional rule fact
                default:throw new InvalidOperationException("unsupported_event_semantics:"+e.Kind+":"+e.Detail);
            }
            throw new InvalidOperationException("unsupported_event_semantics:"+e.Kind+":"+e.Detail);
        }
    }
}
