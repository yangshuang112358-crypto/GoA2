#nullable enable
using System.Collections.Generic;
using Goa2.Domain;

namespace Goa2.Rules.Cards
{
    internal enum InstructionKind { ChooseAttackTarget, Attack, End }
    internal enum DefenseProgramKind { BlockNonAdjacentRanged, BlockRanged, NumericIgnoreMinions }
    internal sealed class AttackProgram
    {
        public readonly string Id;
        public readonly int Version = 1, MinimumDistance;
        public readonly IReadOnlyList<InstructionKind> Instructions;
        public AttackProgram(string id, int minimumDistance)
        {
            Id = id; MinimumDistance = minimumDistance;
            Instructions = System.Array.AsReadOnly(new[] { InstructionKind.ChooseAttackTarget, InstructionKind.Attack, InstructionKind.End });
        }
    }
    internal static class CardPrograms
    {
        private static readonly AttackProgram NonAdjacentRanged = new AttackProgram("non_adjacent_ranged_attack", 2);
        // Binding IDs is confined to this registry. Shared execution never branches on a card ID.
        private static readonly Dictionary<string,string> AttackTexts = new Dictionary<string,string>
        {
            ["sabina-01-拔枪"] = "选择攻击距离内且不与你相邻的一个单位为目标。",
            ["shargatha-02-快速突刺"] = "选择攻击距离内且与你不相邻的一个单位为目标。"
        };
        private static readonly Dictionary<string,(string text, DefenseProgramKind kind)> Defenses = new Dictionary<string,(string, DefenseProgramKind)>
        {
            ["wasp-07-抵挡屏障"] = ("如果攻击者不与你相邻，抵挡一次远程攻击。", DefenseProgramKind.BlockNonAdjacentRanged),
            ["tigerclaw-18-躲闪"] = ("抵挡一次远程攻击", DefenseProgramKind.BlockRanged),
            ["arien-13-挑战者"] = ("无视所有的小兵防御修正。", DefenseProgramKind.NumericIgnoreMinions)
        };
        public static AttackProgram? Attack(CardDefinition card) => card.PrimaryFamily == "attack" && card.Subtype == "远程" &&
            AttackTexts.TryGetValue(card.Id, out var expected) && card.Text == expected ? NonAdjacentRanged : null;
        public static DefenseProgramKind? Defense(CardDefinition card) => card.PrimaryFamily == "defense" &&
            Defenses.TryGetValue(card.Id, out var binding) && card.Text == binding.text ? binding.kind : (DefenseProgramKind?)null;
    }
}
