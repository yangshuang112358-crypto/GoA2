> 2026-09-28 已在日常集成分支接入 engine97 + Unity，提供本机/私网IPv4启动入口。新说明：[联机试运行与验收](../docs/development/联机试运行与验收-2026-09-28.md)。下文NET-01为历史阶段记录。

# NET-01 联网切片

状态：真实服务与四客户端、整轮与等待恢复已实现；**NET-01 尚未整体验收完成**。Unity 接线待主任务协调，四个实际 Unity 窗口未验收。ADR-005 原文保持 proposed。

本次证据与N01—N16逐项状态见[阶段验收](docs/NET-01阶段验收.md)：12次真实网络运行、265个检查、8个客户端单测、4个核心会话测试，分别计数。

基线 `d0808967b1754a38f858aabab97b9891e99b349a`（engine 66），工作分支 `feature/network`。工作树在主仓库之外：`C:/Users/29383/Documents/StudyFile/personal_project/Goa2V1-network`。仅增加/修改 network 内文件，未带入未提交卡牌改动，未修改 GameScreen、核心、牌数据、存档格式、主工作区或永久回退标签。

## 运行

在本工作树根目录的 PowerShell 中：

```powershell
# 构建 + Python 状态机测试 + 真实服务/四进程网络测试 + 独立计数的核心会话测试
./network/run-tests.ps1 -IncludeCore

# 手动启动独立服务；输出目录必须尚不存在
$netDotnet = "$env:LOCALAPPDATA/Goa2V1Toolchain/dotnet/dotnet.exe"
& $netDotnet network/Goa2.Network/bin/Release/net10.0/Goa2.Network.dll serve . artifacts/network/manual-001
```

服务仅监听 `127.0.0.1` 随机空闲 TCP 端口；`ready.json` 给出端口/PID。服务控制台输入 `stop` 正常退出，生成仅供服务所有者使用的 `authority.private.save.json`，并调用既有 Restore 检查。它是退出时验收导出，不是逐命令持久化，也不能作为客户端重连令牌。

每个玩家只取得自己的 `seat-N.private.json`（N=0..3），不得分发整个输出目录。以下客户端是开发 RPC 控制台，不是游戏 UI：

```powershell
& $netDotnet network/tests/Goa2.Network.ClientHarness/bin/Release/net10.0/Goa2.Network.ClientHarness.dll artifacts/network/manual-001/seat-0.private.json artifacts/network/manual-client-0.jsonl
```

标准输入依次发送 JSON 行（玩家2—4各启动同一程序并用各自凭证）：

```json
{"op":"connect"}
{"op":"view"}
{"op":"submit","kind":"ChooseHero","args":{"Value":"wasp"}}
{"op":"disconnect"}
{"op":"connect"}
```

核心候选来自 `view`，如队长按 `Deployments` 提交 `DeployHero`；`SelectCard` 后每席必须 `ConfirmCard`。客户端无认证席位 setter，命令无 ActorSeat。具体字段见 [协议](docs/协议.md)。关闭控制台会结束该客户端，不终止服务。

## 文件与边界

- `Goa2.Network/`：.NET 10 服务入口、单房间权威、连接代次、严格协议。正常入口只创建 Sandbox=false、QuickSelection=false 新局。
- `com.goa2.network.client/`：拟供 Unity 使用的纯 C# 包，`IPlayerSession`、`NetworkPlayerSession`；只依赖 Domain/JSON，没有 GameSession 或规则副本。尚未接入现有界面。
- `Goa2.Network.Client/`：同源 netstandard2.1 编译工程。
- `client/player.py`：独立 Python 参考客户端，无规则实现。
- `tests/`：四进程验收、局部连接故障代理、服务端夹具 Host、C# 客户端控制台。夹具创建使用已有 ScenarioRunner；生产入口没有 Debug/导入场景/导入存档消息。
- `docs/`：接口提案、协议、验收与主任务交接、后续外网方案。

所有 bin/obj、测试日志、凭证和存档输出在本工作树；没有启动或复用主工作区 Unity/Library/Temp。验收脚本只关闭它自己创建的子进程，保留失败运行目录。

## 已知限制

服务存活期间可重连；无服务崩溃恢复、Host 迁移、凭证撤销/账号、房间发现、观战。当前每服务进程一个四席房间。网络尚限回环明文 TCP，不可直接开放公网。升级引擎、Debug 和切换快速选牌均不属于网络 Intent 白名单。

当前没有空闲心跳；无FIN/RST的网络黑洞可能在下一次提交的15秒等待或底层TCP检测后才显示断线。外网阶段需要增加活性检测并测量超时体验，不能把已测的主动断开等同于所有网络故障。

完整投影含历史事件；8 MiB 回包上限不截断合法信息，超过即断开。已测本批候选/整轮规模，未做长局极限与全部卡牌组合证明。慢连接队列满或写超时会断开，续连取当前投影；不会伪称本地点击已经成功。

基线 .NET 构建存在两处 CS8602，服务测试构建只将该编号保留为 warning；C# 客户端单独以警告即错误构建。核心测试的既有源码还有其他 nullable 警告，`-IncludeCore` 用 `TreatWarningsAsErrors=false` 编译，日志保留；没有修核心或掩盖测试失败。
