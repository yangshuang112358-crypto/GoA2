# 语义与网络接线证据

实现源码 `89deb474`；详细结论见[验收](../AI数据语义验收-2026-10-08.md)。

- `catalog/`：从C#目录生成的逐牌中文说明与参数、步骤、效果表。
- `baseline/`：提交后两场固定种子换边正式基线；各场策略输入与观众事件分开。`commands.json`/存档供既有重放流程使用，本批只验证C#恢复/重放。
- `validation/`：最终目录、字段字典、真实响应、事件示例、影子推理、两次接线更新的初始/最终检查点、资源和源码清单。检查点无强度结论。
- `tests/`：C#18项、字段专项、427场景四席投影和Python20项结果。
- `development-failures.zip`：提交前失败日志/场景现场和两次内存保护停止。
- `artifact-index.json`：上述运行原件的大小和SHA256；不包含本说明及索引自身。

`validation/baseline-summary.json`是提交前第一次相同种子运行，实际最终速度以`baseline/summary.json`为准。两组策略输入解压后完全相同，见`baseline/trace-equivalence.json`，避免把影子推理冒称模型自主对局。
