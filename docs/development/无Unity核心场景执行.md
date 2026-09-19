# 无 Unity 的核心场景执行

在 Goa2V1 根目录 PowerShell 执行：

```powershell
./tools/run-core-scenarios.ps1 -Scenario tests/scenarios/closesupport-basic.json
./tools/run-core-scenarios.ps1
```

第一条执行指定场景；第二条执行全部 tests/scenarios 场景。可通过 `-DotnetExe` 指定 .NET 10 SDK。默认先使用本机 Goa2V1Toolchain，再尝试 PATH 中的 dotnet。

此入口构建纯 C# 核心和命令行适配器，直接使用与 Unity 相同的 ScenarioRunner、正式内容和命令规则，不需要 Unity 授权。报告放在 artifacts/core-scenarios/场景名/唯一运行目录，包含步骤断言、保存恢复检查、原始存档、输入哈希和实际运行程序集哈希。报告标注 Dotnet CLI；不能当作 Unity Player、画面或鼠标键盘验证。

退出码：0 表示所有场景断言通过；1 表示场景断言失败；2 表示输入、内容、路径等运行错误。PowerShell 入口遇到任一失败即停止。底层 CLI 拒绝向已存在的输出目录写入，避免覆盖之前的证据。

2026-09-20 验证：Release 构建零警告、零错误；电击六个完整交互场景与一个弃牌窗口场景通过。另实际验证成功、断言失败、未知字段、缺失参数和拒绝覆盖五种调用结果，报告 artifacts/tests/cli-smoke-8342b6a608db4442a79ae6cf468e5ddd/verification.json。

可视化播放继续使用 `tools/run-scenarios.ps1 -Visual -Paused -KeepOpen`，它需要已构建的 Unity Player。两种入口分别保存证据。
