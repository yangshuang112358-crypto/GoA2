# tigerclaw 的公开卡牌语义

[返回目录](README.md)

## tigerclaw-00 · 瞬闪打击

攻击前：沿直线移动2格且穿过一个敌方单位；选择该单位为目标。（如果你无法完成此移动，就不能攻击。）

**有序步骤**：RequiredStraightMoveThroughEnemy → Attack → End

| 参数 | 值 | 含义 |
|---|---|---|
| AdjacentAttack | True | 攻击是否限定相邻目标 |
| AttackBonusKind | None | 牌文攻击加成的条件类型 |
| AttackBonusValue | 0 | 该牌文攻击加成的参数值 |
| AttackSubtype | melee | 本卡攻击步骤的近战/远程类型 |
| ExcludeStraightLine | False | 攻击目标是否排除六角直线上的单位 |
| HeroTarget | None | 英雄目标选择条件；None表示无需另选英雄 |
| MinimumDistance | 1 | 攻击/主要防御适用的最小双方距离 |
| OnlyHeroes | False | 攻击目标是否只允许英雄 |
| RangeBonusKind | None | 牌文攻击距离加成的条件类型 |
| RangeBonusValue | 0 | 该牌文距离加成的参数值 |
| SupportMakesUnblockable | False | 满足友方支援条件是否使攻击无法抵挡 |
| TargetMoveKeepsDistance | False | 移动目标时是否保持与来源的距离 |
| TextMoveDistance | 2 | 牌文移动的距离参数；实际预算由对应步骤决定 |
| TextMoveMinimum | 2 | 需要按牌文最短距离筛选路线时的下限 |
| TextPushDistance | 0 | 牌文推动的距离参数 |

**规则约束**：attack_default_enemy_legal_target；next_turn_does_not_cross_round

## tigerclaw-02 · 偷袭

选择与你相邻的一个单位为目标。攻击后：你可以移动1格。

**有序步骤**：ChooseAttackTarget → Attack → OptionalTextMove → End

| 参数 | 值 | 含义 |
|---|---|---|
| AdjacentAttack | True | 攻击是否限定相邻目标 |
| AttackBonusKind | None | 牌文攻击加成的条件类型 |
| AttackBonusValue | 0 | 该牌文攻击加成的参数值 |
| AttackSubtype | melee | 本卡攻击步骤的近战/远程类型 |
| ExcludeStraightLine | False | 攻击目标是否排除六角直线上的单位 |
| HeroTarget | None | 英雄目标选择条件；None表示无需另选英雄 |
| MinimumDistance | 1 | 攻击/主要防御适用的最小双方距离 |
| OnlyHeroes | False | 攻击目标是否只允许英雄 |
| RangeBonusKind | None | 牌文攻击距离加成的条件类型 |
| RangeBonusValue | 0 | 该牌文距离加成的参数值 |
| SupportMakesUnblockable | False | 满足友方支援条件是否使攻击无法抵挡 |
| TargetMoveKeepsDistance | False | 移动目标时是否保持与来源的距离 |
| TextMoveDistance | 1 | 牌文移动的距离参数；实际预算由对应步骤决定 |
| TextMoveMinimum | 1 | 需要按牌文最短距离筛选路线时的下限 |
| TextPushDistance | 0 | 牌文推动的距离参数 |

**规则约束**：attack_default_enemy_legal_target；next_turn_does_not_cross_round

## tigerclaw-03 · 背刺

选择与你相邻的一个单位为目标。如果有友方单位与目标相邻，则+2攻击。（友方单位是指除你以外的另一个己方队伍的英雄或小兵）

**有序步骤**：ChooseAttackTarget → Attack → End

| 参数 | 值 | 含义 |
|---|---|---|
| AdjacentAttack | True | 攻击是否限定相邻目标 |
| AttackBonusKind | OtherFriendlySupport | 牌文攻击加成的条件类型 |
| AttackBonusValue | 2 | 该牌文攻击加成的参数值 |
| AttackSubtype | melee | 本卡攻击步骤的近战/远程类型 |
| ExcludeStraightLine | False | 攻击目标是否排除六角直线上的单位 |
| HeroTarget | None | 英雄目标选择条件；None表示无需另选英雄 |
| MinimumDistance | 1 | 攻击/主要防御适用的最小双方距离 |
| OnlyHeroes | False | 攻击目标是否只允许英雄 |
| RangeBonusKind | None | 牌文攻击距离加成的条件类型 |
| RangeBonusValue | 0 | 该牌文距离加成的参数值 |
| SupportMakesUnblockable | False | 满足友方支援条件是否使攻击无法抵挡 |
| TargetMoveKeepsDistance | False | 移动目标时是否保持与来源的距离 |
| TextMoveDistance | 0 | 牌文移动的距离参数；实际预算由对应步骤决定 |
| TextMoveMinimum | 0 | 需要按牌文最短距离筛选路线时的下限 |
| TextPushDistance | 0 | 牌文推动的距离参数 |

**规则约束**：attack_default_enemy_legal_target；next_turn_does_not_cross_round

## tigerclaw-04 · 影袭

攻击前：你可以移动1格。选择与你相邻的一个单位为目标。攻击后：如果你在攻击前没有移动，你可以移动1格。

**有序步骤**：OptionalPreAttackTextMove → ChooseAttackTarget → Attack → OptionalTextMoveIfNoPreMove → End

| 参数 | 值 | 含义 |
|---|---|---|
| AdjacentAttack | True | 攻击是否限定相邻目标 |
| AttackBonusKind | None | 牌文攻击加成的条件类型 |
| AttackBonusValue | 0 | 该牌文攻击加成的参数值 |
| AttackSubtype | melee | 本卡攻击步骤的近战/远程类型 |
| ExcludeStraightLine | False | 攻击目标是否排除六角直线上的单位 |
| HeroTarget | None | 英雄目标选择条件；None表示无需另选英雄 |
| MinimumDistance | 1 | 攻击/主要防御适用的最小双方距离 |
| OnlyHeroes | False | 攻击目标是否只允许英雄 |
| RangeBonusKind | None | 牌文攻击距离加成的条件类型 |
| RangeBonusValue | 0 | 该牌文距离加成的参数值 |
| SupportMakesUnblockable | False | 满足友方支援条件是否使攻击无法抵挡 |
| TargetMoveKeepsDistance | False | 移动目标时是否保持与来源的距离 |
| TextMoveDistance | 1 | 牌文移动的距离参数；实际预算由对应步骤决定 |
| TextMoveMinimum | 1 | 需要按牌文最短距离筛选路线时的下限 |
| TextPushDistance | 0 | 牌文推动的距离参数 |

**规则约束**：attack_default_enemy_legal_target；next_turn_does_not_cross_round

## tigerclaw-05 · 两面夹攻

选择攻击距离内的一个单位为目标。如果有友方单位与目标相邻，则+3攻击且此攻击无法被抵挡。（抵挡是一个关键词-目标英雄仍可以进行防御）

**有序步骤**：ChooseAttackTarget → Attack → End

| 参数 | 值 | 含义 |
|---|---|---|
| AdjacentAttack | False | 攻击是否限定相邻目标 |
| AttackBonusKind | OtherFriendlySupport | 牌文攻击加成的条件类型 |
| AttackBonusValue | 3 | 该牌文攻击加成的参数值 |
| AttackSubtype | ranged | 本卡攻击步骤的近战/远程类型 |
| ExcludeStraightLine | False | 攻击目标是否排除六角直线上的单位 |
| HeroTarget | None | 英雄目标选择条件；None表示无需另选英雄 |
| MinimumDistance | 1 | 攻击/主要防御适用的最小双方距离 |
| OnlyHeroes | False | 攻击目标是否只允许英雄 |
| RangeBonusKind | None | 牌文攻击距离加成的条件类型 |
| RangeBonusValue | 0 | 该牌文距离加成的参数值 |
| SupportMakesUnblockable | True | 满足友方支援条件是否使攻击无法抵挡 |
| TargetMoveKeepsDistance | False | 移动目标时是否保持与来源的距离 |
| TextMoveDistance | 0 | 牌文移动的距离参数；实际预算由对应步骤决定 |
| TextMoveMinimum | 0 | 需要按牌文最短距离筛选路线时的下限 |
| TextPushDistance | 0 | 牌文推动的距离参数 |

**规则约束**：attack_default_enemy_legal_target；next_turn_does_not_cross_round

## tigerclaw-06 · 暗影奇袭

攻击前：你可以移动1格。选择与你相邻的一个单位为目标。攻击后：你可以移动1格。

**有序步骤**：OptionalPreAttackTextMove → ChooseAttackTarget → Attack → OptionalTextMove → End

| 参数 | 值 | 含义 |
|---|---|---|
| AdjacentAttack | True | 攻击是否限定相邻目标 |
| AttackBonusKind | None | 牌文攻击加成的条件类型 |
| AttackBonusValue | 0 | 该牌文攻击加成的参数值 |
| AttackSubtype | melee | 本卡攻击步骤的近战/远程类型 |
| ExcludeStraightLine | False | 攻击目标是否排除六角直线上的单位 |
| HeroTarget | None | 英雄目标选择条件；None表示无需另选英雄 |
| MinimumDistance | 1 | 攻击/主要防御适用的最小双方距离 |
| OnlyHeroes | False | 攻击目标是否只允许英雄 |
| RangeBonusKind | None | 牌文攻击距离加成的条件类型 |
| RangeBonusValue | 0 | 该牌文距离加成的参数值 |
| SupportMakesUnblockable | False | 满足友方支援条件是否使攻击无法抵挡 |
| TargetMoveKeepsDistance | False | 移动目标时是否保持与来源的距离 |
| TextMoveDistance | 1 | 牌文移动的距离参数；实际预算由对应步骤决定 |
| TextMoveMinimum | 1 | 需要按牌文最短距离筛选路线时的下限 |
| TextPushDistance | 0 | 牌文推动的距离参数 |

**规则约束**：attack_default_enemy_legal_target；next_turn_does_not_cross_round

## tigerclaw-07 · 伺机待发

如果你与一个障碍物相邻，将自己放置到技能范围内不与敌方单位相邻的一格。若如此做，下回合：你获得免疫，且移动时可以穿过单位。

**有序步骤**：ChooseSelfPlacement → ApplyEffect → End

| 参数 | 值 | 含义 |
|---|---|---|
| AreaKind | None | 效果区域的计算方式，实际覆盖格由规则查询产生 |
| Duration | NextTurn | 持续期类型；具体起止轮次在效果实例中 |
| Effect | ImmunityAndUnitTraversal | 施加的持续效果类型，连接效果定义 |
| HeroTarget | None | 英雄目标选择条件；None表示无需另选英雄 |
| PlacementTarget | SafeInSkillRangeNearObstacle | 自身放置的目的格规则 |
| TargetMoveKeepsDistance | False | 移动目标时是否保持与来源的距离 |
| TextMoveDistance | 0 | 牌文移动的距离参数；实际预算由对应步骤决定 |
| TextMoveMinimum | 0 | 需要按牌文最短距离筛选路线时的下限 |
| TextPushDistance | 0 | 牌文推动的距离参数 |

**规则约束**：effect_source_recovery_or_defeat_cancels_unless_exception；next_turn_does_not_cross_round

**现有未裁定/未扩展边界**：U017_obstacle_scope_current_engine_not_fully_ratified

## tigerclaw-08 · 偷天妙手

移动最多2格，再从与你相邻的一个敌方英雄处拿取最多1枚金币。然后沿直线移动2格（如果可行）。

**有序步骤**：OptionalTextMove → OptionalGoldTransfer → RequiredStraightMoveIfAble → End

| 参数 | 值 | 含义 |
|---|---|---|
| GoldMaximum | 1 | 一次转移金币的上限，实际仍受目标金币数限制 |
| HeroTarget | None | 英雄目标选择条件；None表示无需另选英雄 |
| TargetMoveKeepsDistance | False | 移动目标时是否保持与来源的距离 |
| TextMoveDistance | 2 | 牌文移动的距离参数；实际预算由对应步骤决定 |
| TextMoveMinimum | 2 | 需要按牌文最短距离筛选路线时的下限 |
| TextPushDistance | 0 | 牌文推动的距离参数 |

**规则约束**：next_turn_does_not_cross_round

**现有未裁定/未扩展边界**：Q09_final_if_able_move_current_engine_requires_route_not_user_ratified

## tigerclaw-09 · 淬毒匕首

给攻击距离内一名英雄一枚中毒标记。拥有中毒标记的英雄每个先攻被动和攻击被动的数值减1，而不是增加1。

**有序步骤**：ChooseHeroTarget → ApplyPoison → End

| 参数 | 值 | 含义 |
|---|---|---|
| HeroTarget | AnyHeroInAttackRange | 英雄目标选择条件；None表示无需另选英雄 |
| TargetMoveKeepsDistance | False | 移动目标时是否保持与来源的距离 |
| TextMoveDistance | 0 | 牌文移动的距离参数；实际预算由对应步骤决定 |
| TextMoveMinimum | 0 | 需要按牌文最短距离筛选路线时的下限 |
| TextPushDistance | 0 | 牌文推动的距离参数 |

**规则约束**：poison_nonstacking_round_expiry_source_independent；next_turn_does_not_cross_round

## tigerclaw-10 · 探囊取物

移动最多2格，再从与你相邻的一个敌方英雄处拿取最多2枚金币。然后沿直线移动2格（如果可行）。

**有序步骤**：OptionalTextMove → OptionalGoldTransfer → RequiredStraightMoveIfAble → End

| 参数 | 值 | 含义 |
|---|---|---|
| GoldMaximum | 2 | 一次转移金币的上限，实际仍受目标金币数限制 |
| HeroTarget | None | 英雄目标选择条件；None表示无需另选英雄 |
| TargetMoveKeepsDistance | False | 移动目标时是否保持与来源的距离 |
| TextMoveDistance | 2 | 牌文移动的距离参数；实际预算由对应步骤决定 |
| TextMoveMinimum | 2 | 需要按牌文最短距离筛选路线时的下限 |
| TextPushDistance | 0 | 牌文推动的距离参数 |

**规则约束**：next_turn_does_not_cross_round

**现有未裁定/未扩展边界**：Q09_final_if_able_move_current_engine_requires_route_not_user_ratified

## tigerclaw-11 · 盗贼大师

移动最多2格，再从与你相邻的一个敌方英雄处拿取最多3枚金币。然后沿直线移动2格（如果可行）。

**有序步骤**：OptionalTextMove → OptionalGoldTransfer → RequiredStraightMoveIfAble → End

| 参数 | 值 | 含义 |
|---|---|---|
| GoldMaximum | 3 | 一次转移金币的上限，实际仍受目标金币数限制 |
| HeroTarget | None | 英雄目标选择条件；None表示无需另选英雄 |
| TargetMoveKeepsDistance | False | 移动目标时是否保持与来源的距离 |
| TextMoveDistance | 2 | 牌文移动的距离参数；实际预算由对应步骤决定 |
| TextMoveMinimum | 2 | 需要按牌文最短距离筛选路线时的下限 |
| TextPushDistance | 0 | 牌文推动的距离参数 |

**规则约束**：next_turn_does_not_cross_round

**现有未裁定/未扩展边界**：Q09_final_if_able_move_current_engine_requires_route_not_user_ratified

## tigerclaw-12 · 剧毒飞镖

给攻击距离内一名英雄一枚中毒标记。拥有中毒标记的英雄每个先攻被动、攻击被动和防御被动的数值减1，而不是增加1。

**有序步骤**：ChooseHeroTarget → ApplyDefensePoison → End

| 参数 | 值 | 含义 |
|---|---|---|
| HeroTarget | AnyHeroInAttackRange | 英雄目标选择条件；None表示无需另选英雄 |
| TargetMoveKeepsDistance | False | 移动目标时是否保持与来源的距离 |
| TextMoveDistance | 0 | 牌文移动的距离参数；实际预算由对应步骤决定 |
| TextMoveMinimum | 0 | 需要按牌文最短距离筛选路线时的下限 |
| TextPushDistance | 0 | 牌文推动的距离参数 |

**规则约束**：poison_nonstacking_round_expiry_source_independent；next_turn_does_not_cross_round

## tigerclaw-13 · 斗篷与匕首

如果你处于免疫：在你执行（或重复）任何行动前，移动最多2格；在你执行基础攻击后，你可以对不同目标重复一次基础攻击。

**有序步骤**：BeforeActionMoveAndRepeat

| 参数 | 值 | 含义 |
|---|---|---|
| BasicAttackBonus | 0 | 紫卡对基础攻击的常驻加成 |
| BasicAttackRangeBonus | 0 | 紫卡对基础攻击距离的常驻加成 |
| BattleMinionCount | 0 | 紫卡在兵战中计作的小兵数量 |
| TraverseObstacles | False | 紫卡是否允许穿越障碍，终点仍须合法 |
| Trigger | BeforeActionMoveAndRepeat | 紫卡触发时点和流程类别 |

**规则约束**：basic_attack_repeat_once_different_target_no_recursion；immune_prelude_move_up_to_two_no_future_route_precheck；prelude_includes_primary_secondary_fast_defense_excludes_internal_move；next_turn_does_not_cross_round

## tigerclaw-14 · 近身格挡

抵挡一次非远程攻击。攻击者丢弃一张卡牌（如果可行）。

**有序步骤**：PrimaryDefense

| 参数 | 值 | 含义 |
|---|---|---|
| AttackKind | NonRanged | 该主要防御适用的来袭攻击类型 |
| Block | True | 主要防御是否使用抵挡规则 |
| Followup | DiscardAttacker | 防御后的弃牌、击败或可选直线移动流程 |
| IgnoresMinions | False | 该主要防御是否忽略小兵计算 |
| MinimumDistance | 1 | 攻击/主要防御适用的最小双方距离 |
| PersistImmunityThroughDefeat | False | 该防御免疫是否跨过来源被击败保留 |
| ProtectFromOtherAttacks | False | 防御是否带对其他攻击的免疫 |
| ProtectFromOtherEnemies | False | 防御是否带对其他敌方行动的免疫 |
| RequiresAdjacentFriendlyMinion | False | 主要防御是否要求相邻友方小兵 |
| SwapAfterMove | False | 主要防御是否带后续换牌步骤 |
| TextMoveDistance | 0 | 牌文移动的距离参数；实际预算由对应步骤决定 |

**规则约束**：next_turn_does_not_cross_round

## tigerclaw-15 · 侧步

抵挡一次远程攻击。若如此做，你可以沿直线移动2格。

**有序步骤**：PrimaryDefense

| 参数 | 值 | 含义 |
|---|---|---|
| AttackKind | Ranged | 该主要防御适用的来袭攻击类型 |
| Block | True | 主要防御是否使用抵挡规则 |
| Followup | OptionalStraightMove | 防御后的弃牌、击败或可选直线移动流程 |
| IgnoresMinions | False | 该主要防御是否忽略小兵计算 |
| MinimumDistance | 1 | 攻击/主要防御适用的最小双方距离 |
| PersistImmunityThroughDefeat | False | 该防御免疫是否跨过来源被击败保留 |
| ProtectFromOtherAttacks | False | 防御是否带对其他攻击的免疫 |
| ProtectFromOtherEnemies | False | 防御是否带对其他敌方行动的免疫 |
| RequiresAdjacentFriendlyMinion | False | 主要防御是否要求相邻友方小兵 |
| SwapAfterMove | False | 主要防御是否带后续换牌步骤 |
| TextMoveDistance | 2 | 牌文移动的距离参数；实际预算由对应步骤决定 |

**规则约束**：next_turn_does_not_cross_round

## tigerclaw-16 · 暗影步

抵挡一次远程攻击。若如此做，你可以沿直线移动2格，且可以将此卡与你手中的一张卡牌交换。

**有序步骤**：PrimaryDefense

| 参数 | 值 | 含义 |
|---|---|---|
| AttackKind | Ranged | 该主要防御适用的来袭攻击类型 |
| Block | True | 主要防御是否使用抵挡规则 |
| Followup | OptionalStraightMove | 防御后的弃牌、击败或可选直线移动流程 |
| IgnoresMinions | False | 该主要防御是否忽略小兵计算 |
| MinimumDistance | 1 | 攻击/主要防御适用的最小双方距离 |
| PersistImmunityThroughDefeat | False | 该防御免疫是否跨过来源被击败保留 |
| ProtectFromOtherAttacks | False | 防御是否带对其他攻击的免疫 |
| ProtectFromOtherEnemies | False | 防御是否带对其他敌方行动的免疫 |
| RequiresAdjacentFriendlyMinion | False | 主要防御是否要求相邻友方小兵 |
| SwapAfterMove | True | 主要防御是否带后续换牌步骤 |
| TextMoveDistance | 2 | 牌文移动的距离参数；实际预算由对应步骤决定 |

**规则约束**：next_turn_does_not_cross_round

## tigerclaw-17 · 近身还击

抵挡一次非远程攻击。攻击者丢弃一张卡牌，否则被击败。

**有序步骤**：PrimaryDefense

| 参数 | 值 | 含义 |
|---|---|---|
| AttackKind | NonRanged | 该主要防御适用的来袭攻击类型 |
| Block | True | 主要防御是否使用抵挡规则 |
| Followup | DiscardAttackerOrDefeat | 防御后的弃牌、击败或可选直线移动流程 |
| IgnoresMinions | False | 该主要防御是否忽略小兵计算 |
| MinimumDistance | 1 | 攻击/主要防御适用的最小双方距离 |
| PersistImmunityThroughDefeat | False | 该防御免疫是否跨过来源被击败保留 |
| ProtectFromOtherAttacks | False | 防御是否带对其他攻击的免疫 |
| ProtectFromOtherEnemies | False | 防御是否带对其他敌方行动的免疫 |
| RequiresAdjacentFriendlyMinion | False | 主要防御是否要求相邻友方小兵 |
| SwapAfterMove | False | 主要防御是否带后续换牌步骤 |
| TextMoveDistance | 0 | 牌文移动的距离参数；实际预算由对应步骤决定 |

**规则约束**：next_turn_does_not_cross_round

## tigerclaw-18 · 躲闪

抵挡一次远程攻击

**有序步骤**：PrimaryDefense

| 参数 | 值 | 含义 |
|---|---|---|
| AttackKind | Ranged | 该主要防御适用的来袭攻击类型 |
| Block | True | 主要防御是否使用抵挡规则 |
| Followup | None | 防御后的弃牌、击败或可选直线移动流程 |
| IgnoresMinions | False | 该主要防御是否忽略小兵计算 |
| MinimumDistance | 1 | 攻击/主要防御适用的最小双方距离 |
| PersistImmunityThroughDefeat | False | 该防御免疫是否跨过来源被击败保留 |
| ProtectFromOtherAttacks | False | 防御是否带对其他攻击的免疫 |
| ProtectFromOtherEnemies | False | 防御是否带对其他敌方行动的免疫 |
| RequiresAdjacentFriendlyMinion | False | 主要防御是否要求相邻友方小兵 |
| SwapAfterMove | False | 主要防御是否带后续换牌步骤 |
| TextMoveDistance | 0 | 牌文移动的距离参数；实际预算由对应步骤决定 |

**规则约束**：next_turn_does_not_cross_round
