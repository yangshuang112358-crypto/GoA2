# 引擎5真实存档取证

2026-09-10 08:07:24—08:07:27 UTC，由提交c7c86fb对应的原生Windows Player执行`engine5-headshot-scenario.json`前11条命令获得。捕获时源码正在开发引擎6，但Player尚未重建，实际存档InitialEngineVersion和EngineVersion均为5。

停在一枪爆头的待防御窗口：相邻远程攻击，目标本回合已放弃闪耀之刃，牌文+2，最终攻击6。旧存档没有CardTextReason、CardTextSourceUnits字段。不得以新引擎重造此历史证据。

- 存档SHA-256：`d420379814df1214d08d76380ecf990ee65e002b9fdbfeda021201615161a83e`
- 输入SHA-256：`9bb3500fa37abfec7b2f35c6b6e1ab1612d4ed45a34130271a0169c06248f1c0`
- 原Player Goa2.Rules.dll SHA-256：`be7648160f0133aa0da59803b26b0743e8fa416f232ca541df6cde582ed7f119`

LegacySaveTests核对原始版本、旧字段缺失、归一化全文重放一致、继续数值防御、下一回合显式升级引擎后开放近战攻击，同时InitialEngineVersion保持5。
