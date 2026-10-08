# arien 的公开卡牌语义

[返回目录](README.md)

## arien-00 · 华丽刀锋

选择与你相邻的一个单位为目标。攻击前：你可以将另一个与目标相邻的单位移动1格。（另一个单位不能选择你自己。）

**有序步骤**：ChooseAttackTarget → OptionalMoveOtherAdjacentToTarget → Attack → End

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

## arien-01 · 汹涌

选择攻击距离内的一个单位为目标。攻击后：将最多一个与你相邻的标志物或敌方单位推动1格。

**有序步骤**：ChooseAttackTarget → Attack → ChooseUnitPush → End

| 参数 | 值 | 含义 |
|---|---|---|
| AdjacentAttack | False | 攻击是否限定相邻目标 |
| AttackBonusKind | None | 牌文攻击加成的条件类型 |
| AttackBonusValue | 0 | 该牌文攻击加成的参数值 |
| AttackSubtype | ranged | 本卡攻击步骤的近战/远程类型 |
| ExcludeStraightLine | False | 攻击目标是否排除六角直线上的单位 |
| FixedPushDistance | True | 推动距离固定还是可选择上限以内距离 |
| HeroTarget | None | 英雄目标选择条件；None表示无需另选英雄 |
| MinimumDistance | 1 | 攻击/主要防御适用的最小双方距离 |
| OnlyHeroes | False | 攻击目标是否只允许英雄 |
| OptionalPushTarget | True | 该推动选择是否允许不选目标 |
| PushTarget | EnemyUnit | 选择推动目标时允许的单位类型 |
| RangeBonusKind | None | 牌文攻击距离加成的条件类型 |
| RangeBonusValue | 0 | 该牌文距离加成的参数值 |
| SupportMakesUnblockable | False | 满足友方支援条件是否使攻击无法抵挡 |
| TargetMoveKeepsDistance | False | 移动目标时是否保持与来源的距离 |
| TextMoveDistance | 0 | 牌文移动的距离参数；实际预算由对应步骤决定 |
| TextMoveMinimum | 0 | 需要按牌文最短距离筛选路线时的下限 |
| TextPushDistance | 1 | 牌文推动的距离参数 |

**规则约束**：attack_default_enemy_legal_target；next_turn_does_not_cross_round

**现有未裁定/未扩展边界**：D050_map_markers_not_in_current_six_hero_content

## arien-02 · 激流

选择与你相邻的一个单位为目标。攻击前：目标正后方直线3格内的一个敌方英雄丢弃一张卡牌，否则被击败。

**有序步骤**：ChooseAttackTarget → OtherHeroDiscardOrDefeatIfAvailable → Attack → End

| 参数 | 值 | 含义 |
|---|---|---|
| AdjacentAttack | True | 攻击是否限定相邻目标 |
| AttackBonusKind | None | 牌文攻击加成的条件类型 |
| AttackBonusValue | 0 | 该牌文攻击加成的参数值 |
| AttackSubtype | melee | 本卡攻击步骤的近战/远程类型 |
| ExcludeStraightLine | False | 攻击目标是否排除六角直线上的单位 |
| HeroTarget | EnemyBehindAttackTarget | 英雄目标选择条件；None表示无需另选英雄 |
| MinimumDistance | 1 | 攻击/主要防御适用的最小双方距离 |
| OnlyHeroes | False | 攻击目标是否只允许英雄 |
| RangeBonusKind | None | 牌文攻击距离加成的条件类型 |
| RangeBonusValue | 0 | 该牌文距离加成的参数值 |
| RearTargetDistance | 3 | 相对原攻击目标检查后方英雄的距离 |
| SupportMakesUnblockable | False | 满足友方支援条件是否使攻击无法抵挡 |
| TargetMoveKeepsDistance | False | 移动目标时是否保持与来源的距离 |
| TextMoveDistance | 0 | 牌文移动的距离参数；实际预算由对应步骤决定 |
| TextMoveMinimum | 0 | 需要按牌文最短距离筛选路线时的下限 |
| TextPushDistance | 0 | 牌文推动的距离参数 |

**规则约束**：attack_default_enemy_legal_target；no_rear_target_continues_original_attack；next_turn_does_not_cross_round

## arien-03 · 潮起

选择攻击距离内的一个单位为目标。攻击后：将最多一个与你相邻的标志物或敌方单位推动1格。

**有序步骤**：ChooseAttackTarget → Attack → ChooseUnitPush → End

| 参数 | 值 | 含义 |
|---|---|---|
| AdjacentAttack | False | 攻击是否限定相邻目标 |
| AttackBonusKind | None | 牌文攻击加成的条件类型 |
| AttackBonusValue | 0 | 该牌文攻击加成的参数值 |
| AttackSubtype | ranged | 本卡攻击步骤的近战/远程类型 |
| ExcludeStraightLine | False | 攻击目标是否排除六角直线上的单位 |
| FixedPushDistance | True | 推动距离固定还是可选择上限以内距离 |
| HeroTarget | None | 英雄目标选择条件；None表示无需另选英雄 |
| MinimumDistance | 1 | 攻击/主要防御适用的最小双方距离 |
| OnlyHeroes | False | 攻击目标是否只允许英雄 |
| OptionalPushTarget | True | 该推动选择是否允许不选目标 |
| PushTarget | EnemyUnit | 选择推动目标时允许的单位类型 |
| RangeBonusKind | None | 牌文攻击距离加成的条件类型 |
| RangeBonusValue | 0 | 该牌文距离加成的参数值 |
| SupportMakesUnblockable | False | 满足友方支援条件是否使攻击无法抵挡 |
| TargetMoveKeepsDistance | False | 移动目标时是否保持与来源的距离 |
| TextMoveDistance | 0 | 牌文移动的距离参数；实际预算由对应步骤决定 |
| TextMoveMinimum | 0 | 需要按牌文最短距离筛选路线时的下限 |
| TextPushDistance | 1 | 牌文推动的距离参数 |

**规则约束**：attack_default_enemy_legal_target；next_turn_does_not_cross_round

**现有未裁定/未扩展边界**：D050_map_markers_not_in_current_six_hero_content

## arien-04 · 惊涛骇浪

选择与你相邻的一个单位为目标。攻击前：目标正后方直线5格内的一个敌方英雄丢弃一张卡牌，否则被击败。可以对不同目标重复一次。

**有序步骤**：ChooseAttackTarget → OtherHeroDiscardOrDefeatIfAvailable → Attack → OptionalDifferentFullAttack → OtherHeroDiscardOrDefeatIfAvailable → Attack → End

| 参数 | 值 | 含义 |
|---|---|---|
| AdjacentAttack | True | 攻击是否限定相邻目标 |
| AttackBonusKind | None | 牌文攻击加成的条件类型 |
| AttackBonusValue | 0 | 该牌文攻击加成的参数值 |
| AttackSubtype | melee | 本卡攻击步骤的近战/远程类型 |
| ExcludeStraightLine | False | 攻击目标是否排除六角直线上的单位 |
| HeroTarget | EnemyBehindAttackTarget | 英雄目标选择条件；None表示无需另选英雄 |
| MinimumDistance | 1 | 攻击/主要防御适用的最小双方距离 |
| OnlyHeroes | False | 攻击目标是否只允许英雄 |
| RangeBonusKind | None | 牌文攻击距离加成的条件类型 |
| RangeBonusValue | 0 | 该牌文距离加成的参数值 |
| RearTargetDistance | 5 | 相对原攻击目标检查后方英雄的距离 |
| SupportMakesUnblockable | False | 满足友方支援条件是否使攻击无法抵挡 |
| TargetMoveKeepsDistance | False | 移动目标时是否保持与来源的距离 |
| TextMoveDistance | 0 | 牌文移动的距离参数；实际预算由对应步骤决定 |
| TextMoveMinimum | 0 | 需要按牌文最短距离筛选路线时的下限 |
| TextPushDistance | 0 | 牌文推动的距离参数 |

**规则约束**：attack_default_enemy_legal_target；no_rear_target_continues_original_attack；repeat_serial_responses_returns_reactions_before_next；next_turn_does_not_cross_round

## arien-05 · 巨浪滔天

选择攻击距离内的一个单位为目标。攻击后：将目标移动最多1格。将最多一个与你相邻的标志物或敌方单位推动1格。

**有序步骤**：ChooseAttackTarget → Attack → OptionalTargetUnitMove → ChooseUnitPush → End

| 参数 | 值 | 含义 |
|---|---|---|
| AdjacentAttack | False | 攻击是否限定相邻目标 |
| AttackBonusKind | None | 牌文攻击加成的条件类型 |
| AttackBonusValue | 0 | 该牌文攻击加成的参数值 |
| AttackSubtype | ranged | 本卡攻击步骤的近战/远程类型 |
| ExcludeStraightLine | False | 攻击目标是否排除六角直线上的单位 |
| FixedPushDistance | True | 推动距离固定还是可选择上限以内距离 |
| HeroTarget | None | 英雄目标选择条件；None表示无需另选英雄 |
| MinimumDistance | 1 | 攻击/主要防御适用的最小双方距离 |
| OnlyHeroes | False | 攻击目标是否只允许英雄 |
| OptionalPushTarget | True | 该推动选择是否允许不选目标 |
| PushTarget | EnemyUnit | 选择推动目标时允许的单位类型 |
| RangeBonusKind | None | 牌文攻击距离加成的条件类型 |
| RangeBonusValue | 0 | 该牌文距离加成的参数值 |
| SupportMakesUnblockable | False | 满足友方支援条件是否使攻击无法抵挡 |
| TargetMoveKeepsDistance | False | 移动目标时是否保持与来源的距离 |
| TextMoveDistance | 1 | 牌文移动的距离参数；实际预算由对应步骤决定 |
| TextMoveMinimum | 1 | 需要按牌文最短距离筛选路线时的下限 |
| TextPushDistance | 1 | 牌文推动的距离参数 |

**规则约束**：attack_default_enemy_legal_target；next_turn_does_not_cross_round

**现有未裁定/未扩展边界**：D050_map_markers_not_in_current_six_hero_content

## arien-06 · 打断施法

此回合：在技能范围内的敌方英雄不能执行技能。（打断施法不会阻止攻击行动。）

**有序步骤**：ApplyEffect → End

| 参数 | 值 | 含义 |
|---|---|---|
| AreaKind | SkillRange | 效果区域的计算方式，实际覆盖格由规则查询产生 |
| Duration | ThisTurn | 持续期类型；具体起止轮次在效果实例中 |
| Effect | SkillSuppression | 施加的持续效果类型，连接效果定义 |
| HeroTarget | None | 英雄目标选择条件；None表示无需另选英雄 |
| TargetMoveKeepsDistance | False | 移动目标时是否保持与来源的距离 |
| TextMoveDistance | 0 | 牌文移动的距离参数；实际预算由对应步骤决定 |
| TextMoveMinimum | 0 | 需要按牌文最短距离筛选路线时的下限 |
| TextPushDistance | 0 | 牌文推动的距离参数 |

**规则约束**：effect_source_recovery_or_defeat_cancels_unless_exception；next_turn_does_not_cross_round

## arien-07 · 潮水

将你放置到攻击距离内没有出生点，且不与空的出生点相邻的格子内。

**有序步骤**：ChooseSelfPlacement → End

| 参数 | 值 | 含义 |
|---|---|---|
| HeroTarget | None | 英雄目标选择条件；None表示无需另选英雄 |
| PlacementTarget | EmptyNoSpawnAwayFromEmptySpawns | 自身放置的目的格规则 |
| TargetMoveKeepsDistance | False | 移动目标时是否保持与来源的距离 |
| TextMoveDistance | 0 | 牌文移动的距离参数；实际预算由对应步骤决定 |
| TextMoveMinimum | 0 | 需要按牌文最短距离筛选路线时的下限 |
| TextPushDistance | 0 | 牌文推动的距离参数 |

**规则约束**：empty_spawn_test_after_placement_includes_minion_spawns；next_turn_does_not_cross_round

## arien-08 · 奥术换位

与攻击距离内的一个小兵或友方英雄换位。（与目标换位，这不是移动。）

**有序步骤**：ChooseUnitSwapTarget → SwapTargetUnits → End

| 参数 | 值 | 含义 |
|---|---|---|
| HeroTarget | None | 英雄目标选择条件；None表示无需另选英雄 |
| TargetMoveKeepsDistance | False | 移动目标时是否保持与来源的距离 |
| TextMoveDistance | 0 | 牌文移动的距离参数；实际预算由对应步骤决定 |
| TextMoveMinimum | 0 | 需要按牌文最短距离筛选路线时的下限 |
| TextPushDistance | 0 | 牌文推动的距离参数 |
| UnitSwapTarget | MinionOrFriendlyHeroInAttackRange | 换位目标的阵营、单位类型和范围条件 |

**规则约束**：next_turn_does_not_cross_round

## arien-09 · 魔法水流

将你放置到攻击距离内没有出生点，且不与空的出生点相邻的格子内。

**有序步骤**：ChooseSelfPlacement → End

| 参数 | 值 | 含义 |
|---|---|---|
| HeroTarget | None | 英雄目标选择条件；None表示无需另选英雄 |
| PlacementTarget | EmptyNoSpawnAwayFromEmptySpawns | 自身放置的目的格规则 |
| TargetMoveKeepsDistance | False | 移动目标时是否保持与来源的距离 |
| TextMoveDistance | 0 | 牌文移动的距离参数；实际预算由对应步骤决定 |
| TextMoveMinimum | 0 | 需要按牌文最短距离筛选路线时的下限 |
| TextPushDistance | 0 | 牌文推动的距离参数 |

**规则约束**：empty_spawn_test_after_placement_includes_minion_spawns；next_turn_does_not_cross_round

## arien-10 · 移形换步

与攻击距离内的一个单位或一个标志物换位。

**有序步骤**：ChooseUnitSwapTarget → SwapTargetUnits → End

| 参数 | 值 | 含义 |
|---|---|---|
| HeroTarget | None | 英雄目标选择条件；None表示无需另选英雄 |
| TargetMoveKeepsDistance | False | 移动目标时是否保持与来源的距离 |
| TextMoveDistance | 0 | 牌文移动的距离参数；实际预算由对应步骤决定 |
| TextMoveMinimum | 0 | 需要按牌文最短距离筛选路线时的下限 |
| TextPushDistance | 0 | 牌文推动的距离参数 |
| UnitSwapTarget | AnyUnitInAttackRange | 换位目标的阵营、单位类型和范围条件 |

**规则约束**：next_turn_does_not_cross_round

**现有未裁定/未扩展边界**：D050_map_markers_not_in_current_six_hero_content

## arien-11 · 潮汐之力

将你放置到攻击距离内没有出生点的格子内。

**有序步骤**：ChooseSelfPlacement → End

| 参数 | 值 | 含义 |
|---|---|---|
| HeroTarget | None | 英雄目标选择条件；None表示无需另选英雄 |
| PlacementTarget | EmptyNoSpawnInAttackRange | 自身放置的目的格规则 |
| TargetMoveKeepsDistance | False | 移动目标时是否保持与来源的距离 |
| TextMoveDistance | 0 | 牌文移动的距离参数；实际预算由对应步骤决定 |
| TextMoveMinimum | 0 | 需要按牌文最短距离筛选路线时的下限 |
| TextPushDistance | 0 | 牌文推动的距离参数 |

**规则约束**：next_turn_does_not_cross_round

## arien-12 · 潮汐之主

在你执行基础技能后，可以在你所在的战斗区域触发一场小兵战斗。

**有序步骤**：AfterBasicSkillMinionBattle

| 参数 | 值 | 含义 |
|---|---|---|
| BasicAttackBonus | 0 | 紫卡对基础攻击的常驻加成 |
| BasicAttackRangeBonus | 0 | 紫卡对基础攻击距离的常驻加成 |
| BattleMinionCount | 0 | 紫卡在兵战中计作的小兵数量 |
| TraverseObstacles | False | 紫卡是否允许穿越障碍，终点仍须合法 |
| Trigger | AfterBasicSkillMinionBattle | 紫卡触发时点和流程类别 |

**规则约束**：optional_battle_only_if_owner_in_current_combat_region；next_turn_does_not_cross_round

## arien-13 · 挑战者

无视所有的小兵防御修正。

**有序步骤**：PrimaryDefense

| 参数 | 值 | 含义 |
|---|---|---|
| AttackKind | Any | 该主要防御适用的来袭攻击类型 |
| Block | False | 主要防御是否使用抵挡规则 |
| Followup | None | 防御后的弃牌、击败或可选直线移动流程 |
| IgnoresMinions | True | 该主要防御是否忽略小兵计算 |
| MinimumDistance | 1 | 攻击/主要防御适用的最小双方距离 |
| PersistImmunityThroughDefeat | False | 该防御免疫是否跨过来源被击败保留 |
| ProtectFromOtherAttacks | False | 防御是否带对其他攻击的免疫 |
| ProtectFromOtherEnemies | False | 防御是否带对其他敌方行动的免疫 |
| RequiresAdjacentFriendlyMinion | False | 主要防御是否要求相邻友方小兵 |
| SwapAfterMove | False | 主要防御是否带后续换牌步骤 |
| TextMoveDistance | 0 | 牌文移动的距离参数；实际预算由对应步骤决定 |

**规则约束**：next_turn_does_not_cross_round

## arien-14 · 滑溜溜

此回合：技能范围内敌方单位的移动行动最多移动1格。（技能范围外开始移动的敌人不受影响）

**有序步骤**：ApplyEffect → End

| 参数 | 值 | 含义 |
|---|---|---|
| AreaKind | SkillRange | 效果区域的计算方式，实际覆盖格由规则查询产生 |
| Duration | ThisTurn | 持续期类型；具体起止轮次在效果实例中 |
| Effect | EnemyActionMovementLimitOne | 施加的持续效果类型，连接效果定义 |
| HeroTarget | None | 英雄目标选择条件；None表示无需另选英雄 |
| TargetMoveKeepsDistance | False | 移动目标时是否保持与来源的距离 |
| TextMoveDistance | 0 | 牌文移动的距离参数；实际预算由对应步骤决定 |
| TextMoveMinimum | 0 | 需要按牌文最短距离筛选路线时的下限 |
| TextPushDistance | 0 | 牌文推动的距离参数 |

**规则约束**：effect_source_recovery_or_defeat_cancels_unless_exception；limit_one_at_action_origin_not_card_text_movement；next_turn_does_not_cross_round

## arien-15 · 决斗家

无视所有的小兵防御修正。此回合：你免疫其他敌人的所有攻击行动。

**有序步骤**：PrimaryDefense

| 参数 | 值 | 含义 |
|---|---|---|
| AttackKind | Any | 该主要防御适用的来袭攻击类型 |
| Block | False | 主要防御是否使用抵挡规则 |
| Followup | None | 防御后的弃牌、击败或可选直线移动流程 |
| IgnoresMinions | True | 该主要防御是否忽略小兵计算 |
| MinimumDistance | 1 | 攻击/主要防御适用的最小双方距离 |
| PersistImmunityThroughDefeat | True | 该防御免疫是否跨过来源被击败保留 |
| ProtectFromOtherAttacks | True | 防御是否带对其他攻击的免疫 |
| ProtectFromOtherEnemies | False | 防御是否带对其他敌方行动的免疫 |
| RequiresAdjacentFriendlyMinion | False | 主要防御是否要求相邻友方小兵 |
| SwapAfterMove | False | 主要防御是否带后续换牌步骤 |
| TextMoveDistance | 0 | 牌文移动的距离参数；实际预算由对应步骤决定 |

**规则约束**：defense_immunity_even_on_failure_exempts_attacker；next_turn_does_not_cross_round

## arien-16 · 传奇决斗家

无视所有的小兵防御修正。此回合：你免疫其他敌人的所有行动。

**有序步骤**：PrimaryDefense

| 参数 | 值 | 含义 |
|---|---|---|
| AttackKind | Any | 该主要防御适用的来袭攻击类型 |
| Block | False | 主要防御是否使用抵挡规则 |
| Followup | None | 防御后的弃牌、击败或可选直线移动流程 |
| IgnoresMinions | True | 该主要防御是否忽略小兵计算 |
| MinimumDistance | 1 | 攻击/主要防御适用的最小双方距离 |
| PersistImmunityThroughDefeat | True | 该防御免疫是否跨过来源被击败保留 |
| ProtectFromOtherAttacks | False | 防御是否带对其他攻击的免疫 |
| ProtectFromOtherEnemies | True | 防御是否带对其他敌方行动的免疫 |
| RequiresAdjacentFriendlyMinion | False | 主要防御是否要求相邻友方小兵 |
| SwapAfterMove | False | 主要防御是否带后续换牌步骤 |
| TextMoveDistance | 0 | 牌文移动的距离参数；实际预算由对应步骤决定 |

**规则约束**：defense_immunity_even_on_failure_exempts_attacker；next_turn_does_not_cross_round

## arien-17 · 洪水

此回合和下回合：技能范围内敌方单位的移动行动最多移动1格。

**有序步骤**：ApplyEffect → End

| 参数 | 值 | 含义 |
|---|---|---|
| AreaKind | SkillRange | 效果区域的计算方式，实际覆盖格由规则查询产生 |
| Duration | ThisAndNextTurn | 持续期类型；具体起止轮次在效果实例中 |
| Effect | EnemyActionMovementLimitOne | 施加的持续效果类型，连接效果定义 |
| HeroTarget | None | 英雄目标选择条件；None表示无需另选英雄 |
| TargetMoveKeepsDistance | False | 移动目标时是否保持与来源的距离 |
| TextMoveDistance | 0 | 牌文移动的距离参数；实际预算由对应步骤决定 |
| TextMoveMinimum | 0 | 需要按牌文最短距离筛选路线时的下限 |
| TextPushDistance | 0 | 牌文推动的距离参数 |

**规则约束**：effect_source_recovery_or_defeat_cancels_unless_exception；limit_one_at_action_origin_not_card_text_movement；next_turn_does_not_cross_round
