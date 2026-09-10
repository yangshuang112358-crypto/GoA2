#nullable enable
using System.Collections.Generic;
using Goa2.Domain;

namespace Goa2.Rules.Cards
{
    internal enum InstructionKind { ChooseAttackTarget, Attack, End, ApplyEffect, CancelAdjacentSkillEffects, ChooseOptionalDiscard, DetermineAttackRange }
    internal enum AttackBonusKind { None, TargetUsedAttack, AdjacentEnemies, OtherFriendlySupport }
    internal enum AttackRangeBonusKind { None, DiscardedBeforeAttack, OwnDiscardPile }
    internal enum DefenseFollowup { None, DiscardAttacker, DiscardAttackerThenImmunity, DiscardAttackerOrDefeat }
    internal enum DefenseAttackKind { Any, Ranged, NonRanged }
    internal sealed class DefenseProgram
    {
        public readonly string Id;
        public readonly int Version = 1, MinimumDistance;
        public readonly bool Block, IgnoresMinions, RequiresAdjacentFriendlyMinion;
        public readonly DefenseAttackKind AttackKind;
        public readonly DefenseFollowup Followup;
        public DefenseProgram(string id, bool block, int minimumDistance=1, DefenseFollowup followup=DefenseFollowup.None,
            DefenseAttackKind attackKind=DefenseAttackKind.Ranged, bool adjacentFriendlyMinion=false)
        {
            Id=id; Block=block; AttackKind=block ? attackKind : DefenseAttackKind.Any; IgnoresMinions=!block;
            MinimumDistance=minimumDistance; Followup=followup; RequiresAdjacentFriendlyMinion=adjacentFriendlyMinion;
        }
    }
    internal sealed class PrimaryProgram
    {
        public readonly string Id;
        public readonly int Version = 1, MinimumDistance;
        public readonly IReadOnlyList<InstructionKind> Instructions;
        public readonly EffectKind? Effect;
        public readonly EffectDuration Duration;
        public readonly EffectAreaKind AreaKind;
        public readonly bool AdjacentAttack, OnlyHeroes;
        public readonly AttackBonusKind AttackBonusKind;
        public readonly int AttackBonusValue;
        public readonly AttackRangeBonusKind RangeBonusKind;
        public readonly int RangeBonusValue;
        public PrimaryProgram(string id, int minimumDistance, bool adjacent=false, bool onlyHeroes=false, EffectKind? effect=null,
            EffectAreaKind areaKind=EffectAreaKind.SkillRange, AttackBonusKind bonus=AttackBonusKind.None, int bonusValue=0,
            AttackRangeBonusKind rangeBonus=AttackRangeBonusKind.None,int rangeBonusValue=0,params InstructionKind[] instructions)
        {
            Id = id; MinimumDistance = minimumDistance;
            AdjacentAttack=adjacent; OnlyHeroes=onlyHeroes; Effect=effect; AreaKind=areaKind; Duration=EffectDuration.ThisTurn;
            AttackBonusKind=bonus; AttackBonusValue=bonusValue;
            RangeBonusKind=rangeBonus; RangeBonusValue=rangeBonusValue;
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
        private static PrimaryProgram OptionalDiscardAttack(string id,AttackRangeBonusKind bonus) => new PrimaryProgram(id,1,
            rangeBonus:bonus,rangeBonusValue:2,instructions:new[] {InstructionKind.ChooseOptionalDiscard,InstructionKind.DetermineAttackRange,InstructionKind.ChooseAttackTarget,InstructionKind.Attack,InstructionKind.End});
        private static readonly PrimaryProgram NonAdjacentRanged = new PrimaryProgram("non_adjacent_ranged_attack", 2);
        private static readonly PrimaryProgram ShiningBlade = new PrimaryProgram("adjacent_hero_attack_cancel_skills",1,
            adjacent:true,onlyHeroes:true,effect:EffectKind.SkillSuppression,areaKind:EffectAreaKind.Adjacent,
            instructions:new[] {InstructionKind.ChooseAttackTarget,InstructionKind.Attack,InstructionKind.CancelAdjacentSkillEffects,InstructionKind.ApplyEffect,InstructionKind.End});
        // Binding IDs is confined to this registry. Shared execution never branches on a card ID.
        private static readonly Dictionary<string,(string text,int minimumEngine,PrimaryProgram program)> Attacks = new Dictionary<string,(string,int,PrimaryProgram)>
        {
            ["brogan-02-投掷飞斧"] = ("攻击前：你可以丢弃一张卡牌。若如此做，则+2攻击距离。选择攻击距离内的一个单位为目标。",8,OptionalDiscardAttack("ranged_after_optional_own_discard",AttackRangeBonusKind.DiscardedBeforeAttack)),
            ["brogan-04-投掷长矛"] = ("攻击前：你可以丢弃一张卡牌。如果你的弃牌堆中有卡牌，则+2攻击距离。选择攻击距离内的一个单位为目标。",8,OptionalDiscardAttack("ranged_with_own_discard_pile",AttackRangeBonusKind.OwnDiscardPile)),
            ["sabina-01-拔枪"] = ("选择攻击距离内且不与你相邻的一个单位为目标。",0,NonAdjacentRanged),
            ["shargatha-02-快速突刺"] = ("选择攻击距离内且与你不相邻的一个单位为目标。",0,NonAdjacentRanged),
            ["sabina-03-神枪手"] = ("选择攻击距离内且不与你相邻的一个单位为目标。如果目标英雄在此回合使用了攻击卡牌，则+2攻击。（已揭示卡视为使用，而非已结算）",5,new PrimaryProgram("non_adjacent_ranged_vs_revealed_attack",2,bonus:AttackBonusKind.TargetUsedAttack,bonusValue:2)),
            ["sabina-05-一枪爆头"] = ("选择攻击距离内的一个单位为目标。如果目标英雄在此回合使用了攻击卡牌，则+2攻击。",5,new PrimaryProgram("ranged_vs_revealed_attack",1,bonus:AttackBonusKind.TargetUsedAttack,bonusValue:2)),
            ["shargatha-01-劈砍"] = ("选择与你相邻的一个单位为目标。每有一个与你相邻的敌方单位，+1攻击。（计算所有敌方单位，包括攻击目标。）",6,new PrimaryProgram("adjacent_attack_enemy_count_1",1,adjacent:true,bonus:AttackBonusKind.AdjacentEnemies,bonusValue:1)),
            ["shargatha-03-致命横扫"] = ("选择与你相邻的一个单位为目标。每有一个与你相邻的敌方单位，+2攻击。",6,new PrimaryProgram("adjacent_attack_enemy_count_2",1,adjacent:true,bonus:AttackBonusKind.AdjacentEnemies,bonusValue:2)),
            ["shargatha-05-死亡回旋"] = ("选择与你相邻的一个单位为目标。每有一个与你相邻的敌方单位，+3攻击。",6,new PrimaryProgram("adjacent_attack_enemy_count_3",1,adjacent:true,bonus:AttackBonusKind.AdjacentEnemies,bonusValue:3)),
            ["tigerclaw-03-背刺"] = ("选择与你相邻的一个单位为目标。如果有友方单位与目标相邻，则+2攻击。（友方单位是指除你以外的另一个己方队伍的英雄或小兵）",6,new PrimaryProgram("adjacent_attack_other_friendly_support",1,adjacent:true,bonus:AttackBonusKind.OtherFriendlySupport,bonusValue:2))
        };
        private static readonly Dictionary<string,(string text, int minimumEngine, DefenseProgram program)> Defenses = new Dictionary<string,(string, int, DefenseProgram)>
        {
            ["wasp-07-抵挡屏障"] = ("如果攻击者不与你相邻，抵挡一次远程攻击。", 0, new DefenseProgram("block_non_adjacent_ranged",true,2)),
            ["tigerclaw-18-躲闪"] = ("抵挡一次远程攻击", 0, new DefenseProgram("block_ranged",true)),
            ["arien-13-挑战者"] = ("无视所有的小兵防御修正。", 0, new DefenseProgram("numeric_ignore_minions",false)),
            ["wasp-08-偏转屏障"] = ("如果攻击者不与你相邻，抵挡一次远程攻击。若如此做，攻击者丢弃一张卡牌（如果可行）。", 4, new DefenseProgram("block_ranged_discard_attacker",true,2,DefenseFollowup.DiscardAttacker)),
            ["wasp-10-反射屏障"] = ("如果攻击者不与你相邻，抵挡一次远程攻击。若如此做，攻击者丢弃一张卡牌（如果可行），此回合：你免疫不与你相邻的英雄的远程攻击。", 4, new DefenseProgram("block_ranged_discard_attacker_then_immunity",true,2,DefenseFollowup.DiscardAttackerThenImmunity)),
            ["tigerclaw-14-近身格挡"] = ("抵挡一次非远程攻击。攻击者丢弃一张卡牌（如果可行）。",7,new DefenseProgram("block_non_ranged_discard_attacker",true,followup:DefenseFollowup.DiscardAttacker,attackKind:DefenseAttackKind.NonRanged)),
            ["tigerclaw-17-近身还击"] = ("抵挡一次非远程攻击。攻击者丢弃一张卡牌，否则被击败。",7,new DefenseProgram("block_non_ranged_discard_or_defeat_attacker",true,followup:DefenseFollowup.DiscardAttackerOrDefeat,attackKind:DefenseAttackKind.NonRanged)),
            ["sabina-08-带头冲锋"] = ("如果你与一个友方小兵相邻，抵挡此次攻击。",7,new DefenseProgram("block_with_adjacent_friendly_minion",true,attackKind:DefenseAttackKind.Any,adjacentFriendlyMinion:true))
        };
        private static readonly Dictionary<string,(string text, PrimaryProgram program)> Skills = new Dictionary<string,(string, PrimaryProgram)>
        {
            ["wasp-06-静电封锁"] = ("此回合：技能范围内的敌方单位无法移动或快速移动来脱离技能范围。技能范围外的敌方单位无法移动或快速移动进入技能范围。", new PrimaryProgram("movement_boundary_aura",EffectKind.MovementBoundary,EffectDuration.ThisTurn)),
            ["arien-06-打断施法"] = ("此回合：在技能范围内的敌方英雄不能执行技能。（打断施法不会阻止攻击行动。）", new PrimaryProgram("skill_suppression_aura",EffectKind.SkillSuppression,EffectDuration.ThisTurn))
        };
        public static PrimaryProgram? Primary(CardDefinition card, int engineVersion)
        {
            if (card.PrimaryFamily == "attack" && Attacks.TryGetValue(card.Id,out var attack) && card.Text == attack.text && engineVersion>=attack.minimumEngine &&
                (attack.program.AdjacentAttack ? string.IsNullOrEmpty(card.Subtype) : card.Subtype=="远程")) return attack.program;
            if (engineVersion >= 2 && card.PrimaryFamily == "skill" && card.Subtype == "范围" && Skills.TryGetValue(card.Id,out var skill) && card.Text == skill.text) return skill.program;
            if (engineVersion >= 3 && card.Id=="wasp-00-闪耀之刃" && card.PrimaryCategory=="基础攻击" && string.IsNullOrEmpty(card.Subtype) &&
                card.Text=="选择与你相邻的一个英雄为目标。攻击后：取消与你相邻的敌方英雄技能卡上的激活效果。此回合：与你相邻的敌方英雄无法执行技能行动。") return ShiningBlade;
            return null;
        }
        public static DefenseProgram? Defense(CardDefinition card, int engineVersion) => card.PrimaryFamily == "defense" &&
            Defenses.TryGetValue(card.Id, out var binding) && card.Text == binding.text && engineVersion >= binding.minimumEngine ? binding.program : null;
    }
}
