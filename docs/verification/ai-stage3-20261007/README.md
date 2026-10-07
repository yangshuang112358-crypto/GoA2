# 教学短试证据

详细结论见[教学预热验收](../AI教学预热验收-2026-10-07.md)。训练源码 a1b45d1；环境实现 e16db0b。观察3/动作1/编码器2，默认7生命/3标记，engine98。

- `teaching-v3-01`：1208行允许策略输入与教师标签（policy.jsonl.gz）、规则合同、来源分组和计数。完整场景原件单独存于authority，编号对应Source，Prefix为场景步骤编号。导出开始于实现提交前，代码随后提交为e16db0b；合同记录实际程序集哈希。sources.json保留当时artifact路径；相同原始正式命令现已纳入ai/teaching，当前可重建来源表为ai/teaching-sources.json。
- `curriculum-v3-01`：三个完整检查点与各自评测旁文件、源码/配置/数据哈希、公开目录、预热损失、教学成绩、固定局面行为、PPO更新、正式评测、资源采样与测速。authority内14局各自保存策略输入、公共观众事件、命令、最终存档、内容、配置和结果。三截断也完整保留。
- `curriculum-replay`：最终PPO模型11002蓝方的335命令正式败局，C#恢复与场景核验通过。scenario.json直接供原有Unity机制使用。
- `unity-curriculum-replay`：原生Player报告、最终保存、来源/载荷核对及日志；passed=true、visual=false。
- `ai-stage3.trx`、`curriculum-v3-01/python-tests.txt`：9项C#与8项Python通过。

`.gz`文件解压回同名无.gz后即可供现有工具读取；已有policy/spectator.jsonl.gz本来就是压缩流，不要改成普通JSON。policy与观众/权威资料仍分目录，训练器不读取后者。文档约27.5MiB，较大原始未压缩日志也保留于本工作树artifacts/ai-training。采集脚本tools/collect-ai-stage3-evidence.py默认拒绝覆盖已有证据目录。

sha256.json覆盖218份采集原件（不含本说明与清单本身），逐字校验。没有把教学命中提升或一次训练胜局当作整体棋力提升；三个正式评测分别1胜2负1截断、0胜3负1截断、0胜3负1截断。全部流程已结束，未开长训。
