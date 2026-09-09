# 卡牌结构化草稿审阅备注

已生成 `cards_ocr_draft.json`。这是给人工纠错用的结构化草稿，暂时不直接导入游戏逻辑。

## 当前字段

- `card_name`：卡名。
- `hero`：所属角色。
- `color` / `level`：颜色与等级。金、银卡的 `level` 为 `null`；紫卡为 `4`；红绿蓝升级卡为 `1`、`2`、`3`。
- `initiative.value`：左上角先攻值。
- `secondary_actions.movement`：次要移动槽。
- `secondary_actions.defense`：次要防御槽。
- `primary_action.category`：主要行动类型，例如攻击、技能、移动、防御、终极技能等。
- `primary_action.text`：主要行动描述。
- `primary_action.value`：主要行动数值，例如攻击值、移动值、技能数值。
- `primary_action.subtype`：主要行动右下角图标。没有图标时为 `null`；有图标时为 `{ "type": "远程", "value": 3 }` 或 `{ "type": "范围", "value": 2 }`。
- `passive_bonus.type`：底部被动加成类型。加成值默认都是 1，因此不单独写数值。

## 次要行动填写方式

每张卡固定有两个次要行动槽：

```json
"secondary_actions": {
  "movement": {
    "type": "移动",
    "has_action": false,
    "value": null
  },
  "defense": {
    "type": "防御",
    "has_action": false,
    "value": null
  }
}
```

如果卡牌左侧有鞋子图标：

- `secondary_actions.movement.has_action` 改为 `true`
- `secondary_actions.movement.value` 填成移动值

如果卡牌左侧有盾牌图标：

- `secondary_actions.defense.has_action` 改为 `true`
- `secondary_actions.defense.value` 填成防御值

如果没有对应图标，保留 `has_action: false` 和 `value: null`。

## 主要行动右下角图标填写方式

如果没有远程或范围图标：

```json
"subtype": null
```

如果是远程图标：

```json
"subtype": {
  "type": "远程",
  "value": null
}
```

如果是范围图标：

```json
"subtype": {
  "type": "范围",
  "value": null
}
```

其中 `value` 填图标对应的远程距离或范围数值。

## 需要重点核对

1. `initiative.value`：左上角先攻值。
2. `secondary_actions.movement` / `secondary_actions.defense`：左侧鞋子、盾牌图标及数值。
3. `primary_action.value`：攻击值、移动值、技能数值等图标值。
4. `primary_action.subtype`：右下角是空、远程、还是范围，以及对应数值。
5. `passive_bonus.type`：底部被动图标类型。
6. 部分 OCR 识别不到卡名，仍需按图片修正。

建议先纠四个优先实现的角色：黄蜂、夏尔加萨、布罗根、艾瑞恩。每次可以只改一个角色，我再把 JSON 迁移进游戏卡牌逻辑。
