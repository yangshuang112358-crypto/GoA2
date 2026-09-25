#nullable enable
using Goa2.Domain;

namespace Goa2.Rules.Cards
{
    internal enum UltimateTrigger { AfterBasicSkillDiscard }

    internal sealed class UltimateProgram
    {
        public readonly string Id;
        public readonly int Version = 1;
        public readonly UltimateTrigger Trigger;
        public UltimateProgram(string id, UltimateTrigger trigger) { Id = id; Trigger = trigger; }
    }

    internal static class UltimatePrograms
    {
        private static readonly UltimateProgram BasicSkillDiscard =
            new UltimateProgram("after_basic_skill_enemy_discard", UltimateTrigger.AfterBasicSkillDiscard);

        public static UltimateProgram? Find(CardDefinition card, int engine)
        {
            if (engine >= 55 && card.Id == "wasp-12-电闪雷鸣" && card.HeroId == "wasp" &&
                card.Color == "purple" && card.PrimaryCategory == "终极技能" &&
                card.Text == "在你执行基础技能后，场上的一个敌方英雄丢弃一张卡牌（如果可行）。")
                return BasicSkillDiscard;
            return null;
        }
    }
}
