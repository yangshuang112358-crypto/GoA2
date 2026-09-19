# Unity 编辑器场景执行

在Goa2V1根目录PowerShell执行：

```powershell
./tools/run-editor-scenarios.ps1 -Scenario tests/scenarios/tidal-place.json,tests/scenarios/tidal-boundary.json
```

使用已安装并授权的Unity 6000.3.23f1，在无窗口批处理模式运行相同的ScenarioRunner和正式内容。可用`-UnityExe`指定编辑器路径；不要与同一工程的构建或EditMode测试同时启动。此入口不加载游戏画面，不宣称Player或鼠标键盘验收。

报告位于`artifacts/editor-scenarios/场景名/唯一目录/`，保存逐步骤断言、原始存档、输入哈希及实际Unity程序集哈希，runner明确为Unity Editor batch。每次运行创建新目录，不覆盖旧报告；只允许写入本工程artifacts/editor-scenarios。批次输入和日志在batches子目录。

进程退出0表示全部通过，1表示至少一个断言失败，2表示输入或运行错误；PowerShell入口在任何非零退出码或未完成报告时抛出错误。输入大小上限2 MiB，场景仍沿用核心严格字段校验。

该入口适用于.NET运行受环境限制、但Unity编辑器可运行时。它是独立的编辑器自动化路径，不更改系统应用控制设置。可视化场景仍使用原Player工具，系统阻止Player时保持待验收状态。

2026-09-20实际验收：5场景77步通过；另验证断言失败退出1、未知字段退出2、拒绝覆盖退出2且原报告哈希不变。证据 artifacts/editor-scenarios/smoke-d233d49abdbd4c70af4e085c1823b5e9/verification.json。
