# brogan 的公开卡牌语义

[返回目录](README.md)

## brogan-00 · 猛攻

选择与你相邻的一个单位为目标。攻击后：移动到对方所在的位置。

**有序步骤**：ChooseAttackTarget → Attack → MoveIntoAttackTargetCell → End

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

## brogan-01 · 冲撞

攻击前：沿直线移动2格到与敌方单位相邻的位置，然后以该单位为目标。（如果你无法完成此移动，就不能攻击。）

**有序步骤**：RequiredStraightMoveToAttack → ChooseAttackTarget → Attack → End

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

## brogan-02 · 投掷飞斧

攻击前：你可以丢弃一张卡牌。若如此做，则+2攻击距离。选择攻击距离内的一个单位为目标。

**有序步骤**：ChooseOptionalDiscard → DetermineAttackRange → ChooseAttackTarget → Attack → End

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
| RangeBonusKind | DiscardedBeforeAttack | 牌文攻击距离加成的条件类型 |
| RangeBonusValue | 2 | 该牌文距离加成的参数值 |
| SupportMakesUnblockable | False | 满足友方支援条件是否使攻击无法抵挡 |
| TargetMoveKeepsDistance | False | 移动目标时是否保持与来源的距离 |
| TextMoveDistance | 0 | 牌文移动的距离参数；实际预算由对应步骤决定 |
| TextMoveMinimum | 0 | 需要按牌文最短距离筛选路线时的下限 |
| TextPushDistance | 0 | 牌文推动的距离参数 |

**规则约束**：attack_default_enemy_legal_target；next_turn_does_not_cross_round

## brogan-03 · 奋勇冲锋

攻击前：沿直线移动2或3格到与敌方单位相邻的位置，然后以该单位为目标。

**有序步骤**：RequiredStraightMoveToAttack → ChooseAttackTarget → Attack → End

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
| TextMoveDistance | 3 | 牌文移动的距离参数；实际预算由对应步骤决定 |
| TextMoveMinimum | 2 | 需要按牌文最短距离筛选路线时的下限 |
| TextPushDistance | 0 | 牌文推动的距离参数 |

**规则约束**：attack_default_enemy_legal_target；next_turn_does_not_cross_round

## brogan-04 · 投掷长矛

攻击前：你可以丢弃一张卡牌。如果你的弃牌堆中有卡牌，则+2攻击距离。选择攻击距离内的一个单位为目标。

**有序步骤**：ChooseOptionalDiscard → DetermineAttackRange → ChooseAttackTarget → Attack → End

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
| RangeBonusKind | OwnDiscardPile | 牌文攻击距离加成的条件类型 |
| RangeBonusValue | 2 | 该牌文距离加成的参数值 |
| SupportMakesUnblockable | False | 满足友方支援条件是否使攻击无法抵挡 |
| TargetMoveKeepsDistance | False | 移动目标时是否保持与来源的距离 |
| TextMoveDistance | 0 | 牌文移动的距离参数；实际预算由对应步骤决定 |
| TextMoveMinimum | 0 | 需要按牌文最短距离筛选路线时的下限 |
| TextPushDistance | 0 | 牌文推动的距离参数 |

**规则约束**：attack_default_enemy_legal_target；next_turn_does_not_cross_round

## brogan-05 · 勇往直前

攻击前：沿直线移动2、3或4格到与敌方单位相邻的位置，然后以该单位为目标。

**有序步骤**：RequiredStraightMoveToAttack → ChooseAttackTarget → Attack → End

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
| TextMoveDistance | 4 | 牌文移动的距离参数；实际预算由对应步骤决定 |
| TextMoveMinimum | 2 | 需要按牌文最短距离筛选路线时的下限 |
| TextPushDistance | 0 | 牌文推动的距离参数 |

**规则约束**：attack_default_enemy_legal_target；next_turn_does_not_cross_round

## brogan-06 · 铜墙铁壁

选择一项：本轮，你和技能范围内的友方单位不能被敌方英雄移动、推动、换位或强制移动；或如果你的弃牌堆为空，取回此卡牌。

**有序步骤**：ChooseProtectionOrSelfRecovery → End

| 参数 | 值 | 含义 |
|---|---|---|
| AreaKind | SkillRange | 效果区域的计算方式，实际覆盖格由规则查询产生 |
| Duration | ThisRound | 持续期类型；具体起止轮次在效果实例中 |
| Effect | FriendlyDisplacementProtection | 施加的持续效果类型，连接效果定义 |
| HeroTarget | None | 英雄目标选择条件；None表示无需另选英雄 |
| TargetMoveKeepsDistance | False | 移动目标时是否保持与来源的距离 |
| TextMoveDistance | 0 | 牌文移动的距离参数；实际预算由对应步骤决定 |
| TextMoveMinimum | 0 | 需要按牌文最短距离筛选路线时的下限 |
| TextPushDistance | 0 | 牌文推动的距离参数 |

**规则约束**：effect_source_recovery_or_defeat_cancels_unless_exception；next_turn_does_not_cross_round

## brogan-07 · 保卫

本轮：如果技能范围内的一个友方近战小兵将被击败，你可以丢弃一张卡牌。若如此做，该小兵不会被击败。

**有序步骤**：ApplyEffect → End

| 参数 | 值 | 含义 |
|---|---|---|
| AreaKind | SkillRange | 效果区域的计算方式，实际覆盖格由规则查询产生 |
| Duration | ThisRound | 持续期类型；具体起止轮次在效果实例中 |
| Effect | FriendlyMeleeDefeatPrevention | 施加的持续效果类型，连接效果定义 |
| HeroTarget | None | 英雄目标选择条件；None表示无需另选英雄 |
| TargetMoveKeepsDistance | False | 移动目标时是否保持与来源的距离 |
| TextMoveDistance | 0 | 牌文移动的距离参数；实际预算由对应步骤决定 |
| TextMoveMinimum | 0 | 需要按牌文最短距离筛选路线时的下限 |
| TextPushDistance | 0 | 牌文推动的距离参数 |

**规则约束**：effect_source_recovery_or_defeat_cancels_unless_exception；next_turn_does_not_cross_round

## brogan-08 · 战鼓

如果你或攻击距离内的一个友方英雄与敌方单位相邻，则该友方英雄可以拿回一张已丢弃的卡牌。

**有序步骤**：ChooseHeroTarget → OptionalRecoverDiscard → End

| 参数 | 值 | 含义 |
|---|---|---|
| HeroTarget | AlliedNearEnemy | 英雄目标选择条件；None表示无需另选英雄 |
| RecoverResolved | False | 取回是否还允许已结算区，而非仅弃牌区 |
| RecoveryRequiresAdjacentMinion | False | 取回步骤是否要求与小兵相邻 |
| TargetMoveKeepsDistance | False | 移动目标时是否保持与来源的距离 |
| TextMoveDistance | 0 | 牌文移动的距离参数；实际预算由对应步骤决定 |
| TextMoveMinimum | 0 | 需要按牌文最短距离筛选路线时的下限 |
| TextPushDistance | 0 | 牌文推动的距离参数 |

**规则约束**：recovery_card_chosen_by_recipient；next_turn_does_not_cross_round

## brogan-09 · 持盾

本轮：如果技能范围内的一个友方非重型小兵将被击败，你可以丢弃一张卡牌。若如此做，该小兵不会被击败。

**有序步骤**：ApplyEffect → End

| 参数 | 值 | 含义 |
|---|---|---|
| AreaKind | SkillRange | 效果区域的计算方式，实际覆盖格由规则查询产生 |
| Duration | ThisRound | 持续期类型；具体起止轮次在效果实例中 |
| Effect | FriendlyNonHeavyDefeatPrevention | 施加的持续效果类型，连接效果定义 |
| HeroTarget | None | 英雄目标选择条件；None表示无需另选英雄 |
| TargetMoveKeepsDistance | False | 移动目标时是否保持与来源的距离 |
| TextMoveDistance | 0 | 牌文移动的距离参数；实际预算由对应步骤决定 |
| TextMoveMinimum | 0 | 需要按牌文最短距离筛选路线时的下限 |
| TextPushDistance | 0 | 牌文推动的距离参数 |

**规则约束**：effect_source_recovery_or_defeat_cancels_unless_exception；next_turn_does_not_cross_round

## brogan-10 · 吟游诗人

如果你或攻击距离内的一个友方英雄与敌方单位相邻，则该友方英雄可以拿回一张已结算或已丢弃的卡牌。

**有序步骤**：ChooseHeroTarget → OptionalRecoverDiscard → End

| 参数 | 值 | 含义 |
|---|---|---|
| HeroTarget | AlliedNearEnemy | 英雄目标选择条件；None表示无需另选英雄 |
| RecoverResolved | True | 取回是否还允许已结算区，而非仅弃牌区 |
| RecoveryRequiresAdjacentMinion | False | 取回步骤是否要求与小兵相邻 |
| TargetMoveKeepsDistance | False | 移动目标时是否保持与来源的距离 |
| TextMoveDistance | 0 | 牌文移动的距离参数；实际预算由对应步骤决定 |
| TextMoveMinimum | 0 | 需要按牌文最短距离筛选路线时的下限 |
| TextPushDistance | 0 | 牌文推动的距离参数 |

**规则约束**：recovery_card_chosen_by_recipient；next_turn_does_not_cross_round

## brogan-11 · 巩固防线

本轮：如果技能范围内的一个友方小兵将被击败，你可以丢弃一张卡牌。若如此做，该小兵不会被击败。

**有序步骤**：ApplyEffect → End

| 参数 | 值 | 含义 |
|---|---|---|
| AreaKind | SkillRange | 效果区域的计算方式，实际覆盖格由规则查询产生 |
| Duration | ThisRound | 持续期类型；具体起止轮次在效果实例中 |
| Effect | FriendlyMinionDefeatPrevention | 施加的持续效果类型，连接效果定义 |
| HeroTarget | None | 英雄目标选择条件；None表示无需另选英雄 |
| TargetMoveKeepsDistance | False | 移动目标时是否保持与来源的距离 |
| TextMoveDistance | 0 | 牌文移动的距离参数；实际预算由对应步骤决定 |
| TextMoveMinimum | 0 | 需要按牌文最短距离筛选路线时的下限 |
| TextPushDistance | 0 | 牌文推动的距离参数 |

**规则约束**：effect_source_recovery_or_defeat_cancels_unless_exception；next_turn_does_not_cross_round

## brogan-12 · 一人成军

在小兵战斗中，你视为2个小兵。如果在小兵战斗中你将被移除，则本次推线失败而不会被移除。

**有序步骤**：MinionBattleParticipant

| 参数 | 值 | 含义 |
|---|---|---|
| BasicAttackBonus | 0 | 紫卡对基础攻击的常驻加成 |
| BasicAttackRangeBonus | 0 | 紫卡对基础攻击距离的常驻加成 |
| BattleMinionCount | 2 | 紫卡在兵战中计作的小兵数量 |
| TraverseObstacles | False | 紫卡是否允许穿越障碍，终点仍须合法 |
| Trigger | MinionBattleParticipant | 紫卡触发时点和流程类别 |

**规则约束**：battle_hero_any_region_contributes_two_removal_stops_push；next_turn_does_not_cross_round

## brogan-13 · 冲拳

选择一项：移除与你相邻的一个标志物；或将与你相邻的一个敌方小兵推动最多2格。

**有序步骤**：ChooseUnitPush → End

| 参数 | 值 | 含义 |
|---|---|---|
| FixedPushDistance | False | 推动距离固定还是可选择上限以内距离 |
| HeroTarget | None | 英雄目标选择条件；None表示无需另选英雄 |
| OptionalPushTarget | False | 该推动选择是否允许不选目标 |
| PushTarget | EnemyMinion | 选择推动目标时允许的单位类型 |
| TargetMoveKeepsDistance | False | 移动目标时是否保持与来源的距离 |
| TextMoveDistance | 0 | 牌文移动的距离参数；实际预算由对应步骤决定 |
| TextMoveMinimum | 0 | 需要按牌文最短距离筛选路线时的下限 |
| TextPushDistance | 2 | 牌文推动的距离参数 |

**规则约束**：next_turn_does_not_cross_round

**现有未裁定/未扩展边界**：D050_map_markers_not_in_current_six_hero_content

## brogan-14 · 碾压重拳

移动最多1格，然后选择一项：移除与你相邻的一个标志物；或将与你相邻的一个敌方小兵推动最多2格。

**有序步骤**：OptionalTextMove → ChooseUnitPush → End

| 参数 | 值 | 含义 |
|---|---|---|
| FixedPushDistance | False | 推动距离固定还是可选择上限以内距离 |
| HeroTarget | None | 英雄目标选择条件；None表示无需另选英雄 |
| OptionalPushTarget | False | 该推动选择是否允许不选目标 |
| PushTarget | EnemyMinion | 选择推动目标时允许的单位类型 |
| TargetMoveKeepsDistance | False | 移动目标时是否保持与来源的距离 |
| TextMoveDistance | 1 | 牌文移动的距离参数；实际预算由对应步骤决定 |
| TextMoveMinimum | 1 | 需要按牌文最短距离筛选路线时的下限 |
| TextPushDistance | 2 | 牌文推动的距离参数 |

**规则约束**：next_turn_does_not_cross_round

**现有未裁定/未扩展边界**：D050_map_markers_not_in_current_six_hero_content

## brogan-15 · 盾牌猛击

与你相邻的一个敌方英雄，如果在此回合打出过攻击卡，该英雄丢弃一张卡牌（如果可行）。

**有序步骤**：ChooseHeroTarget → TargetDiscardIfAble → End

| 参数 | 值 | 含义 |
|---|---|---|
| HeroTarget | AdjacentEnemyUsedAttack | 英雄目标选择条件；None表示无需另选英雄 |
| TargetMoveKeepsDistance | False | 移动目标时是否保持与来源的距离 |
| TextMoveDistance | 0 | 牌文移动的距离参数；实际预算由对应步骤决定 |
| TextMoveMinimum | 0 | 需要按牌文最短距离筛选路线时的下限 |
| TextPushDistance | 0 | 牌文推动的距离参数 |

**规则约束**：next_turn_does_not_cross_round

## brogan-16 · 野蛮飞踢

移动最多1格，然后选择一项：移除与你相邻的一个标志物；或将与你相邻的一个敌方单位推动最多2格。

**有序步骤**：OptionalTextMove → ChooseUnitPush → End

| 参数 | 值 | 含义 |
|---|---|---|
| FixedPushDistance | False | 推动距离固定还是可选择上限以内距离 |
| HeroTarget | None | 英雄目标选择条件；None表示无需另选英雄 |
| OptionalPushTarget | False | 该推动选择是否允许不选目标 |
| PushTarget | EnemyUnit | 选择推动目标时允许的单位类型 |
| TargetMoveKeepsDistance | False | 移动目标时是否保持与来源的距离 |
| TextMoveDistance | 1 | 牌文移动的距离参数；实际预算由对应步骤决定 |
| TextMoveMinimum | 1 | 需要按牌文最短距离筛选路线时的下限 |
| TextPushDistance | 2 | 牌文推动的距离参数 |

**规则约束**：next_turn_does_not_cross_round

**现有未裁定/未扩展边界**：D050_map_markers_not_in_current_six_hero_content

## brogan-17 · 防守反击

与你相邻的一个敌方英雄，如果在此回合打出过攻击卡，需丢弃一张卡牌，否则被击败。

**有序步骤**：ChooseHeroTarget → TargetDiscardOrDefeat → End

| 参数 | 值 | 含义 |
|---|---|---|
| HeroTarget | AdjacentEnemyUsedAttack | 英雄目标选择条件；None表示无需另选英雄 |
| TargetMoveKeepsDistance | False | 移动目标时是否保持与来源的距离 |
| TextMoveDistance | 0 | 牌文移动的距离参数；实际预算由对应步骤决定 |
| TextMoveMinimum | 0 | 需要按牌文最短距离筛选路线时的下限 |
| TextPushDistance | 0 | 牌文推动的距离参数 |

**规则约束**：next_turn_does_not_cross_round
