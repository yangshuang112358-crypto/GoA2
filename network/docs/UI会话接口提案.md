# UI 会话接口提案（待主任务协调，未接线）

基线 `d0808967b1754a38f858aabab97b9891e99b349a`，分支 `feature/network`。
工作树 `C:/Users/29383/Documents/StudyFile/personal_project/Goa2V1-network`。
所有网络源码、测试、交接位于 network；运行输出位于本工作树 artifacts/network。
Unity 如需运行仅使用本工作树 unity/Library、Temp、Builds，不复用主工作区。

建议 `IPlayerSession`：

```csharp
int? AuthenticatedSeat { get; } // 连接认证后固定；没有 setter
GameView? View { get; }         // 仅本人投影；UI不得修改
ConnectionState Connection { get; }
event Action StateUpdated;     // 新投影或连接变化；UI线程派发由适配器处理
Task<IntentResult> SubmitAsync(PlayerIntent intent); // 无 ActorSeat
Task<IntentResult> RetryAsync(string commandId);    // 原消息原ID原revision
Task ReconnectAsync();
```

提交时冻结 MatchId、ExpectedRevision、CommandId 和参数。未收到 Result 的命令保留；重连后可原样重试，明确 stale 后新选择生成新ID。断线禁用提交；重连清除本地预选。同revision只更新连接/结果，不重播动画。较旧结果仍完成对应命令，但不能回退视图。连接代次隔离旧读循环。

网络实例不暴露 GameSession、存档、其他席位视图查询、身份 setter。热座快速选牌保留独立适配；网络必须四人确认；数字键只可查看公开信息。网络不提供 Debug、SetQuickSelection、UpgradeEngine 操作。

本提案未与其他任务交换确认，不修改 GameScreen*.cs。正式 UI 接线及四个 Unity 窗口验收待协调，不能用命令行四进程冒充。合法候选全部来自服务 GameSession.View，规则只经 Execute。
