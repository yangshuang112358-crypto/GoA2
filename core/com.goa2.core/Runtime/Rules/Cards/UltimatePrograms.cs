#nullable enable
using Goa2.Domain;

namespace Goa2.Rules.Cards
{
    internal enum UltimateTrigger { AfterBasicSkillDiscard, AfterPushDiscardOrDefeat, BeforeActionAdjacentDiscard }

    internal sealed class UltimateProgram
    {
        public readonly string Id;
        public readonly int Version = 1;
        public readonly UltimateTrigger Trigger;
        public readonly int BasicAttackBonus, BasicAttackRangeBonus;
        public readonly bool TraverseObstacles;
        public UltimateProgram(string id, UltimateTrigger trigger, int attack = 0, int range = 0, bool traverseObstacles = false)
        { Id = id; Trigger = trigger; BasicAttackBonus = attack; BasicAttackRangeBonus = range; TraverseObstacles = traverseObstacles; }
    }

    internal static class UltimatePrograms
    {
        private static readonly UltimateProgram BasicSkillDiscard =
            new UltimateProgram("after_basic_skill_enemy_discard", UltimateTrigger.AfterBasicSkillDiscard);
        private static readonly UltimateProgram PushPayment =
            new UltimateProgram("basic_attack_boost_and_push_payment", UltimateTrigger.AfterPushDiscardOrDefeat, 2, 2);

        private static readonly UltimateProgram PreludeDiscard =
            new UltimateProgram("traverse_obstacles_before_action_discard", UltimateTrigger.BeforeActionAdjacentDiscard, traverseObstacles: true);

        public static UltimateProgram? Find(CardDefinition card, int engine)
        {
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
