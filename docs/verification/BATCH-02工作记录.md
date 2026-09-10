# BATCH-02 工作记录

2026-09-10 10:07开始，预计工作至约22:07（Asia/Shanghai）。起点commit 71f0268；31项核心和11项资料工具测试为上一批基线。

当前阶段：记录最新交互裁定，实施界面、测试模式与调试命令。具体范围见[BATCH-02](../planning/BATCH-02.md)。本文件仅在取得证据后标记完成。

## 第一阶段：测试对局核心

新增6项SandboxTests，先在Release运行中证明调试命令/自动揭示尚未支持，再实施；当前完整核心37/37通过，结果artifacts/tests/core.trx。首次Debug构建被本机应用控制拒绝加载测试DLL，记录为环境失败；之后使用项目正常Release配置，没有修改系统安全设置。

已实现新局固定Sandbox标志、选完自动揭示/正式四确认切换、加减金币、合法格传送、弃牌/取回、自动选英雄与出生、第一张/最高先攻/可重放随机快选、同色测试装配、决策币。调试意图仍经过身份/版本/原子命令检查并写入回放；普通对局拒绝调试命令。

公开视图仅暴露弃牌颜色；本轮四回合出牌历史由揭示事件构成，卡牌之后离开已出区也不删除历史。原有正式确认存档已在新版Player实际读取成功。

资料校验仍为6英雄、108牌、254格、44障碍；11/11资料工具测试通过，正式内容和来源哈希保持不变。尚未提升任何牌效实现状态。

## 第二阶段：界面与真实输入验收（10:49）

UI-01至UI-06、DEBUG-02已实现。四周扩大并独立折叠、无交接屏幕、1—4快捷键、字体放大、按鼠标位置缩放/拖动/Home全图、四回合颜色槽与独立弃牌圆点、顶部揭示牌跨选牌阶段保留。滚动位置随刷新保留；1280×800下默认聚焦可辨认的战区，允许折叠面板看大地图。

tools/test-player-ui.ps1在1600×1000和1280×800各完成19项检查；输入全部通过Win32鼠标/键盘发送给真实前台Player，没有直接注入权威状态。报告artifacts/unity/ui-smoke-1600x1000.json与ui-smoke-1280x800.json，附两组discard/complete截图。覆盖四人快揭示、四边折叠、鼠标锚点缩放、拖动后切角色、数字编辑不切角色、缩放后点格/传送、弃牌/取回、下一回合公开牌保留。

Unity BuildWindows成功，实际Player无异常日志。新增run-player.ps1的QA模式将存档隔离到artifacts目录，保留正常用户存档；-goaLoad恢复指定文件。

仍在继续本批后续开发；上述检查不代表主要牌效或完整对局已经完成。

## 第三阶段：流程快进与Unity同源测试

新增DebugConfirmAll、DebugAdvance(action/turn/round)、DebugSetGold，以及5项边界测试。真实窗口点击“快速到轮末”后，修订2即完成自动准备与整轮快进，记录16次揭示、16次放弃后结算，停在RoundEnd；截图batch02-debug-roundend.png，实际存档qa-save.json。

新增规则最初4项失败均为unsupported_command；实现后.NET测试宿主被Windows Smart App Control拒绝加载Goa2.Rules.dll（0x800711C7）。同一二进制重试仍被拒，已核实系统CodeIntegrity事件；未修改安全策略，也没有把环境失败当作行为失败或通过。

已将核心测试移到UPM包Tests/Editor，.NET工程引用同样源码，接入编辑器自带Test Framework 1.6.0。Unity实际42/42通过；其中“快进不能丢弃无关强制选择”先取得0/1失败，再增加拒绝边界并通过。结果artifacts/unity/tests/core-editmode.xml。新增test-core.ps1的显式运行器选择与仅环境错误时的Auto切换。

Player重新构建与运行成功；测试/NUnit程序集没有进入发行Player。场景无窗口/可见执行仍在实施中。

随后在加入强制选择保护并重新编译后，.NET也实际42/42通过（最新core.trx）；此前两份被阻止的报告仍保留。说明拦截并非每次构建都会发生，不能因此抹去环境问题或宣称安全策略已改变。

## 第四阶段：无窗口与可见场景

QA-01已实现。ScenarioRunner由Unity无窗口Player和可见Player共用；9步沙盒、7步普通对局权限、23步正式确认场景均通过无窗口执行。可见沙盒已实测暂停起步、单步、阻止手工命令干扰、继续播放；正式确认场景也在窗口通过。两者分别与无窗口最终状态哈希完全一致。

- sandbox-smoke：40e5a974939ba7f1e2485a5a33b991b4e90d5233bbafd6c52fad2c590aa0b10b。
- formal-turn：b1854c814e65f3095aeda47072b220d218d7878ec748b95f291c0d697325e587。
- permissions：e473acdbb6593686006feb2aa13350c0664ec10e91fc58e0640fef8858f32c95。

每步都校验结果并从保存的命令重放继续；拒绝和重复命令必须保持状态哈希。输入字段、整数/布尔类型、坐标完整性、重复键和大小写错误严格拒绝。新增场景测试先取得5项未实现失败；标量/坐标严格校验也先取得真实失败再修复。当前Unity完整50/50通过；.NET在加入三份正式场景前的47/47已通过。

故意错误场景只完成准备一步，拒绝执行后续999金币命令；报告为失败，原生Player退出码1，修订1、金币0。该夹具保留在tests/fixtures，排除默认通过集。

可见截图：batch02-scenario-paused.png、batch02-scenario-complete.png、batch02-formal-scenario-complete.png。运行报告、最终状态、输入和程序集哈希均在artifacts/scenarios。图形展示不冒充真实鼠标/键盘测试。
