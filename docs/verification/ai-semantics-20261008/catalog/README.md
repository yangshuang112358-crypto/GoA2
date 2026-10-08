# 当前卡牌语义目录

由C#公开查询导出，供人工核对。中文名称和牌文只用于阅读，不进入模型。模型使用稳定ID、类型化参数、有序步骤、效果定义及约束代码。

卡牌 108；步骤定义 57；效果定义 24。

[参数中文含义](parameters.csv) · [步骤表](steps.csv) · [持续效果表](effects.csv)

参数只在对应步骤/分支执行时适用；不存在的分支不代表拥有该能力。false、0与未提供参数不同。
步骤是描述，不在Python执行。未知牌文、机制字段、事件或词表项必须报错；卡牌名称相近不代表规则可复用。

- [arien 的18张卡牌](arien.md)
- [brogan 的18张卡牌](brogan.md)
- [sabina 的18张卡牌](sabina.md)
- [shargatha 的18张卡牌](shargatha.md)
- [tigerclaw 的18张卡牌](tigerclaw.md)
- [wasp 的18张卡牌](wasp.md)
