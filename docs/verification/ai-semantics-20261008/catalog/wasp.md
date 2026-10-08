# wasp 的公开卡牌语义

[返回目录](README.md)

## wasp-00 · 闪耀之刃

选择与你相邻的一个英雄为目标。攻击后：取消与你相邻的敌方英雄技能卡上的激活效果。此回合：与你相邻的敌方英雄无法执行技能行动。

**有序步骤**：ChooseAttackTarget → Attack → CancelAdjacentSkillEffects → ApplyEffect → End

| 参数 | 值 | 含义 |
|---|---|---|
| AdjacentAttack | True | 攻击是否限定相邻目标 |
| AreaKind | Adjacent | 效果区域的计算方式，实际覆盖格由规则查询产生 |
| AttackBonusKind | None | 牌文攻击加成的条件类型 |
| AttackBonusValue | 0 | 该牌文攻击加成的参数值 |
| AttackSubtype | melee | 本卡攻击步骤的近战/远程类型 |
| Duration | ThisTurn | 持续期类型；具体起止轮次在效果实例中 |
| Effect | SkillSuppression | 施加的持续效果类型，连接效果定义 |
| ExcludeStraightLine | False | 攻击目标是否排除六角直线上的单位 |
| HeroTarget | None | 英雄目标选择条件；None表示无需另选英雄 |
| MinimumDistance | 1 | 攻击/主要防御适用的最小双方距离 |
| OnlyHeroes | True | 攻击目标是否只允许英雄 |
| RangeBonusKind | None | 牌文攻击距离加成的条件类型 |
| RangeBonusValue | 0 | 该牌文距离加成的参数值 |
| SupportMakesUnblockable | False | 满足友方支援条件是否使攻击无法抵挡 |
| TargetMoveKeepsDistance | False | 移动目标时是否保持与来源的距离 |
| TextMoveDistance | 0 | 牌文移动的距离参数；实际预算由对应步骤决定 |
| TextMoveMinimum | 0 | 需要按牌文最短距离筛选路线时的下限 |
| TextPushDistance | 0 | 牌文推动的距离参数 |

**规则约束**：attack_default_enemy_legal_target；effect_source_recovery_or_defeat_cancels_unless_exception；next_turn_does_not_cross_round

## wasp-01 · 电击

选择与你相邻的一个单位为目标。攻击前：最多一个与你相邻的敌方英雄（除攻击目标外）丢弃一张牌（如果可能）。

**有序步骤**：ChooseAttackTarget → OptionalOtherHeroDiscard → Attack → End

| 参数 | 值 | 含义 |
|---|---|---|
| AdjacentAttack | True | 攻击是否限定相邻目标 |
| AttackBonusKind | None | 牌文攻击加成的条件类型 |
| AttackBonusValue | 0 | 该牌文攻击加成的参数值 |
| AttackSubtype | melee | 本卡攻击步骤的近战/远程类型 |
| ExcludeStraightLine | False | 攻击目标是否排除六角直线上的单位 |
| HeroTarget | OtherAdjacentEnemy | 英雄目标选择条件；None表示无需另选英雄 |
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

## wasp-02 · 回旋镖

选择攻击距离内与你不在同一直线上的一个单位为目标。（相邻单位也被视为在直线上）

**有序步骤**：ChooseAttackTarget → Attack → End

| 参数 | 值 | 含义 |
|---|---|---|
| AdjacentAttack | False | 攻击是否限定相邻目标 |
| AttackBonusKind | None | 牌文攻击加成的条件类型 |
| AttackBonusValue | 0 | 该牌文攻击加成的参数值 |
| AttackSubtype | ranged | 本卡攻击步骤的近战/远程类型 |
| ExcludeStraightLine | True | 攻击目标是否排除六角直线上的单位 |
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

## wasp-03 · 电能波

选择与你相邻的一个单位为目标。攻击前：技能范围内最多一个敌方英雄（除攻击目标外）丢弃一张牌（如果可能）。（虽然此卡有技能范围图标，但这不是远程攻击，选中的攻击目标必须与你相邻。）

**有序步骤**：ChooseAttackTarget → OptionalOtherHeroDiscard → Attack → End

| 参数 | 值 | 含义 |
|---|---|---|
| AdjacentAttack | True | 攻击是否限定相邻目标 |
| AttackBonusKind | None | 牌文攻击加成的条件类型 |
| AttackBonusValue | 0 | 该牌文攻击加成的参数值 |
| AttackSubtype | melee | 本卡攻击步骤的近战/远程类型 |
| ExcludeStraightLine | False | 攻击目标是否排除六角直线上的单位 |
| HeroTarget | OtherEnemyInSkillRange | 英雄目标选择条件；None表示无需另选英雄 |
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

## wasp-04 · 雷霆回旋镖

选择攻击距离内与你不在同一直线上的一个单位为目标。如果你击败一个敌方英雄，可以重复一次。

**有序步骤**：ChooseAttackTarget → Attack → OptionalRepeatAttackAfterHeroDefeat → End

| 参数 | 值 | 含义 |
|---|---|---|
| AdjacentAttack | False | 攻击是否限定相邻目标 |
| AttackBonusKind | None | 牌文攻击加成的条件类型 |
| AttackBonusValue | 0 | 该牌文攻击加成的参数值 |
| AttackSubtype | ranged | 本卡攻击步骤的近战/远程类型 |
| ExcludeStraightLine | True | 攻击目标是否排除六角直线上的单位 |
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

**规则约束**：attack_default_enemy_legal_target；repeat_serial_responses_returns_reactions_before_next；repeat_after_each_hero_defeat_without_fixed_count_limit；next_turn_does_not_cross_round

## wasp-05 · 电能爆炸

选择与你相邻的一个单位为目标。攻击前：技能范围内最多一名敌方英雄（除攻击目标外）丢弃一张卡牌，否则被击败。

**有序步骤**：ChooseAttackTarget → OptionalOtherHeroDiscardOrDefeat → Attack → End

| 参数 | 值 | 含义 |
|---|---|---|
| AdjacentAttack | True | 攻击是否限定相邻目标 |
| AttackBonusKind | None | 牌文攻击加成的条件类型 |
| AttackBonusValue | 0 | 该牌文攻击加成的参数值 |
| AttackSubtype | melee | 本卡攻击步骤的近战/远程类型 |
| ExcludeStraightLine | False | 攻击目标是否排除六角直线上的单位 |
| HeroTarget | OtherEnemyInSkillRange | 英雄目标选择条件；None表示无需另选英雄 |
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

## wasp-06 · 静电封锁

此回合：技能范围内的敌方单位无法移动或快速移动来脱离技能范围。技能范围外的敌方单位无法移动或快速移动进入技能范围。

**有序步骤**：ApplyEffect → End

| 参数 | 值 | 含义 |
|---|---|---|
| AreaKind | SkillRange | 效果区域的计算方式，实际覆盖格由规则查询产生 |
| Duration | ThisTurn | 持续期类型；具体起止轮次在效果实例中 |
| Effect | MovementBoundary | 施加的持续效果类型，连接效果定义 |
| HeroTarget | None | 英雄目标选择条件；None表示无需另选英雄 |
| TargetMoveKeepsDistance | False | 移动目标时是否保持与来源的距离 |
| TextMoveDistance | 0 | 牌文移动的距离参数；实际预算由对应步骤决定 |
| TextMoveMinimum | 0 | 需要按牌文最短距离筛选路线时的下限 |
| TextPushDistance | 0 | 牌文推动的距离参数 |

**规则约束**：effect_source_recovery_or_defeat_cancels_unless_exception；next_turn_does_not_cross_round

## wasp-07 · 抵挡屏障

如果攻击者不与你相邻，抵挡一次远程攻击。

**有序步骤**：PrimaryDefense

| 参数 | 值 | 含义 |
|---|---|---|
| AttackKind | Ranged | 该主要防御适用的来袭攻击类型 |
| Block | True | 主要防御是否使用抵挡规则 |
| Followup | None | 防御后的弃牌、击败或可选直线移动流程 |
| IgnoresMinions | False | 该主要防御是否忽略小兵计算 |
| MinimumDistance | 2 | 攻击/主要防御适用的最小双方距离 |
| PersistImmunityThroughDefeat | False | 该防御免疫是否跨过来源被击败保留 |
| ProtectFromOtherAttacks | False | 防御是否带对其他攻击的免疫 |
| ProtectFromOtherEnemies | False | 防御是否带对其他敌方行动的免疫 |
| RequiresAdjacentFriendlyMinion | False | 主要防御是否要求相邻友方小兵 |
| SwapAfterMove | False | 主要防御是否带后续换牌步骤 |
| TextMoveDistance | 0 | 牌文移动的距离参数；实际预算由对应步骤决定 |

**规则约束**：next_turn_does_not_cross_round

## wasp-08 · 偏转屏障

如果攻击者不与你相邻，抵挡一次远程攻击。若如此做，攻击者丢弃一张卡牌（如果可行）。

**有序步骤**：PrimaryDefense

| 参数 | 值 | 含义 |
|---|---|---|
| AttackKind | Ranged | 该主要防御适用的来袭攻击类型 |
| Block | True | 主要防御是否使用抵挡规则 |
| Followup | DiscardAttacker | 防御后的弃牌、击败或可选直线移动流程 |
| IgnoresMinions | False | 该主要防御是否忽略小兵计算 |
| MinimumDistance | 2 | 攻击/主要防御适用的最小双方距离 |
| PersistImmunityThroughDefeat | False | 该防御免疫是否跨过来源被击败保留 |
| ProtectFromOtherAttacks | False | 防御是否带对其他攻击的免疫 |
| ProtectFromOtherEnemies | False | 防御是否带对其他敌方行动的免疫 |
| RequiresAdjacentFriendlyMinion | False | 主要防御是否要求相邻友方小兵 |
| SwapAfterMove | False | 主要防御是否带后续换牌步骤 |
| TextMoveDistance | 0 | 牌文移动的距离参数；实际预算由对应步骤决定 |

**规则约束**：next_turn_does_not_cross_round

## wasp-09 · 意念操控

将攻击距离内与你不在同一直线上的一个单位或标志物放置到与你相邻的格子。

**有序步骤**：ChooseUnitPlacement → End

| 参数 | 值 | 含义 |
|---|---|---|
| HeroTarget | None | 英雄目标选择条件；None表示无需另选英雄 |
| TargetMoveKeepsDistance | False | 移动目标时是否保持与来源的距离 |
| TextMoveDistance | 0 | 牌文移动的距离参数；实际预算由对应步骤决定 |
| TextMoveMinimum | 0 | 需要按牌文最短距离筛选路线时的下限 |
| TextPushDistance | 0 | 牌文推动的距离参数 |

**规则约束**：next_turn_does_not_cross_round

**现有未裁定/未扩展边界**：D050_map_markers_not_in_current_six_hero_content

## wasp-10 · 反射屏障

如果攻击者不与你相邻，抵挡一次远程攻击。若如此做，攻击者丢弃一张卡牌（如果可行），此回合：你免疫不与你相邻的英雄的远程攻击。

**有序步骤**：PrimaryDefense

| 参数 | 值 | 含义 |
|---|---|---|
| AttackKind | Ranged | 该主要防御适用的来袭攻击类型 |
| Block | True | 主要防御是否使用抵挡规则 |
| Followup | DiscardAttackerThenImmunity | 防御后的弃牌、击败或可选直线移动流程 |
| IgnoresMinions | False | 该主要防御是否忽略小兵计算 |
| MinimumDistance | 2 | 攻击/主要防御适用的最小双方距离 |
| PersistImmunityThroughDefeat | False | 该防御免疫是否跨过来源被击败保留 |
| ProtectFromOtherAttacks | False | 防御是否带对其他攻击的免疫 |
| ProtectFromOtherEnemies | False | 防御是否带对其他敌方行动的免疫 |
| RequiresAdjacentFriendlyMinion | False | 主要防御是否要求相邻友方小兵 |
| SwapAfterMove | False | 主要防御是否带后续换牌步骤 |
| TextMoveDistance | 0 | 牌文移动的距离参数；实际预算由对应步骤决定 |

**规则约束**：next_turn_does_not_cross_round

## wasp-11 · 心灵控制

将攻击距离内与你不在同一直线上的一个单位或标志物放置到与你相邻的格子。可以重复一次。

**有序步骤**：ChooseUnitPlacement → OptionalRepeatUnitPlacement → End

| 参数 | 值 | 含义 |
|---|---|---|
| HeroTarget | None | 英雄目标选择条件；None表示无需另选英雄 |
| TargetMoveKeepsDistance | False | 移动目标时是否保持与来源的距离 |
| TextMoveDistance | 0 | 牌文移动的距离参数；实际预算由对应步骤决定 |
| TextMoveMinimum | 0 | 需要按牌文最短距离筛选路线时的下限 |
| TextPushDistance | 0 | 牌文推动的距离参数 |

**规则约束**：repeat_serial_responses_returns_reactions_before_next；next_turn_does_not_cross_round

**现有未裁定/未扩展边界**：D050_map_markers_not_in_current_six_hero_content

## wasp-12 · 电闪雷鸣

在你执行基础技能后，场上的一个敌方英雄丢弃一张卡牌（如果可行）。

**有序步骤**：AfterBasicSkillDiscard

| 参数 | 值 | 含义 |
|---|---|---|
| BasicAttackBonus | 0 | 紫卡对基础攻击的常驻加成 |
| BasicAttackRangeBonus | 0 | 紫卡对基础攻击距离的常驻加成 |
| BattleMinionCount | 0 | 紫卡在兵战中计作的小兵数量 |
| TraverseObstacles | False | 紫卡是否允许穿越障碍，终点仍须合法 |
| Trigger | AfterBasicSkillDiscard | 紫卡触发时点和流程类别 |

**规则约束**：next_turn_does_not_cross_round

## wasp-13 · 控物

移动技能范围内不与你相邻的一个单位或标志物最多1格，该移动不能使其远离或靠近你。

**有序步骤**：ChooseOrbitalTarget → OptionalTargetUnitMove → End

| 参数 | 值 | 含义 |
|---|---|---|
| HeroTarget | None | 英雄目标选择条件；None表示无需另选英雄 |
| TargetMoveKeepsDistance | True | 移动目标时是否保持与来源的距离 |
| TextMoveDistance | 1 | 牌文移动的距离参数；实际预算由对应步骤决定 |
| TextMoveMinimum | 1 | 需要按牌文最短距离筛选路线时的下限 |
| TextPushDistance | 0 | 牌文推动的距离参数 |

**规则约束**：orbital_excludes_self_nonadjacent_preserves_distance；next_turn_does_not_cross_round

**现有未裁定/未扩展边界**：D050_map_markers_not_in_current_six_hero_content

## wasp-14 · 动力助推

将所有与你相邻的敌方单位推动2格；每个被障碍物阻挡的敌方英雄丢弃一张牌（如果可能）。

**有序步骤**：PushAllAdjacentEnemies → DiscardBlockedPushHeroesIfAble → End

| 参数 | 值 | 含义 |
|---|---|---|
| HeroTarget | None | 英雄目标选择条件；None表示无需另选英雄 |
| TargetMoveKeepsDistance | False | 移动目标时是否保持与来源的距离 |
| TextMoveDistance | 0 | 牌文移动的距离参数；实际预算由对应步骤决定 |
| TextMoveMinimum | 0 | 需要按牌文最短距离筛选路线时的下限 |
| TextPushDistance | 2 | 牌文推动的距离参数 |

**规则约束**：next_turn_does_not_cross_round

## wasp-15 · 引力控制

移动技能范围内不与你相邻的一个单位或标志物最多1格，该移动不能使其远离或靠近你。可以重复1次。

**有序步骤**：ChooseOrbitalTarget → OptionalTargetUnitMove → OptionalRepeatOrbitalMove → OptionalTargetUnitMove → End

| 参数 | 值 | 含义 |
|---|---|---|
| HeroTarget | None | 英雄目标选择条件；None表示无需另选英雄 |
| TargetMoveKeepsDistance | True | 移动目标时是否保持与来源的距离 |
| TextMoveDistance | 1 | 牌文移动的距离参数；实际预算由对应步骤决定 |
| TextMoveMinimum | 1 | 需要按牌文最短距离筛选路线时的下限 |
| TextPushDistance | 0 | 牌文推动的距离参数 |

**规则约束**：orbital_excludes_self_nonadjacent_preserves_distance；repeat_serial_responses_returns_reactions_before_next；next_turn_does_not_cross_round

**现有未裁定/未扩展边界**：D050_map_markers_not_in_current_six_hero_content

## wasp-16 · 动能震爆

将所有与你相邻的敌方单位推动3格；每个被障碍物阻挡的敌方英雄丢弃一张牌（如果可行）。

**有序步骤**：PushAllAdjacentEnemies → DiscardBlockedPushHeroesIfAble → End

| 参数 | 值 | 含义 |
|---|---|---|
| HeroTarget | None | 英雄目标选择条件；None表示无需另选英雄 |
| TargetMoveKeepsDistance | False | 移动目标时是否保持与来源的距离 |
| TextMoveDistance | 0 | 牌文移动的距离参数；实际预算由对应步骤决定 |
| TextMoveMinimum | 0 | 需要按牌文最短距离筛选路线时的下限 |
| TextPushDistance | 3 | 牌文推动的距离参数 |

**规则约束**：next_turn_does_not_cross_round

## wasp-17 · 意念黑洞

移动技能范围内不与你相邻的一个单位或标志物最多1格，该移动不能使其远离或靠近你。可以重复最多2次。

**有序步骤**：ChooseOrbitalTarget → OptionalTargetUnitMove → OptionalRepeatOrbitalMove → OptionalTargetUnitMove → OptionalRepeatOrbitalMove → OptionalTargetUnitMove → End

| 参数 | 值 | 含义 |
|---|---|---|
| HeroTarget | None | 英雄目标选择条件；None表示无需另选英雄 |
| TargetMoveKeepsDistance | True | 移动目标时是否保持与来源的距离 |
| TextMoveDistance | 1 | 牌文移动的距离参数；实际预算由对应步骤决定 |
| TextMoveMinimum | 1 | 需要按牌文最短距离筛选路线时的下限 |
| TextPushDistance | 0 | 牌文推动的距离参数 |

**规则约束**：orbital_excludes_self_nonadjacent_preserves_distance；repeat_serial_responses_returns_reactions_before_next；next_turn_does_not_cross_round

**现有未裁定/未扩展边界**：D050_map_markers_not_in_current_six_hero_content
