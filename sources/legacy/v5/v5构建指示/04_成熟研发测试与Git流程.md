# 成熟研发、测试与 Git 流程

## 1. 开始任何功能前

1. 阅读最新规则和风险登记。
2. 查看 Git 状态，不覆盖用户改动。
3. 明确本步只解决一个行为切片。
4. 写验收场景和非法场景。
5. 判断是否需要用户规则裁定。
6. 创建或更新 Issue/任务卡。
7. 决定执行 Agent 和审查 Agent。

任务卡必须包含：

- 用户价值；
- 输入状态；
- 玩家 Intent；
- 期望事件；
- 期望最终状态；
- 非法命令和原子性；
- 公共/私有信息；
- 自动测试；
- Unity 手工检查；
- 不在本步范围内的内容。

## 2. 分支策略

初期推荐 trunk-based：

- `main`：始终可打开、可测试、可演示。
- 短生命周期功能分支：`feature/<issue>-<name>`。
- 修复分支：`fix/<issue>-<name>`。
- 文档分支：`docs/<issue>-<name>`。

一个分支只做一个紧密相关任务，尽量在 1 至 2 天内合并。
禁止长期大分支和“全部完成后一次合并”。

## 3. 开发循环

1. 添加失败测试。
2. 确认失败原因正是缺失行为。
3. 写最小实现。
4. 运行聚焦测试。
5. 运行相关模块测试。
6. 运行全量 Edit Mode tests。
7. 涉及 Unity 展示则运行 Play Mode tests。
8. 涉及联网则运行四实例集成测试。
9. 检查序列化、重连和非法命令原子性。
10. 更新文档、状态表和风险。
11. Code Review。
12. 合并 main 后再次运行 CI。

## 4. 测试分层

### 每次提交

- 编译；
- 格式和静态分析；
- Domain/Rules Edit Mode tests；
- Card schema validation；
- 数据唯一性和引用完整性；
- Git diff 检查。

### Pull Request

- 全量 Edit Mode；
- 相关 Play Mode；
- 序列化 round-trip；
- 公共/私有信息测试；
- Invalid Command atomicity；
- Golden replay；
- 最小构建 smoke test。

### 联网改动

- Host + 4 Client；
- 每个 token/ClientId 只控制自己的席位；
- 下一行动者获得合法操作；
- 非行动者不能提交行动；
- 重连恢复私有手牌和 Pending Choice；
- revision 冲突；
- 延迟、丢包和重复消息；
- Host 退出策略；
- 不泄漏其他玩家手牌。

### 发布候选

- Windows Player 全流程；
- 长局 soak test；
- 存档升级；
- 回放一致性；
- 性能和内存；
- 崩溃日志；
- 四人真实网络验收；
- 全部已声明卡牌测试状态核验。

## 5. Commit 规则

Commit 应小而完整：

- `feat(rules): add fast move region validation`
- `fix(multiplayer): project legal actions by player identity`
- `test(cards): cover mandatory discard with empty hand`
- `docs(rules): record round-four expiry ruling`

提交前必须：

- 测试通过；
- 不包含 Library、Temp、Logs、Build、用户设置和密钥；
- 不包含个人绝对路径；
- 不提交大型生成资产，除非项目明确需要；
- 更新受影响文档；
- `git diff --check` 通过。

不要提交：

- `Library/`
- `Temp/`
- `Obj/`
- `Logs/`
- `Build/`、`Builds/`
- IDE 用户文件；
- 本地账号或服务凭据；
- Unity 自动生成但不应版本化的缓存。

必须提交：

- `Assets/`
- `Packages/manifest.json`
- `Packages/packages-lock.json`
- `ProjectSettings/`
- 对应 `.meta` 文件；
- 测试和文档。

## 6. Pull Request 模板

PR 描述应包含：

- 目标；
- 行为变化；
- 规则依据；
- 修改文件/程序集；
- 自动测试结果；
- Unity 手工验收；
- 公共/私有信息影响；
- 存档和联网兼容性；
- 未完成和风险；
- 截图或短视频，仅在视觉变化时需要。

## 7. Code Review 检查

优先找问题：

- 是否违反最新规则；
- 是否把规则写进 MonoBehaviour/UI/网络；
- 是否按 card ID 污染主流程；
- 是否存在部分修改；
- 是否丢失 effect source；
- 是否无法序列化 Pending Choice；
- 是否泄漏私有信息；
- 是否只测 happy path；
- 是否把“实现”误标为“集成测试完成”；
- 是否改变存档格式而无迁移。

## 8. CI 建议

CI 阶段：

1. Validate repository；
2. Unity compile；
3. Edit Mode tests；
4. Play Mode tests；
5. Build Windows test player；
6. Multiplayer smoke；
7. Artifact：测试报告、日志、构建；
8. main 分支创建可演示构建。

初期可以先由本机脚本执行，流程稳定后接 GitHub Actions 或 Unity DevOps。
凭据只存 CI Secret，不进入仓库。

## 9. 版本与发布

- 规则版本、内容版本、协议版本、客户端版本分开记录。
- 保存和重放必须记录规则版本与内容版本。
- 破坏兼容的修改必须提供迁移或明确拒绝旧存档。
- 每个里程碑打 annotated tag，例如 `v5.0.0-m1`。
- 发布前从 main 创建 release branch，只允许阻断性修复。

## 10. 何时上传 Git

必须上传：

- Unity 空工程可打开且测试基线通过；
- 每个里程碑验收完成；
- 每个规则 primitive 完成；
- 每组卡牌行为测试完成；
- 联网垂直切片完成；
- 重要文档裁定完成。

不要上传：

- 无法编译；
- 测试红；
- 只改了一半且会破坏 main；
- 含凭据、个人路径、缓存或巨大无关文件；
- 规则尚未确认却被当成正式实现。

## 11. Bug 流程

1. 记录最小复现。
2. 保存初始 GameState、Command、revision、规则/内容版本。
3. 先添加失败回归测试。
4. 修复最小根因。
5. 运行相关交互矩阵。
6. 若是规则遗漏，先更新规则文档。
7. 合并后在 Bug 记录中链接测试名和 commit。

