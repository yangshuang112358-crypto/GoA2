#nullable enable
using System.Collections.Generic;
using Goa2.Domain;

namespace Goa2.Rules.Cards
{
    internal enum InstructionKind { ChooseAttackTarget, Attack, End, ApplyEffect, CancelAdjacentSkillEffects }
    internal enum DefenseProgramKind { BlockNonAdjacentRanged, BlockRanged, NumericIgnoreMinions }
    internal sealed class PrimaryProgram
    {
        public readonly string Id;
        public readonly int Version = 1, MinimumDistance;
        public readonly IReadOnlyList<InstructionKind> Instructions;
        public readonly EffectKind? Effect;
        public readonly EffectDuration Duration;
        public readonly EffectAreaKind AreaKind;
        public readonly bool AdjacentAttack, OnlyHeroes;
        public PrimaryProgram(string id, int minimumDistance, bool adjacent=false, bool onlyHeroes=false, EffectKind? effect=null,
            EffectAreaKind areaKind=EffectAreaKind.SkillRange, params InstructionKind[] instructions)
        {
            Id = id; MinimumDistance = minimumDistance;
            AdjacentAttack=adjacent; OnlyHeroes=onlyHeroes; Effect=effect; AreaKind=areaKind; Duration=EffectDuration.ThisTurn;
            Instructions = System.Array.AsReadOnly(instructions.Length>0 ? instructions : new[] { InstructionKind.ChooseAttackTarget, InstructionKind.Attack, InstructionKind.End });
        }
        public PrimaryProgram(string id, EffectKind effect, EffectDuration duration)
        {
            Id = id; Effect = effect; Duration = duration;
            Instructions = System.Array.AsReadOnly(new[] { InstructionKind.ApplyEffect, InstructionKind.End });
        }
    }
    internal static class CardPrograms
    {
        private static readonly PrimaryProgram NonAdjacentRanged = new PrimaryProgram("non_adjacent_ranged_attack", 2);
        private static readonly PrimaryProgram ShiningBlade = new PrimaryProgram("adjacent_hero_attack_cancel_skills",1,
            adjacent:true,onlyHeroes:true,effect:EffectKind.SkillSuppression,areaKind:EffectAreaKind.Adjacent,
            instructions:new[] {InstructionKind.ChooseAttackTarget,InstructionKind.Attack,InstructionKind.CancelAdjacentSkillEffects,InstructionKind.ApplyEffect,InstructionKind.End});
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
        private static readonly Dictionary<string,(string text, PrimaryProgram program)> Skills = new Dictionary<string,(string, PrimaryProgram)>
        {
            ["wasp-06-静电封锁"] = ("此回合：技能范围内的敌方单位无法移动或快速移动来脱离技能范围。技能范围外的敌方单位无法移动或快速移动进入技能范围。", new PrimaryProgram("movement_boundary_aura",EffectKind.MovementBoundary,EffectDuration.ThisTurn)),
            ["arien-06-打断施法"] = ("此回合：在技能范围内的敌方英雄不能执行技能。（打断施法不会阻止攻击行动。）", new PrimaryProgram("skill_suppression_aura",EffectKind.SkillSuppression,EffectDuration.ThisTurn))
        };
        public static PrimaryProgram? Primary(CardDefinition card, int engineVersion)
        {
            if (card.PrimaryFamily == "attack" && card.Subtype == "远程" && AttackTexts.TryGetValue(card.Id,out var expected) && card.Text == expected) return NonAdjacentRanged;
            if (engineVersion >= 2 && card.PrimaryFamily == "skill" && card.Subtype == "范围" && Skills.TryGetValue(card.Id,out var skill) && card.Text == skill.text) return skill.program;
            if (engineVersion >= 3 && card.Id=="wasp-00-闪耀之刃" && card.PrimaryCategory=="基础攻击" && string.IsNullOrEmpty(card.Subtype) &&
                card.Text=="选择与你相邻的一个英雄为目标。攻击后：取消与你相邻的敌方英雄技能卡上的激活效果。此回合：与你相邻的敌方英雄无法执行技能行动。") return ShiningBlade;
            return null;
        }
        public static DefenseProgramKind? Defense(CardDefinition card, int engineVersion) => card.PrimaryFamily == "defense" &&
            Defenses.TryGetValue(card.Id, out var binding) && card.Text == binding.text ? binding.kind : (DefenseProgramKind?)null;
    }
}
