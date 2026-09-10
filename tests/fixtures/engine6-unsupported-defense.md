# 引擎6真实存档取证

2026-09-10 08:27:45—08:27:49 UTC，由提交01587c2对应的原生Windows Player执行`engine6-unsupported-defense-scenario.json`，14步中13条命令接受，近身还击响应按当时未实装规则拒绝。捕获时源码已在开发引擎7，Player仍为6，存档InitialEngineVersion与EngineVersion均经检查为6。

黄蜂以闪耀之刃攻击虎爪；虎爪唯一剩余手牌是近身还击，属于当时未实装防御，所以旧程序保留待防御，不能擅自无防御击败。新实现恢复后也须保留此历史能力边界。

- 存档SHA-256：`354d15dbe0e250e2619fef05a5f29274aa64674ea99ef4612c7273ec33b5b473`
- 输入SHA-256：`99c299d0ebba28bccace9a70a16b25bdbf3f371bab47a32d81f6212597af8a2b`
- 原Player Goa2.Rules.dll SHA-256：`a73f807bbe5052c4d3232aa21eef96c1e47e21a21574d9880b8b4f544d67c749`

LegacySaveTests核对归一化全文重放、唯一未实装防御、拒绝采用新反制，再显式不防御并继续原攻击。不得以新引擎重造此旧版本夹具。
