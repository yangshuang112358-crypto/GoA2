#nullable enable
using Goa2.Domain;

namespace Goa2.Rules.Cards
{
    internal enum UltimateTrigger { AfterBasicSkillDiscard, AfterPushDiscardOrDefeat, BeforeActionAdjacentDiscard, BeforeActionMoveAndRepeat, MinionBattleParticipant }

    internal sealed class UltimateProgram
    {
        public readonly string Id;
        public readonly int Version = 1;
        public readonly UltimateTrigger Trigger;
        public readonly int BasicAttackBonus, BasicAttackRangeBonus;
        public readonly bool TraverseObstacles;
        public readonly int BattleMinionCount;
        public UltimateProgram(string id, UltimateTrigger trigger, int attack = 0, int range = 0, bool traverseObstacles = false, int battleMinionCount = 0)
        { Id = id; Trigger = trigger; BasicAttackBonus = attack; BasicAttackRangeBonus = range; TraverseObstacles = traverseObstacles; BattleMinionCount = battleMinionCount; }
    }

    internal static class UltimatePrograms
    {
        private static readonly UltimateProgram BasicSkillDiscard =
            new UltimateProgram("after_basic_skill_enemy_discard", UltimateTrigger.AfterBasicSkillDiscard);
        private static readonly UltimateProgram PushPayment =
            new UltimateProgram("basic_attack_boost_and_push_payment", UltimateTrigger.AfterPushDiscardOrDefeat, 2, 2);

        private static readonly UltimateProgram ImmunePrelude =
            new UltimateProgram("immune_before_action_move_and_basic_repeat", UltimateTrigger.BeforeActionMoveAndRepeat);

        private static readonly UltimateProgram PreludeDiscard =
            new UltimateProgram("traverse_obstacles_before_action_discard", UltimateTrigger.BeforeActionAdjacentDiscard, traverseObstacles: true);
        private static readonly UltimateProgram BattleParticipant =
            new UltimateProgram("two_minions_stop_battle_on_removal", UltimateTrigger.MinionBattleParticipant, battleMinionCount: 2);

        public static UltimateProgram? Find(CardDefinition card, int engine)
        {
            if (engine >= 59 && card.Id == "brogan-12-一人成军" && card.HeroId == "brogan" && card.Color == "purple" && card.PrimaryCategory == "终极技能" &&
                card.Text == "在小兵战斗中，你视为2个小兵。如果在小兵战斗中你将被移除，则本次推线失败而不会被移除。") return BattleParticipant;
            if (engine >= 58 && card.Id == "tigerclaw-13-斗篷与匕首" && card.HeroId == "tigerclaw" && card.Color == "purple" && card.PrimaryCategory == "终极技能" &&
                card.Text == "如果你处于免疫：在你执行（或重复）任何行动前，移动最多2格；在你执行基础攻击后，你可以对不同目标重复一次基础攻击。") return ImmunePrelude;
            if (engine >= 57 && card.Id == "shargatha-12-幻化" && card.HeroId == "shargatha" &&
                card.Color == "purple" && card.PrimaryCategory == "终极技能" &&
                card.Text == "你可以穿过障碍物。在你执行行动之前，与你相邻的一个敌方英雄丢弃一张卡牌（如果可行）。")
                return PreludeDiscard;
            if (engine >= 56 && card.Id == "sabina-12-重型枪械" && card.HeroId == "sabina" &&
                card.Color == "purple" && card.PrimaryCategory == "终极技能" &&
                card.Text == "你的基础攻击+2攻击距离和+2攻击。如果你推动一名敌方英雄，该英雄丢弃一张卡牌，否则被击败。")
                return PushPayment;
            if (engine >= 55 && card.Id == "wasp-12-电闪雷鸣" && card.HeroId == "wasp" &&
                card.Color == "purple" && card.PrimaryCategory == "终极技能" &&
                card.Text == "在你执行基础技能后，场上的一个敌方英雄丢弃一张卡牌（如果可行）。")
                return BasicSkillDiscard;
            return null;
        }
    }
}
