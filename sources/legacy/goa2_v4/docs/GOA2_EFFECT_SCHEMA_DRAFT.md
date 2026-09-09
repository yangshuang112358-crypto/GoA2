# GoA2 Effect Schema 草案

状态：设计草案，不代表所有卡牌已经转换。

## 1. Effect Schema 是什么

Effect Schema 是“卡牌效果的结构化语法”。

它把自然语言：

> 选择一个相邻英雄。可以再选择另一个相邻英雄，使其弃置一张牌，然后攻击第一个英雄。

拆成引擎可以逐步执行、暂停等待选择、验证目标和记录事件的数据。

Schema 不是规则代码本身，也不是直接把卡牌文本换成另一段文本。它定义：

- 什么时候触发；
- 每一步是什么元动作；
- 谁做选择；
- 合法目标是什么；
- 该步骤可选还是强制；
- 无合法目标时如何处理；
- 产生哪些事件；
- 效果持续到什么时候；
- 来源是哪张卡。

## 2. 为什么项目需要它

没有 Effect Schema 时，容易把每张牌写成：

```python
if card_id == "...":
    ...
elif card_id == "...":
    ...
```

这会让选择窗口、持续效果、替换效果和卡牌联动散落在主回合流程。

有 Schema 后：

- 普通卡牌可以只写数据；
- 相同元动作可以复用；
- 特殊卡只注册少量 custom handler；
- 测试可以按步骤检查；
- 服务端可以把“当前等待谁选择什么”序列化；
- 多人客户端可以只显示后端返回的合法选择。

## 3. 建议的顶层结构

```json
{
  "cardId": "wasp-01-电击",
  "version": 1,
  "primaryAction": {
    "family": "attack",
    "trigger": "on_primary_action",
    "steps": []
  },
  "defenseEffect": null,
  "continuousEffects": [],
  "customHandler": null
}
```

说明：

- `version`：Schema 版本，方便以后迁移。
- `trigger`：效果从哪个规则事件进入。
- `steps`：按顺序执行的元动作。
- `continuousEffects`：持续监听规则检查或事件的效果。
- `customHandler`：Schema 无法合理表达时使用的特殊代码入口。

## 4. 元动作通用字段

```json
{
  "id": "choose-attack-target",
  "type": "choose_target",
  "required": true,
  "chooser": "source_controller",
  "target": {},
  "onNoLegalTarget": "end_card",
  "storeAs": "attackTarget"
}
```

建议所有步骤支持：

- `id`：卡内唯一步骤 ID；
- `type`：元动作类型；
- `required`：强制或可选；
- `condition`：执行条件；
- `chooser`：由谁决定；
- `onNoLegalTarget`：无合法目标时结束卡牌、跳过本步或执行其它分支；
- `storeAs`：保存选择结果供后续步骤引用；
- `source`：默认继承卡牌来源；
- `tags`：攻击前、攻击后、本回合等语义标签。

## 5. 目标过滤器

```json
{
  "kinds": ["hero"],
  "teams": "any",
  "distance": {
    "type": "adjacent"
  },
  "exclude": [
    {"ref": "attackTarget"}
  ],
  "mustBeTargetableBy": "attack"
}
```

目标术语遵守规则说明书：

- `unit`：双方英雄和双方小兵；
- `hero`：双方英雄；
- `minion`：双方小兵；
- 没有指定阵营时，`teams` 默认为 `any`。

目标过滤必须由服务端执行。

## 6. 建议的基础元动作

- `choose_target`
- `choose_cell`
- `choose_card_from_hand`
- `confirm_choice`
- `move`
- `push`
- `place`
- `swap`
- `attack`
- `defend`
- `discard`
- `retrieve`
- `defeat`
- `gain_gold`
- `add_effect`
- `remove_effect`
- `repeat`
- `branch`
- `end_card`
- `custom`

每个元动作只处理一个目标。多个目标由多个步骤或 `repeat` 顺序执行。

## 7. 电击示例

以下只是结构示例，具体范围继续以正式卡牌文本和用户确认测试为准。

```json
{
  "cardId": "wasp-01-电击",
  "version": 1,
  "primaryAction": {
    "family": "attack",
    "trigger": "on_primary_action",
    "steps": [
      {
        "id": "choose-attack-target",
        "type": "choose_target",
        "required": true,
        "chooser": "source_controller",
        "target": {
          "kinds": ["unit"],
          "teams": "enemy",
          "distance": {"type": "adjacent"},
          "mustBeTargetableBy": "attack"
        },
        "onNoLegalTarget": "end_card",
        "storeAs": "attackTarget"
      },
      {
        "id": "choose-discard-target",
        "type": "choose_target",
        "required": false,
        "chooser": "source_controller",
        "target": {
          "kinds": ["hero"],
          "teams": "any",
          "distance": {"type": "adjacent"},
          "exclude": [{"ref": "attackTarget"}]
        },
        "storeAs": "discardTarget"
      },
      {
        "id": "discard-one",
        "type": "discard",
        "required": true,
        "condition": {"refExists": "discardTarget"},
        "chooser": {"controllerOf": "discardTarget"},
        "from": "hand",
        "count": 1,
        "ifEmpty": "continue"
      },
      {
        "id": "resolve-attack",
        "type": "attack",
        "required": true,
        "target": {"ref": "attackTarget"},
        "valueFrom": "card.primaryAction.value"
      }
    ]
  }
}
```

这个示例表达了：

- 攻击目标是强制选择；
- 没有攻击目标时结束卡牌；
- 额外弃牌目标是可选；
- 不能选择刚才的攻击目标；
- 弃哪张牌由目标英雄的控制者决定；
- 目标无牌时继续攻击；
- 弃牌完成后返回原攻击。

## 8. 持续效果示例

紫色卡全部是持续被动，可表示为：

```json
{
  "cardId": "hero-ultimate",
  "continuousEffects": [
    {
      "id": "ultimate-passive-1",
      "type": "rule_modifier",
      "source": "card",
      "duration": "while_owned",
      "listensTo": ["RuleCheckRequested"],
      "condition": {},
      "modify": {}
    }
  ]
}
```

持续效果必须保存：

- `source`：效果来源；
- `controller`：谁控制；
- `duration`：持续到何时；
- `createdAt`：产生时点；
- `priority`：同一窗口的处理顺序；
- `active`：当前是否有效。

具体 `condition` 和 `modify` 在实现该紫卡时逐张确认。

## 9. Custom Handler 使用边界

只有满足以下情况才使用 custom handler：

- 用通用步骤表达会比代码更复杂；
- 存在高度独特的循环或替换行为；
- Schema 尚未支持该机制；
- 性能或确定性要求确实需要代码。

即使使用 custom handler，也必须：

- 通过统一 Command/Event 入口；
- 返回可序列化的 pending choice；
- 记录来源；
- 不直接修改 UI；
- 有行为测试；
- 不把逻辑塞回主回合流程。

## 10. 实施顺序

1. 先实现纯数值攻击、移动、防御元动作。
2. 再实现目标选择、弃牌和取回。
3. 再实现推动、放置和交换。
4. 再实现持续、触发、替换和免疫。
5. 每完成一类元动作，迁移一组卡牌并增加行为测试。
6. 遇到歧义时先向用户提问，不由 Schema 设计者猜测。
