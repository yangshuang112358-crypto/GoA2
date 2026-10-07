# 2026-10-08 原子输入与关系模型原件

- `baseline/`：41001换边两场simple-v1对random-v1，源提交10dc1ce0；权威存档/命令和当时可见策略输入分文件保存。
- `validation/`：源提交da58fae5的输入/前向/反向/检查点验证，包含公开目录、33响应输入、初始与两次更新检查点、资源记录和逐项字典。`probe-host/`是明确截断的协议探针，不计为正式比赛。
- `response-inputs.jsonl`外层的Window只是C#验收分类标签；网络只读取内层Decision，不把内部续行标签作为特征。
- `validation/diagram/`：1800×3420手机流程图和SVG，代码生成并查看排版；不是游戏截图。
- `unity/`：322命令Unity原生无画面重放，与C#终局哈希一致；不是新模型的比赛录像。
- `tests/`：C#12项TRX、公共字段覆盖清单；Python17项原始日志在`validation/python-tests.log`。
- `failures/`：开发中严格编码失败的输入/目录和旧版本断言失败。它们来自格式构造期间，不能把这些旧输入直接当作最终观察4数据训练。

`sha256.json`核对本目录其他文件的原始字节，避免换行规范化改动原始证据。没有新模型强度提升、跨规则泛化或信息完全性的结论；`complete_information=false`。
