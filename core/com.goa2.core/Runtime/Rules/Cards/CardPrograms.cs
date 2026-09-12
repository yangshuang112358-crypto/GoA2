#nullable enable
using System.Collections.Generic;
using Goa2.Domain;

namespace Goa2.Rules.Cards
{
    internal enum InstructionKind { ChooseAttackTarget, Attack, End, ApplyEffect, CancelAdjacentSkillEffects, ChooseOptionalDiscard, DetermineAttackRange, OptionalTextMove, OptionalRecoverDiscard, OptionalPreAttackTextMove, OptionalTextMoveIfNoPreMove, ChooseHeroTarget, OptionalRepeatAttackAfterHeroDefeat, OptionalMinionRemovalAfterDefeat, PushAttackTargetIfAdjacent, MoveIntoAttackTargetCell, RequiredStraightMoveToAttack }
    internal enum HeroTargetKind { None, AlliedNearEnemy }
    internal enum AttackBonusKind { None, TargetUsedAttack, AdjacentEnemies, OtherFriendlySupport }
    internal enum AttackRangeBonusKind { None, DiscardedBeforeAttack, OwnDiscardPile }
    internal enum DefenseFollowup { None, DiscardAttacker, DiscardAttackerThenImmunity, DiscardAttackerOrDefeat, OptionalStraightMove }
    internal enum DefenseAttackKind { Any, Ranged, NonRanged }
    internal sealed class DefenseProgram
    {
        public readonly string Id;
        public readonly int Version = 1, MinimumDistance, TextMoveDistance;
        public readonly bool Block, IgnoresMinions, RequiresAdjacentFriendlyMinion, SwapAfterMove;
        public readonly DefenseAttackKind AttackKind;
        public readonly DefenseFollowup Followup;
        public DefenseProgram(string id, bool block, int minimumDistance=1, DefenseFollowup followup=DefenseFollowup.None,
            DefenseAttackKind attackKind=DefenseAttackKind.Ranged, bool adjacentFriendlyMinion=false,int textMoveDistance=0,bool swapAfterMove=false)
        {
            Id=id; Block=block; AttackKind=block ? attackKind : DefenseAttackKind.Any; IgnoresMinions=!block;
            MinimumDistance=minimumDistance; Followup=followup; RequiresAdjacentFriendlyMinion=adjacentFriendlyMinion;
            TextMoveDistance=textMoveDistance;
            SwapAfterMove=swapAfterMove;
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
        public readonly int TextMoveDistance, TextMoveMinimum;
        public readonly bool RecoveryRequiresAdjacentMinion;
        public readonly HeroTargetKind HeroTarget;
        public readonly bool RecoverResolved;
        public readonly bool SupportMakesUnblockable;
        public readonly bool ExcludeStraightLine;
        public readonly bool ExtraRemovalUsesAttackRange;
        public readonly int TextPushDistance;
        public PrimaryProgram(string id, int minimumDistance, bool adjacent=false, bool onlyHeroes=false, EffectKind? effect=null,
            EffectAreaKind areaKind=EffectAreaKind.SkillRange, AttackBonusKind bonus=AttackBonusKind.None, int bonusValue=0,
            AttackRangeBonusKind rangeBonus=AttackRangeBonusKind.None,int rangeBonusValue=0,int textMoveDistance=0,bool recoveryRequiresAdjacentMinion=false,HeroTargetKind heroTarget=HeroTargetKind.None,bool recoverResolved=false,bool supportMakesUnblockable=false,bool excludeStraightLine=false,bool extraRemovalUsesAttackRange=false,int textPushDistance=0,int textMoveMinimum=0,params InstructionKind[] instructions)
        {
            Id = id; MinimumDistance = minimumDistance;
            AdjacentAttack=adjacent; OnlyHeroes=onlyHeroes; Effect=effect; AreaKind=areaKind; Duration=EffectDuration.ThisTurn;
            AttackBonusKind=bonus; AttackBonusValue=bonusValue;
            RangeBonusKind=rangeBonus; RangeBonusValue=rangeBonusValue;
            TextMoveDistance=textMoveDistance;
            TextMoveMinimum=textMoveMinimum>0 ? textMoveMinimum : textMoveDistance;
            RecoveryRequiresAdjacentMinion=recoveryRequiresAdjacentMinion;
            HeroTarget=heroTarget;
            RecoverResolved=recoverResolved;
            ExcludeStraightLine=excludeStraightLine;
            ExtraRemovalUsesAttackRange=extraRemovalUsesAttackRange;
            TextPushDistance=textPushDistance;
            SupportMakesUnblockable=supportMakesUnblockable;
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
            ["brogan-03-奋勇冲锋"] = ("攻击前：沿直线移动2或3格到与敌方单位相邻的位置，然后以该单位为目标。",27,
                new PrimaryProgram("required_straight_two_or_three_before_adjacent_attack",1,adjacent:true,textMoveMinimum:2,textMoveDistance:3,
                    instructions:new[]{InstructionKind.RequiredStraightMoveToAttack,InstructionKind.ChooseAttackTarget,InstructionKind.Attack,InstructionKind.End})),
            ["brogan-01-冲撞"] = ("攻击前：沿直线移动2格到与敌方单位相邻的位置，然后以该单位为目标。（如果你无法完成此移动，就不能攻击。）",26,
                new PrimaryProgram("required_straight_two_before_adjacent_attack",1,adjacent:true,textMoveDistance:2,
                    instructions:new[]{InstructionKind.RequiredStraightMoveToAttack,InstructionKind.ChooseAttackTarget,InstructionKind.Attack,InstructionKind.End})),
            ["brogan-00-猛攻"] = ("选择与你相邻的一个单位为目标。攻击后：移动到对方所在的位置。",23,
                new PrimaryProgram("adjacent_attack_move_into_target_cell",1,adjacent:true,textMoveDistance:1,
                    instructions:new[]{InstructionKind.ChooseAttackTarget,InstructionKind.Attack,InstructionKind.MoveIntoAttackTargetCell,InstructionKind.End})),
            ["sabina-00-近身射击"] = ("选择攻击距离内的一个单位为目标。攻击后：如果目标与你相邻，将目标推动1格。（单位被推动到障碍物上会停下，这算作一次有效的推动。）",22,
                new PrimaryProgram("ranged_attack_push_adjacent_surviving_target",1,textPushDistance:1,
                    instructions:new[]{InstructionKind.ChooseAttackTarget,InstructionKind.Attack,InstructionKind.PushAttackTargetIfAdjacent,InstructionKind.End})),
            ["sabina-04-枪林弹雨"] = ("选择攻击距离内的一个单位为目标。攻击后：如果此攻击击败了一个小兵，且攻击距离内没有敌方英雄，你可以移除攻击距离内的一个非重型敌方小兵。",21,
                new PrimaryProgram("ranged_attack_optional_ranged_minion_removal",1,extraRemovalUsesAttackRange:true,
                    instructions:new[]{InstructionKind.ChooseAttackTarget,InstructionKind.Attack,InstructionKind.OptionalMinionRemovalAfterDefeat,InstructionKind.End})),
            ["sabina-02-交叉火力"] = ("选择攻击距离内的一个单位为目标。攻击后：如果此攻击击败了一个小兵，且攻击距离内没有敌方英雄，你可以移除与你相邻的一个非重型敌方小兵。（你不会因移除小兵而获得金币。）",20,
                new PrimaryProgram("ranged_attack_optional_adjacent_minion_removal",1,
                    instructions:new[]{InstructionKind.ChooseAttackTarget,InstructionKind.Attack,InstructionKind.OptionalMinionRemovalAfterDefeat,InstructionKind.End})),
            ["wasp-04-雷霆回旋镖"] = ("选择攻击距离内与你不在同一直线上的一个单位为目标。如果你击败一个敌方英雄，可以重复一次。",19,
                new PrimaryProgram("ranged_off_line_repeat_after_hero_defeat",1,excludeStraightLine:true,
                    instructions:new[]{InstructionKind.ChooseAttackTarget,InstructionKind.Attack,InstructionKind.OptionalRepeatAttackAfterHeroDefeat,InstructionKind.End})),
            ["wasp-02-回旋镖"] = ("选择攻击距离内与你不在同一直线上的一个单位为目标。（相邻单位也被视为在直线上）",18,
                new PrimaryProgram("ranged_excluding_straight_line",1,excludeStraightLine:true)),
            ["tigerclaw-05-两面夹攻"] = ("选择攻击距离内的一个单位为目标。如果有友方单位与目标相邻，则+3攻击且此攻击无法被抵挡。（抵挡是一个关键词-目标英雄仍可以进行防御）",17,
                new PrimaryProgram("ranged_attack_supported_unblockable",1,bonus:AttackBonusKind.OtherFriendlySupport,bonusValue:3,supportMakesUnblockable:true)),
            ["tigerclaw-06-暗影奇袭"] = ("攻击前：你可以移动1格。选择与你相邻的一个单位为目标。攻击后：你可以移动1格。",14,
                new PrimaryProgram("adjacent_attack_move_before_and_after",1,adjacent:true,textMoveDistance:1,
                    instructions:new[]{InstructionKind.OptionalPreAttackTextMove,InstructionKind.ChooseAttackTarget,InstructionKind.Attack,InstructionKind.OptionalTextMove,InstructionKind.End})),
            ["tigerclaw-04-影袭"] = ("攻击前：你可以移动1格。选择与你相邻的一个单位为目标。攻击后：如果你在攻击前没有移动，你可以移动1格。",13,
                new PrimaryProgram("adjacent_attack_move_before_or_after",1,adjacent:true,textMoveDistance:1,
                    instructions:new[]{InstructionKind.OptionalPreAttackTextMove,InstructionKind.ChooseAttackTarget,InstructionKind.Attack,InstructionKind.OptionalTextMoveIfNoPreMove,InstructionKind.End})),
            ["tigerclaw-02-偷袭"] = ("选择与你相邻的一个单位为目标。攻击后：你可以移动1格。",11,
                new PrimaryProgram("adjacent_attack_optional_text_move",1,adjacent:true,textMoveDistance:1,
                    instructions:new[]{InstructionKind.ChooseAttackTarget,InstructionKind.Attack,InstructionKind.OptionalTextMove,InstructionKind.End})),
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
            ["tigerclaw-16-暗影步"] = ("抵挡一次远程攻击。若如此做，你可以沿直线移动2格，且可以将此卡与你手中的一张卡牌交换。",25,new DefenseProgram("block_ranged_optional_move_and_card_swap",true,followup:DefenseFollowup.OptionalStraightMove,textMoveDistance:2,swapAfterMove:true)),
            ["tigerclaw-15-侧步"] = ("抵挡一次远程攻击。若如此做，你可以沿直线移动2格。",24,new DefenseProgram("block_ranged_optional_straight_move",true,followup:DefenseFollowup.OptionalStraightMove,textMoveDistance:2)),
            ["wasp-07-抵挡屏障"] = ("如果攻击者不与你相邻，抵挡一次远程攻击。", 0, new DefenseProgram("block_non_adjacent_ranged",true,2)),
            ["tigerclaw-18-躲闪"] = ("抵挡一次远程攻击", 0, new DefenseProgram("block_ranged",true)),
            ["arien-13-挑战者"] = ("无视所有的小兵防御修正。", 0, new DefenseProgram("numeric_ignore_minions",false)),
            ["wasp-08-偏转屏障"] = ("如果攻击者不与你相邻，抵挡一次远程攻击。若如此做，攻击者丢弃一张卡牌（如果可行）。", 4, new DefenseProgram("block_ranged_discard_attacker",true,2,DefenseFollowup.DiscardAttacker)),
            ["wasp-10-反射屏障"] = ("如果攻击者不与你相邻，抵挡一次远程攻击。若如此做，攻击者丢弃一张卡牌（如果可行），此回合：你免疫不与你相邻的英雄的远程攻击。", 4, new DefenseProgram("block_ranged_discard_attacker_then_immunity",true,2,DefenseFollowup.DiscardAttackerThenImmunity)),
            ["tigerclaw-14-近身格挡"] = ("抵挡一次非远程攻击。攻击者丢弃一张卡牌（如果可行）。",7,new DefenseProgram("block_non_ranged_discard_attacker",true,followup:DefenseFollowup.DiscardAttacker,attackKind:DefenseAttackKind.NonRanged)),
            ["tigerclaw-17-近身还击"] = ("抵挡一次非远程攻击。攻击者丢弃一张卡牌，否则被击败。",7,new DefenseProgram("block_non_ranged_discard_or_defeat_attacker",true,followup:DefenseFollowup.DiscardAttackerOrDefeat,attackKind:DefenseAttackKind.NonRanged)),
            ["sabina-08-带头冲锋"] = ("如果你与一个友方小兵相邻，抵挡此次攻击。",7,new DefenseProgram("block_with_adjacent_friendly_minion",true,attackKind:DefenseAttackKind.Any,adjacentFriendlyMinion:true))
        };
        private static readonly Dictionary<string,(string text, int minimumEngine, string? subtype, PrimaryProgram program)> Skills = new Dictionary<string,(string, int, string?, PrimaryProgram)>
        {
            ["brogan-10-吟游诗人"] = ("如果你或攻击距离内的一个友方英雄与敌方单位相邻，则该友方英雄可以拿回一张已结算或已丢弃的卡牌。",16,"远程",
                new PrimaryProgram("ally_near_enemy_optional_resolved_or_discard_recovery",0,heroTarget:HeroTargetKind.AlliedNearEnemy,recoverResolved:true,
                    instructions:new[]{InstructionKind.ChooseHeroTarget,InstructionKind.OptionalRecoverDiscard,InstructionKind.End})),
            ["brogan-08-战鼓"] = ("如果你或攻击距离内的一个友方英雄与敌方单位相邻，则该友方英雄可以拿回一张已丢弃的卡牌。",15,"远程",
                new PrimaryProgram("ally_near_enemy_optional_discard_recovery",0,heroTarget:HeroTargetKind.AlliedNearEnemy,
                    instructions:new[]{InstructionKind.ChooseHeroTarget,InstructionKind.OptionalRecoverDiscard,InstructionKind.End})),
            ["wasp-06-静电封锁"] = ("此回合：技能范围内的敌方单位无法移动或快速移动来脱离技能范围。技能范围外的敌方单位无法移动或快速移动进入技能范围。",2,"范围",new PrimaryProgram("movement_boundary_aura",EffectKind.MovementBoundary,EffectDuration.ThisTurn)),
            ["arien-06-打断施法"] = ("此回合：在技能范围内的敌方英雄不能执行技能。（打断施法不会阻止攻击行动。）",2,"范围",new PrimaryProgram("skill_suppression_aura",EffectKind.SkillSuppression,EffectDuration.ThisTurn)),
            ["shargatha-15-忠实信徒"] = ("如果你与一个小兵相邻，你可以拿回一张已丢弃的卡牌。",12,null,new PrimaryProgram("optional_discard_recovery_near_minion",0,recoveryRequiresAdjacentMinion:true,
                instructions:new[]{InstructionKind.OptionalRecoverDiscard,InstructionKind.End}))
        };
        public static PrimaryProgram? Primary(CardDefinition card, int engineVersion)
        {
            if (card.PrimaryFamily == "attack" && Attacks.TryGetValue(card.Id,out var attack) && card.Text == attack.text && engineVersion>=attack.minimumEngine &&
                (attack.program.AdjacentAttack ? string.IsNullOrEmpty(card.Subtype) : card.Subtype=="远程")) return attack.program;
            if (card.PrimaryFamily == "skill" && Skills.TryGetValue(card.Id,out var skill) && engineVersion>=skill.minimumEngine && card.Subtype==skill.subtype && card.Text==skill.text) return skill.program;
            if (engineVersion >= 3 && card.Id=="wasp-00-闪耀之刃" && card.PrimaryCategory=="基础攻击" && string.IsNullOrEmpty(card.Subtype) &&
                card.Text=="选择与你相邻的一个英雄为目标。攻击后：取消与你相邻的敌方英雄技能卡上的激活效果。此回合：与你相邻的敌方英雄无法执行技能行动。") return ShiningBlade;
            return null;
        }
        public static DefenseProgram? Defense(CardDefinition card, int engineVersion) => card.PrimaryFamily == "defense" &&
            Defenses.TryGetValue(card.Id, out var binding) && card.Text == binding.text && engineVersion >= binding.minimumEngine ? binding.program : null;
    }
}
