# shargatha 的公开卡牌语义

[返回目录](README.md)

## shargatha-00 · 反击

选择与你相邻的一个单位为目标。攻击后：此回合，当你因任何原因丢弃一张卡牌后，如果可行，执行你弃牌堆中一张攻击牌的主要行动。（优先执行完导致弃牌的行动。）

**有序步骤**：ChooseAttackTarget → Attack → ApplyEffect → End

| 参数 | 值 | 含义 |
|---|---|---|
| AdjacentAttack | True | 攻击是否限定相邻目标 |
| AreaKind | None | 效果区域的计算方式，实际覆盖格由规则查询产生 |
| AttackBonusKind | None | 牌文攻击加成的条件类型 |
| AttackBonusValue | 0 | 该牌文攻击加成的参数值 |
| AttackSubtype | melee | 本卡攻击步骤的近战/远程类型 |
| Duration | ThisTurn | 持续期类型；具体起止轮次在效果实例中 |
| Effect | AttackFromDiscard | 施加的持续效果类型，连接效果定义 |
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

**规则约束**：attack_default_enemy_legal_target；effect_source_recovery_or_defeat_cancels_unless_exception；one_reaction_per_discard_after_whole_causing_action；next_turn_does_not_cross_round

## shargatha-01 · 劈砍

选择与你相邻的一个单位为目标。每有一个与你相邻的敌方单位，+1攻击。（计算所有敌方单位，包括攻击目标。）

**有序步骤**：ChooseAttackTarget → Attack → End

| 参数 | 值 | 含义 |
|---|---|---|
| AdjacentAttack | True | 攻击是否限定相邻目标 |
| AttackBonusKind | AdjacentEnemies | 牌文攻击加成的条件类型 |
| AttackBonusValue | 1 | 该牌文攻击加成的参数值 |
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

## shargatha-02 · 快速突刺

选择攻击距离内且与你不相邻的一个单位为目标。

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

## shargatha-03 · 致命横扫

选择与你相邻的一个单位为目标。每有一个与你相邻的敌方单位，+2攻击。

**有序步骤**：ChooseAttackTarget → Attack → End

| 参数 | 值 | 含义 |
|---|---|---|
| AdjacentAttack | True | 攻击是否限定相邻目标 |
| AttackBonusKind | AdjacentEnemies | 牌文攻击加成的条件类型 |
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

## shargatha-04 · 横枪跃马

选择攻击距离内与你不相邻的一个单位为目标。攻击后：如果你与一个敌方英雄相邻，可以对不同目标重复一次。

**有序步骤**：ChooseAttackTarget → Attack → OptionalDifferentAttackIfAdjacentEnemy → Attack → End

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

**规则约束**：attack_default_enemy_legal_target；repeat_serial_responses_returns_reactions_before_next；next_turn_does_not_cross_round

## shargatha-05 · 死亡回旋

选择与你相邻的一个单位为目标。每有一个与你相邻的敌方单位，+3攻击。

**有序步骤**：ChooseAttackTarget → Attack → End

| 参数 | 值 | 含义 |
|---|---|---|
| AdjacentAttack | True | 攻击是否限定相邻目标 |
| AttackBonusKind | AdjacentEnemies | 牌文攻击加成的条件类型 |
| AttackBonusValue | 3 | 该牌文攻击加成的参数值 |
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

## shargatha-06 · 海妖之歌

选择攻击距离内与你不相邻的、最近的、非免疫的敌方单位为目标；沿最短有效路径将该单位向你移动最多2格。可重复一次。

**有序步骤**：ChooseNearestApproachTarget → OptionalApproachMove → OptionalRepeatApproach → End

| 参数 | 值 | 含义 |
|---|---|---|
| HeroTarget | None | 英雄目标选择条件；None表示无需另选英雄 |
| TargetMoveKeepsDistance | False | 移动目标时是否保持与来源的距离 |
| TextMoveDistance | 2 | 牌文移动的距离参数；实际预算由对应步骤决定 |
| TextMoveMinimum | 2 | 需要按牌文最短距离筛选路线时的下限 |
| TextPushDistance | 0 | 牌文推动的距离参数 |

**规则约束**：repeat_serial_responses_returns_reactions_before_next；next_turn_does_not_cross_round

## shargatha-07 · 魅惑

本轮：计算防御总值时，在技能范围内的敌方近战小兵视为友方单位。

**有序步骤**：PrimaryMovement → ApplyEffect → End

| 参数 | 值 | 含义 |
|---|---|---|
| AreaKind | SkillRange | 效果区域的计算方式，实际覆盖格由规则查询产生 |
| Duration | ThisRound | 持续期类型；具体起止轮次在效果实例中 |
| Effect | EnemyMeleeFriendlyForOwnDefense | 施加的持续效果类型，连接效果定义 |
| HeroTarget | None | 英雄目标选择条件；None表示无需另选英雄 |
| TargetMoveKeepsDistance | False | 移动目标时是否保持与来源的距离 |
| TextMoveDistance | 0 | 牌文移动的距离参数；实际预算由对应步骤决定 |
| TextMoveMinimum | 0 | 需要按牌文最短距离筛选路线时的下限 |
| TextPushDistance | 0 | 牌文推动的距离参数 |

**规则约束**：effect_source_recovery_or_defeat_cancels_unless_exception；minion_team_view_for_owner_defense_only；next_turn_does_not_cross_round

## shargatha-08 · 统治领域

本轮：计算防御总值时，在技能范围内的敌方远程小兵和近战小兵均视为友方单位。

**有序步骤**：PrimaryMovement → ApplyEffect → End

| 参数 | 值 | 含义 |
|---|---|---|
| AreaKind | SkillRange | 效果区域的计算方式，实际覆盖格由规则查询产生 |
| Duration | ThisRound | 持续期类型；具体起止轮次在效果实例中 |
| Effect | EnemyLightMinionsFriendlyForOwnDefense | 施加的持续效果类型，连接效果定义 |
| HeroTarget | None | 英雄目标选择条件；None表示无需另选英雄 |
| TargetMoveKeepsDistance | False | 移动目标时是否保持与来源的距离 |
| TextMoveDistance | 0 | 牌文移动的距离参数；实际预算由对应步骤决定 |
| TextMoveMinimum | 0 | 需要按牌文最短距离筛选路线时的下限 |
| TextPushDistance | 0 | 牌文推动的距离参数 |

**规则约束**：effect_source_recovery_or_defeat_cancels_unless_exception；minion_team_view_for_owner_defense_only；next_turn_does_not_cross_round

## shargatha-09 · 致命束缚

下回合：与你相邻的所有敌方英雄无法移动，只能执行金色或红色卡牌上的移动。

**有序步骤**：PrimaryMovement → ApplyEffect → End

| 参数 | 值 | 含义 |
|---|---|---|
| AreaKind | Adjacent | 效果区域的计算方式，实际覆盖格由规则查询产生 |
| Duration | NextTurn | 持续期类型；具体起止轮次在效果实例中 |
| Effect | EnemyMovementGoldOrRedOnly | 施加的持续效果类型，连接效果定义 |
| HeroTarget | None | 英雄目标选择条件；None表示无需另选英雄 |
| TargetMoveKeepsDistance | False | 移动目标时是否保持与来源的距离 |
| TextMoveDistance | 0 | 牌文移动的距离参数；实际预算由对应步骤决定 |
| TextMoveMinimum | 0 | 需要按牌文最短距离筛选路线时的下限 |
| TextPushDistance | 0 | 牌文推动的距离参数 |

**规则约束**：effect_source_recovery_or_defeat_cancels_unless_exception；dynamic_adjacent_movement_source_card_color_not_push_or_place；next_turn_does_not_cross_round

## shargatha-10 · 独霸一方

本轮：计算防御总值时，技能范围内所有敌方小兵（包括免疫的小兵）视为友方单位。

**有序步骤**：PrimaryMovement → ApplyEffect → End

| 参数 | 值 | 含义 |
|---|---|---|
| AreaKind | SkillRange | 效果区域的计算方式，实际覆盖格由规则查询产生 |
| Duration | ThisRound | 持续期类型；具体起止轮次在效果实例中 |
| Effect | EnemyAllMinionsFriendlyForOwnDefense | 施加的持续效果类型，连接效果定义 |
| HeroTarget | None | 英雄目标选择条件；None表示无需另选英雄 |
| TargetMoveKeepsDistance | False | 移动目标时是否保持与来源的距离 |
| TextMoveDistance | 0 | 牌文移动的距离参数；实际预算由对应步骤决定 |
| TextMoveMinimum | 0 | 需要按牌文最短距离筛选路线时的下限 |
| TextPushDistance | 0 | 牌文推动的距离参数 |

**规则约束**：effect_source_recovery_or_defeat_cancels_unless_exception；minion_team_view_for_owner_defense_only；next_turn_does_not_cross_round

## shargatha-11 · 死亡缠绕

下回合：与你相邻的敌方英雄无法移动，只能执行金色卡牌上的移动。

**有序步骤**：PrimaryMovement → ApplyEffect → End

| 参数 | 值 | 含义 |
|---|---|---|
| AreaKind | Adjacent | 效果区域的计算方式，实际覆盖格由规则查询产生 |
| Duration | NextTurn | 持续期类型；具体起止轮次在效果实例中 |
| Effect | EnemyMovementGoldOnly | 施加的持续效果类型，连接效果定义 |
| HeroTarget | None | 英雄目标选择条件；None表示无需另选英雄 |
| TargetMoveKeepsDistance | False | 移动目标时是否保持与来源的距离 |
| TextMoveDistance | 0 | 牌文移动的距离参数；实际预算由对应步骤决定 |
| TextMoveMinimum | 0 | 需要按牌文最短距离筛选路线时的下限 |
| TextPushDistance | 0 | 牌文推动的距离参数 |

**规则约束**：effect_source_recovery_or_defeat_cancels_unless_exception；dynamic_adjacent_movement_source_card_color_not_push_or_place；next_turn_does_not_cross_round

## shargatha-12 · 幻化

你可以穿过障碍物。在你执行行动之前，与你相邻的一个敌方英雄丢弃一张卡牌（如果可行）。

**有序步骤**：BeforeActionAdjacentDiscard

| 参数 | 值 | 含义 |
|---|---|---|
| BasicAttackBonus | 0 | 紫卡对基础攻击的常驻加成 |
| BasicAttackRangeBonus | 0 | 紫卡对基础攻击距离的常驻加成 |
| BattleMinionCount | 0 | 紫卡在兵战中计作的小兵数量 |
| TraverseObstacles | True | 紫卡是否允许穿越障碍，终点仍须合法 |
| Trigger | BeforeActionAdjacentDiscard | 紫卡触发时点和流程类别 |

**规则约束**：prelude_enemy_may_have_empty_hand_traverse_units_and_obstacles；prelude_includes_primary_secondary_fast_defense_excludes_internal_move；next_turn_does_not_cross_round

## shargatha-13 · 石化

此回合：技能范围内最近的敌方英雄获得免疫，无法移动，且视为地形。（若有多个英雄距离相同，则影响多个英雄）

**有序步骤**：ApplyEffect → End

| 参数 | 值 | 含义 |
|---|---|---|
| AreaKind | SkillRange | 效果区域的计算方式，实际覆盖格由规则查询产生 |
| Duration | ThisTurn | 持续期类型；具体起止轮次在效果实例中 |
| Effect | PetrifyNearestEnemyHeroes | 施加的持续效果类型，连接效果定义 |
| HeroTarget | None | 英雄目标选择条件；None表示无需另选英雄 |
| TargetMoveKeepsDistance | False | 移动目标时是否保持与来源的距离 |
| TextMoveDistance | 0 | 牌文移动的距离参数；实际预算由对应步骤决定 |
| TextMoveMinimum | 0 | 需要按牌文最短距离筛选路线时的下限 |
| TextPushDistance | 0 | 牌文推动的距离参数 |

**规则约束**：effect_source_recovery_or_defeat_cancels_unless_exception；nearest_before_immunity_all_ties；petrify_preserves_hero_actions_recompute_after_whole_move；next_turn_does_not_cross_round

## shargatha-14 · 石化之眼

此回合：技能范围内最近的敌方英雄获得免疫，无法移动，且视为地形。

**有序步骤**：ApplyEffect → End

| 参数 | 值 | 含义 |
|---|---|---|
| AreaKind | SkillRange | 效果区域的计算方式，实际覆盖格由规则查询产生 |
| Duration | ThisTurn | 持续期类型；具体起止轮次在效果实例中 |
| Effect | PetrifyNearestEnemyHeroes | 施加的持续效果类型，连接效果定义 |
| HeroTarget | None | 英雄目标选择条件；None表示无需另选英雄 |
| TargetMoveKeepsDistance | False | 移动目标时是否保持与来源的距离 |
| TextMoveDistance | 0 | 牌文移动的距离参数；实际预算由对应步骤决定 |
| TextMoveMinimum | 0 | 需要按牌文最短距离筛选路线时的下限 |
| TextPushDistance | 0 | 牌文推动的距离参数 |

**规则约束**：effect_source_recovery_or_defeat_cancels_unless_exception；nearest_before_immunity_all_ties；petrify_preserves_hero_actions_recompute_after_whole_move；next_turn_does_not_cross_round

## shargatha-15 · 忠实信徒

如果你与一个小兵相邻，你可以拿回一张已丢弃的卡牌。

**有序步骤**：OptionalRecoverDiscard → End

| 参数 | 值 | 含义 |
|---|---|---|
| HeroTarget | None | 英雄目标选择条件；None表示无需另选英雄 |
| RecoverResolved | False | 取回是否还允许已结算区，而非仅弃牌区 |
| RecoveryRequiresAdjacentMinion | True | 取回步骤是否要求与小兵相邻 |
| TargetMoveKeepsDistance | False | 移动目标时是否保持与来源的距离 |
| TextMoveDistance | 0 | 牌文移动的距离参数；实际预算由对应步骤决定 |
| TextMoveMinimum | 0 | 需要按牌文最短距离筛选路线时的下限 |
| TextPushDistance | 0 | 牌文推动的距离参数 |

**规则约束**：recovery_card_chosen_by_recipient；next_turn_does_not_cross_round

## shargatha-16 · 石化凝视

此回合：技能范围内的所有敌方英雄获得免疫，无法移动，且视为地形。

**有序步骤**：ApplyEffect → End

| 参数 | 值 | 含义 |
|---|---|---|
| AreaKind | SkillRange | 效果区域的计算方式，实际覆盖格由规则查询产生 |
| Duration | ThisTurn | 持续期类型；具体起止轮次在效果实例中 |
| Effect | PetrifyAllEnemyHeroes | 施加的持续效果类型，连接效果定义 |
| HeroTarget | None | 英雄目标选择条件；None表示无需另选英雄 |
| TargetMoveKeepsDistance | False | 移动目标时是否保持与来源的距离 |
| TextMoveDistance | 0 | 牌文移动的距离参数；实际预算由对应步骤决定 |
| TextMoveMinimum | 0 | 需要按牌文最短距离筛选路线时的下限 |
| TextPushDistance | 0 | 牌文推动的距离参数 |

**规则约束**：effect_source_recovery_or_defeat_cancels_unless_exception；petrify_preserves_hero_actions_recompute_after_whole_move；next_turn_does_not_cross_round

## shargatha-17 · 至死不渝

如果你与一个小兵相邻，你可以拿回一张已丢弃的卡牌，然后此回合：你免疫攻击行动。

**有序步骤**：OptionalRecoverDiscard → ApplyEffectIfRecovered → End

| 参数 | 值 | 含义 |
|---|---|---|
| AreaKind | None | 效果区域的计算方式，实际覆盖格由规则查询产生 |
| Duration | ThisTurn | 持续期类型；具体起止轮次在效果实例中 |
| Effect | AttackActionImmunity | 施加的持续效果类型，连接效果定义 |
| HeroTarget | None | 英雄目标选择条件；None表示无需另选英雄 |
| RecoverResolved | False | 取回是否还允许已结算区，而非仅弃牌区 |
| RecoveryRequiresAdjacentMinion | True | 取回步骤是否要求与小兵相邻 |
| TargetMoveKeepsDistance | False | 移动目标时是否保持与来源的距离 |
| TextMoveDistance | 0 | 牌文移动的距离参数；实际预算由对应步骤决定 |
| TextMoveMinimum | 0 | 需要按牌文最短距离筛选路线时的下限 |
| TextPushDistance | 0 | 牌文推动的距离参数 |

**规则约束**：recovery_card_chosen_by_recipient；immunity_requires_actual_recovery；effect_source_recovery_or_defeat_cancels_unless_exception；next_turn_does_not_cross_round
