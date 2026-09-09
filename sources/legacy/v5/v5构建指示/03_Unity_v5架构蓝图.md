# Unity v5 架构蓝图

## 1. 总体原则

GoA2 是规则复杂、交互窗口多、需要多人同步和回放的回合制游戏。
Unity 只负责承载客户端、表现和本地开发工具；规则核心必须保持纯 C#。

依赖方向：

`Presentation -> Application -> Rules Core <- Card Definitions`

`Networking -> Application`

`Persistence -> Application`

Rules Core 不得引用 UnityEngine、MonoBehaviour、GameObject、场景或网络 SDK。

## 2. 建议技术基线

- Unity 6.3 LTS，锁定所有成员相同 patch。
- URP。
- C# 与 Assembly Definition 分层。
- Unity Test Framework。
- Input System。
- Addressables 只在有明确资产加载需求时引入。
- 联网优先评估 Netcode for GameObjects + Multiplayer Services Sessions。
- 不采用 DOTS 作为第一版规则架构；四人回合制不需要其复杂度。

## 3. 推荐程序集

### GoA2.Domain

纯值对象和不可变数据：

- GameState、PlayerState、TeamState；
- HeroState、MinionState、BoardState；
- CardInstance、EffectInstance；
- Command、Event；
- PendingChoice；
- 枚举、ID、坐标、结果类型。

不得依赖 Unity。

### GoA2.Rules

- Command 校验；
- Reducer / State Transition；
- 合法行动查询；
- 六边形路径和距离；
- 回合、先攻、战斗、轮末、升级；
- Trigger、Replacement、Continuous Effect；
- 确定性随机接口。

不得依赖 Unity、网络和 UI。

### GoA2.Cards

- Card Definition；
- Effect Schema；
- Schema validator；
- 通用原子效果；
- 少量 Custom Effect Handler；
- 卡牌实现状态清单。

不得把 card ID 分支写入主回合流程。

### GoA2.Application

- Use Case；
- 创建/加入房间；
- 提交玩家 Intent；
- 权限和当前玩家校验；
- 公共/私有 ViewModel；
- Snapshot、Event Log、Replay；
- 错误码和 revision。

### GoA2.Networking

- Unity Multiplayer Sessions；
- Netcode 适配；
- 消息 DTO；
- Host/Dedicated Server 入口；
- 重连、超时、版本握手；
- 网络模拟工具。

联网层不计算规则，只传输 Intent、Result、Snapshot 和 Event。

### GoA2.Presentation

- 场景、Prefab、UI Toolkit 或 uGUI；
- 地图渲染、选择高亮、动画、音效；
- 公共区、私有手牌、队长选择、升级选择；
- 调试面板。

只显示 Application 返回的合法目标和合法按钮。

### GoA2.Infrastructure

- JSON/二进制存档；
- 日志；
- 配置；
- 内容加载；
- 时间与随机实现；
- 平台服务适配。

### GoA2.Tests

- Domain/Rules Edit Mode tests；
- Card contract tests；
- Application integration tests；
- Play Mode UI tests；
- Multiplayer integration tests；
- Replay/golden tests；
- Build smoke tests。

## 4. 权威状态模型

GameState 必须包含或可推导：

- match ID、规则版本、内容版本、revision；
- phase、round、turn；
- 四席、队伍、队长、英雄；
- 地图单位、小兵、战区、战线标记、水晶；
- 手牌、已选、已打出、已弃置；
- 先攻队列、已结算项；
- Pending Action / Attack / Defense / Choice / Respawn / Upgrade；
- 决策币；
- 活跃效果及来源；
- 胜者；
- 确定性随机状态。

序列化后必须能完整恢复，不依赖场景中隐藏状态。

## 5. Command/Event 模型

客户端只提交玩家意图，例如：

- SelectCard；
- ConfirmSelection；
- ChooseActionSlot；
- ChooseTarget；
- ConfirmChoice；
- CancelChoice；
- ChooseDefenseCard；
- ChooseInitiativeOrder；
- ChooseUpgrade；
- DebugCommand。

Rules Core：

1. 校验身份、phase、revision 和合法性；
2. 原子执行；
3. 返回新状态与领域事件；
4. 非法命令不产生部分修改。

事件示例：

- CardsRevealed；
- ActionChosen；
- UnitMoved；
- AttackDeclared；
- DefenseResolved；
- UnitDefeated；
- FrontlineAdvanced；
- UpgradeChosen；
- EffectAdded / EffectExpired；
- MatchEnded。

## 6. Effect Engine

效果分四类：

- Instant Effect：立即执行。
- Triggered Effect：监听事件后入栈或排队。
- Continuous Effect：持续修改查询或数值。
- Replacement Effect：替换、防止或重定向事件。

每个效果至少保存：

- effect ID；
- source card / source unit；
- controller；
- creation sequence；
- duration / expiry；
- priority；
- conditions；
- payload；
- active state。

事件窗口：

- before；
- replace / prevent；
- resolve；
- after；
- cleanup。

第一版不追求通用脚本语言。先用可验证的结构化 Schema 覆盖常用原子效果，
复杂牌再使用受约束的 Custom Handler。

## 7. 多人权威方案

开发顺序：

1. 纯规则本地测试；
2. 单进程四席模拟；
3. Host 权威四客户端；
4. Relay/Sessions；
5. 再评估 Dedicated Server。

任何模式都必须满足：

- 客户端不能直接修改 GameState；
- 玩家只能看到自己的手牌和私有选择；
- Host/Server 校验所有 Intent；
- 支持 snapshot + revision；
- Pending Choice 可重连；
- 客户端动画不能阻塞权威结算；
- 网络断开不丢失已确认命令。

## 8. Unity 表现层

场景建议：

- Bootstrap；
- Main Menu / Session；
- Match；
- Test Sandbox。

Match UI 区域：

- 房间与调试；
- 四席状态；
- 公开行动；
- 行动按钮；
- 六边形战场；
- 当前玩家手牌；
- 事件日志；
- 卡牌目录/详情；
- Modal Choice Overlay。

地图视觉数据来自正式 Map Definition，运行时单位状态来自 GameState。
不得通过场景层级位置反推规则坐标。

## 9. 数据策略

- 正式卡牌和地图保留一个权威内容源。
- Unity ScriptableObject 可作为编辑器资产，但不能成为第二套冲突真相。
- 推荐从权威 JSON 导入生成只读资产，并校验内容 hash/version。
- 保存数据时引用稳定 ID，不保存 GameObject 引用。
- 内容变更必须升级 content version 并运行 catalog tests。

## 10. 决策记录

重要架构变化创建 ADR，至少包含：

- 背景；
- 选择；
- 备选方案；
- 原因；
- 后果；
- 回滚方案。

需要 ADR 的事项包括联网框架、UI 框架、序列化格式、随机策略、
Effect Schema 版本、Dedicated Server 和存档兼容策略。

