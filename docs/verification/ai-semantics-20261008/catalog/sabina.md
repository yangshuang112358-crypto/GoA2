# sabina 的公开卡牌语义

[返回目录](README.md)

## sabina-00 · 近身射击

选择攻击距离内的一个单位为目标。攻击后：如果目标与你相邻，将目标推动1格。（单位被推动到障碍物上会停下，这算作一次有效的推动。）

**有序步骤**：ChooseAttackTarget → Attack → PushAttackTargetIfAdjacent → End

| 参数 | 值 | 含义 |
|---|---|---|
| AdjacentAttack | False | 攻击是否限定相邻目标 |
| AttackBonusKind | None | 牌文攻击加成的条件类型 |
| AttackBonusValue | 0 | 该牌文攻击加成的参数值 |
| AttackSubtype | ranged | 本卡攻击步骤的近战/远程类型 |
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
| TextPushDistance | 1 | 牌文推动的距离参数 |

**规则约束**：attack_default_enemy_legal_target；next_turn_does_not_cross_round

## sabina-01 · 拔枪

选择攻击距离内且不与你相邻的一个单位为目标。

**有序步骤**：ChooseAttackTarget → Attack → End

| 参数 | 值 | 含义 |
|---|---|---|
| AdjacentAttack | False | 攻击是否限定相邻目标 |
| AttackBonusKind | None | 牌文攻击加成的条件类型 |
| AttackBonusValue | 0 | 该牌文攻击加成的参数值 |
| AttackSubtype | ranged | 本卡攻击步骤的近战/远程类型 |
| ExcludeStraightLine | False | 攻击目标是否排除六角直线上的单位 |
| HeroTarget | None | 英雄目标选择条件；None表示无需另选英雄 |
| MinimumDistance | 2 | 攻击/主要防御适用的最小双方距离 |
| OnlyHeroes | False | 攻击目标是否只允许英雄 |
| RangeBonusKind | None | 牌文攻击距离加成的条件类型 |
| RangeBonusValue | 0 | 该牌文距离加成的参数值 |
| SupportMakesUnblockable | False | 满足友方支援条件是否使攻击无法抵挡 |
| TargetMoveKeepsDistance | False | 移动目标时是否保持与来源的距离 |
| TextMoveDistance | 0 | 牌文移动的距离参数；实际预算由对应步骤决定 |
| TextMoveMinimum | 0 | 需要按牌文最短距离筛选路线时的下限 |
| TextPushDistance | 0 | 牌文推动的距离参数 |

**规则约束**：attack_default_enemy_legal_target；next_turn_does_not_cross_round

## sabina-02 · 交叉火力

选择攻击距离内的一个单位为目标。攻击后：如果此攻击击败了一个小兵，且攻击距离内没有敌方英雄，你可以移除与你相邻的一个非重型敌方小兵。（你不会因移除小兵而获得金币。）

**有序步骤**：ChooseAttackTarget → Attack → OptionalMinionRemovalAfterDefeat → End

| 参数 | 值 | 含义 |
|---|---|---|
| AdjacentAttack | False | 攻击是否限定相邻目标 |
| AttackBonusKind | None | 牌文攻击加成的条件类型 |
| AttackBonusValue | 0 | 该牌文攻击加成的参数值 |
| AttackSubtype | ranged | 本卡攻击步骤的近战/远程类型 |
| ExcludeStraightLine | False | 攻击目标是否排除六角直线上的单位 |
| ExtraRemovalUsesAttackRange | False | 追加小兵移除使用攻击距离还是相邻范围 |
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

## sabina-03 · 神枪手

选择攻击距离内且不与你相邻的一个单位为目标。如果目标英雄在此回合使用了攻击卡牌，则+2攻击。（已揭示卡视为使用，而非已结算）

**有序步骤**：ChooseAttackTarget → Attack → End

| 参数 | 值 | 含义 |
|---|---|---|
| AdjacentAttack | False | 攻击是否限定相邻目标 |
| AttackBonusKind | TargetUsedAttack | 牌文攻击加成的条件类型 |
| AttackBonusValue | 2 | 该牌文攻击加成的参数值 |
| AttackSubtype | ranged | 本卡攻击步骤的近战/远程类型 |
| ExcludeStraightLine | False | 攻击目标是否排除六角直线上的单位 |
| HeroTarget | None | 英雄目标选择条件；None表示无需另选英雄 |
| MinimumDistance | 2 | 攻击/主要防御适用的最小双方距离 |
| OnlyHeroes | False | 攻击目标是否只允许英雄 |
| RangeBonusKind | None | 牌文攻击距离加成的条件类型 |
| RangeBonusValue | 0 | 该牌文距离加成的参数值 |
| SupportMakesUnblockable | False | 满足友方支援条件是否使攻击无法抵挡 |
| TargetMoveKeepsDistance | False | 移动目标时是否保持与来源的距离 |
| TextMoveDistance | 0 | 牌文移动的距离参数；实际预算由对应步骤决定 |
| TextMoveMinimum | 0 | 需要按牌文最短距离筛选路线时的下限 |
| TextPushDistance | 0 | 牌文推动的距离参数 |

**规则约束**：attack_default_enemy_legal_target；next_turn_does_not_cross_round

## sabina-04 · 枪林弹雨

选择攻击距离内的一个单位为目标。攻击后：如果此攻击击败了一个小兵，且攻击距离内没有敌方英雄，你可以移除攻击距离内的一个非重型敌方小兵。

**有序步骤**：ChooseAttackTarget → Attack → OptionalMinionRemovalAfterDefeat → End

| 参数 | 值 | 含义 |
|---|---|---|
| AdjacentAttack | False | 攻击是否限定相邻目标 |
| AttackBonusKind | None | 牌文攻击加成的条件类型 |
| AttackBonusValue | 0 | 该牌文攻击加成的参数值 |
| AttackSubtype | ranged | 本卡攻击步骤的近战/远程类型 |
| ExcludeStraightLine | False | 攻击目标是否排除六角直线上的单位 |
| ExtraRemovalUsesAttackRange | True | 追加小兵移除使用攻击距离还是相邻范围 |
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

## sabina-05 · 一枪爆头

选择攻击距离内的一个单位为目标。如果目标英雄在此回合使用了攻击卡牌，则+2攻击。

**有序步骤**：ChooseAttackTarget → Attack → End

| 参数 | 值 | 含义 |
|---|---|---|
| AdjacentAttack | False | 攻击是否限定相邻目标 |
| AttackBonusKind | TargetUsedAttack | 牌文攻击加成的条件类型 |
| AttackBonusValue | 2 | 该牌文攻击加成的参数值 |
| AttackSubtype | ranged | 本卡攻击步骤的近战/远程类型 |
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

## sabina-06 · 并肩作战

你可以和与你相邻的一个友方小兵换位。此回合：你和技能范围内的友方英雄如果与一个或多个友方小兵相邻，则+1防御。

**有序步骤**：ChooseOptionalUnitSwapTarget → SwapTargetUnits → ApplyEffect → End

| 参数 | 值 | 含义 |
|---|---|---|
| AreaKind | SkillRange | 效果区域的计算方式，实际覆盖格由规则查询产生 |
| Duration | ThisTurn | 持续期类型；具体起止轮次在效果实例中 |
| Effect | FriendlyNearMinionDefense | 施加的持续效果类型，连接效果定义 |
| HeroTarget | None | 英雄目标选择条件；None表示无需另选英雄 |
| TargetMoveKeepsDistance | False | 移动目标时是否保持与来源的距离 |
| TextMoveDistance | 0 | 牌文移动的距离参数；实际预算由对应步骤决定 |
| TextMoveMinimum | 0 | 需要按牌文最短距离筛选路线时的下限 |
| TextPushDistance | 0 | 牌文推动的距离参数 |
| UnitSwapTarget | AdjacentFriendlyMinion | 换位目标的阵营、单位类型和范围条件 |

**规则约束**：effect_source_recovery_or_defeat_cancels_unless_exception；next_turn_does_not_cross_round

## sabina-07 · 指挥

将技能范围内的任意1个友方小兵移动最多2格；无视重型小兵免疫。

**有序步骤**：ChooseFriendlyMinionTarget → OptionalTargetUnitMove → End

| 参数 | 值 | 含义 |
|---|---|---|
| HeroTarget | None | 英雄目标选择条件；None表示无需另选英雄 |
| IgnoreHeavyImmunity | True | 该小兵选择步骤是否忽略重型小兵免疫 |
| TargetMoveKeepsDistance | False | 移动目标时是否保持与来源的距离 |
| TextMoveDistance | 2 | 牌文移动的距离参数；实际预算由对应步骤决定 |
| TextMoveMinimum | 2 | 需要按牌文最短距离筛选路线时的下限 |
| TextPushDistance | 0 | 牌文推动的距离参数 |

**规则约束**：next_turn_does_not_cross_round

## sabina-08 · 带头冲锋

如果你与一个友方小兵相邻，抵挡此次攻击。

**有序步骤**：PrimaryDefense

| 参数 | 值 | 含义 |
|---|---|---|
| AttackKind | Any | 该主要防御适用的来袭攻击类型 |
| Block | True | 主要防御是否使用抵挡规则 |
| Followup | None | 防御后的弃牌、击败或可选直线移动流程 |
| IgnoresMinions | False | 该主要防御是否忽略小兵计算 |
| MinimumDistance | 1 | 攻击/主要防御适用的最小双方距离 |
| PersistImmunityThroughDefeat | False | 该防御免疫是否跨过来源被击败保留 |
| ProtectFromOtherAttacks | False | 防御是否带对其他攻击的免疫 |
| ProtectFromOtherEnemies | False | 防御是否带对其他敌方行动的免疫 |
| RequiresAdjacentFriendlyMinion | True | 主要防御是否要求相邻友方小兵 |
| SwapAfterMove | False | 主要防御是否带后续换牌步骤 |
| TextMoveDistance | 0 | 牌文移动的距离参数；实际预算由对应步骤决定 |

**规则约束**：next_turn_does_not_cross_round

## sabina-09 · 战略控制

将技能范围内的任意1个友方小兵移动最多3格；无视重型小兵免疫。

**有序步骤**：ChooseFriendlyMinionTarget → OptionalTargetUnitMove → End

| 参数 | 值 | 含义 |
|---|---|---|
| HeroTarget | None | 英雄目标选择条件；None表示无需另选英雄 |
| IgnoreHeavyImmunity | True | 该小兵选择步骤是否忽略重型小兵免疫 |
| TargetMoveKeepsDistance | False | 移动目标时是否保持与来源的距离 |
| TextMoveDistance | 3 | 牌文移动的距离参数；实际预算由对应步骤决定 |
| TextMoveMinimum | 3 | 需要按牌文最短距离筛选路线时的下限 |
| TextPushDistance | 0 | 牌文推动的距离参数 |

**规则约束**：next_turn_does_not_cross_round

## sabina-10 · 武装密谋

如果你与一个友方小兵相邻，抵挡此次攻击且此回合你对其他敌方的所有行动免疫。

**有序步骤**：PrimaryDefense

| 参数 | 值 | 含义 |
|---|---|---|
| AttackKind | Any | 该主要防御适用的来袭攻击类型 |
| Block | True | 主要防御是否使用抵挡规则 |
| Followup | None | 防御后的弃牌、击败或可选直线移动流程 |
| IgnoresMinions | False | 该主要防御是否忽略小兵计算 |
| MinimumDistance | 1 | 攻击/主要防御适用的最小双方距离 |
| PersistImmunityThroughDefeat | False | 该防御免疫是否跨过来源被击败保留 |
| ProtectFromOtherAttacks | False | 防御是否带对其他攻击的免疫 |
| ProtectFromOtherEnemies | True | 防御是否带对其他敌方行动的免疫 |
| RequiresAdjacentFriendlyMinion | True | 主要防御是否要求相邻友方小兵 |
| SwapAfterMove | False | 主要防御是否带后续换牌步骤 |
| TextMoveDistance | 0 | 牌文移动的距离参数；实际预算由对应步骤决定 |

**规则约束**：next_turn_does_not_cross_round

## sabina-11 · 战略优势

将技能范围内的任意1个友方小兵移动最多3格；无视重型小兵免疫。可以重复一次。

**有序步骤**：ChooseFriendlyMinionTarget → OptionalTargetUnitMove → OptionalRepeatFriendlyMinionMove → OptionalTargetUnitMove → End

| 参数 | 值 | 含义 |
|---|---|---|
| HeroTarget | None | 英雄目标选择条件；None表示无需另选英雄 |
| IgnoreHeavyImmunity | True | 该小兵选择步骤是否忽略重型小兵免疫 |
| TargetMoveKeepsDistance | False | 移动目标时是否保持与来源的距离 |
| TextMoveDistance | 3 | 牌文移动的距离参数；实际预算由对应步骤决定 |
| TextMoveMinimum | 3 | 需要按牌文最短距离筛选路线时的下限 |
| TextPushDistance | 0 | 牌文推动的距离参数 |

**规则约束**：repeat_serial_responses_returns_reactions_before_next；next_turn_does_not_cross_round

## sabina-12 · 重型枪械

你的基础攻击+2攻击距离和+2攻击。如果你推动一名敌方英雄，该英雄丢弃一张卡牌，否则被击败。

**有序步骤**：AfterPushDiscardOrDefeat

| 参数 | 值 | 含义 |
|---|---|---|
| BasicAttackBonus | 2 | 紫卡对基础攻击的常驻加成 |
| BasicAttackRangeBonus | 2 | 紫卡对基础攻击距离的常驻加成 |
| BattleMinionCount | 0 | 紫卡在兵战中计作的小兵数量 |
| TraverseObstacles | False | 紫卡是否允许穿越障碍，终点仍须合法 |
| Trigger | AfterPushDiscardOrDefeat | 紫卡触发时点和流程类别 |

**规则约束**：next_turn_does_not_cross_round

## sabina-13 · 近身支援

如果你与一个友方小兵相邻，技能范围内的一名敌方英雄丢弃一张卡牌（如果可行）。

**有序步骤**：ChooseHeroTarget → TargetDiscardIfAble → End

| 参数 | 值 | 含义 |
|---|---|---|
| HeroTarget | EnemyInSkillRangeNearFriendlyMinion | 英雄目标选择条件；None表示无需另选英雄 |
| TargetMoveKeepsDistance | False | 移动目标时是否保持与来源的距离 |
| TextMoveDistance | 0 | 牌文移动的距离参数；实际预算由对应步骤决定 |
| TextMoveMinimum | 0 | 需要按牌文最短距离筛选路线时的下限 |
| TextPushDistance | 0 | 牌文推动的距离参数 |

**规则约束**：next_turn_does_not_cross_round

## sabina-14 · 战斗演练

本轮：当你或一名友方英雄执行攻击时，将技能范围内的所有友方小兵（包括免疫的）视为远程单位。

**有序步骤**：PrimaryMovement → ApplyEffect → End

| 参数 | 值 | 含义 |
|---|---|---|
| AreaKind | SkillRange | 效果区域的计算方式，实际覆盖格由规则查询产生 |
| Duration | ThisRound | 持续期类型；具体起止轮次在效果实例中 |
| Effect | FriendlyAttackMinionsRanged | 施加的持续效果类型，连接效果定义 |
| HeroTarget | None | 英雄目标选择条件；None表示无需另选英雄 |
| TargetMoveKeepsDistance | False | 移动目标时是否保持与来源的距离 |
| TextMoveDistance | 0 | 牌文移动的距离参数；实际预算由对应步骤决定 |
| TextMoveMinimum | 0 | 需要按牌文最短距离筛选路线时的下限 |
| TextPushDistance | 0 | 牌文推动的距离参数 |

**规则约束**：effect_source_recovery_or_defeat_cancels_unless_exception；next_turn_does_not_cross_round

## sabina-15 · 火力掩护

如果你与一个友方小兵相邻，则技能范围内的一名敌方英雄丢弃一张卡牌，否则被击败。

**有序步骤**：ChooseHeroTarget → TargetDiscardOrDefeat → End

| 参数 | 值 | 含义 |
|---|---|---|
| HeroTarget | EnemyInSkillRangeNearFriendlyMinion | 英雄目标选择条件；None表示无需另选英雄 |
| TargetMoveKeepsDistance | False | 移动目标时是否保持与来源的距离 |
| TextMoveDistance | 0 | 牌文移动的距离参数；实际预算由对应步骤决定 |
| TextMoveMinimum | 0 | 需要按牌文最短距离筛选路线时的下限 |
| TextPushDistance | 0 | 牌文推动的距离参数 |

**规则约束**：next_turn_does_not_cross_round

## sabina-16 · 战斗武装

本轮：当你或一名友方英雄执行攻击时，将技能范围内的所有友方小兵（包括免疫的）视为同时具有近战和远程。（这可以使每个小兵减少敌方英雄最多2点防御总值。）

**有序步骤**：PrimaryMovement → ApplyEffect → End

| 参数 | 值 | 含义 |
|---|---|---|
| AreaKind | SkillRange | 效果区域的计算方式，实际覆盖格由规则查询产生 |
| Duration | ThisRound | 持续期类型；具体起止轮次在效果实例中 |
| Effect | FriendlyAttackMinionsDual | 施加的持续效果类型，连接效果定义 |
| HeroTarget | None | 英雄目标选择条件；None表示无需另选英雄 |
| TargetMoveKeepsDistance | False | 移动目标时是否保持与来源的距离 |
| TextMoveDistance | 0 | 牌文移动的距离参数；实际预算由对应步骤决定 |
| TextMoveMinimum | 0 | 需要按牌文最短距离筛选路线时的下限 |
| TextPushDistance | 0 | 牌文推动的距离参数 |

**规则约束**：effect_source_recovery_or_defeat_cancels_unless_exception；next_turn_does_not_cross_round

## sabina-17 · 演练

本轮：当你或一名友方英雄执行基础攻击时，将技能范围内的所有友方小兵（包括免疫的）视为远程单位。

**有序步骤**：PrimaryMovement → ApplyEffect → End

| 参数 | 值 | 含义 |
|---|---|---|
| AreaKind | SkillRange | 效果区域的计算方式，实际覆盖格由规则查询产生 |
| Duration | ThisRound | 持续期类型；具体起止轮次在效果实例中 |
| Effect | FriendlyBasicMinionsRanged | 施加的持续效果类型，连接效果定义 |
| HeroTarget | None | 英雄目标选择条件；None表示无需另选英雄 |
| TargetMoveKeepsDistance | False | 移动目标时是否保持与来源的距离 |
| TextMoveDistance | 0 | 牌文移动的距离参数；实际预算由对应步骤决定 |
| TextMoveMinimum | 0 | 需要按牌文最短距离筛选路线时的下限 |
| TextPushDistance | 0 | 牌文推动的距离参数 |

**规则约束**：effect_source_recovery_or_defeat_cancels_unless_exception；next_turn_does_not_cross_round
