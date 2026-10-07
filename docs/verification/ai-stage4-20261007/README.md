# 防御微调证据与视频

[完整验收](../AI防御微调验收-2026-10-07.md) · [启动](../../development/AI防御对比微调-2026-10-07.md)

训练源码accf623，观察3/行动1/编码器2、engine98、默认7生命/3标记。223份采集原件约21.15MiB，sha256.json覆盖原件（不含本说明和清单本身）。已逐字核验。

- defense-data-01：45个既有场景的限定攻防窗口、分组清单、58条策略可见输入、117标签验证计数；完整源场景另在authority。
- model-visited-data-01：模型实际1701换边训练轨迹提取的503行、909标签；不使用上批正式评测轨迹。与旧教学合并后1255训练/514留出，跨来源组与哈希隔离。
- defense-finetune-01：父/微调后检查点、真实源码及父权重/数据哈希、教学与正式评测、300批学习记录、资源/测速、10项Python日志；authority内保留全部8场正式对局的策略输入、公共观众事件、权威记录、内容和结果。
- defense-replay、unity-defense-replay：513命令正式模型胜局的C#及Unity原生报告与最终存档；7场败局仍在完整评测中。
- ai-stage4.trx：9项C#专项通过。
- defense-video：[微调前](defense-video/parent.mp4)、[微调后](defense-video/defense.mp4)，每段约17秒。44/43真实窗口帧按实际时间合成、低帧率、无声，附教学字幕；不是生成的游戏画面，不是正式整局强度证明。

视频在同一个pike-defense前缀的真实模型本人观察上选择响应。parent选择防御3对攻击5导致虎爪被击败；defense选择躲闪抵挡并存活。模型选择的输入与导出分支输入逐字段一致；两个15步Unity图形重放均通过，最终hash与各自C#相同。video-verification.json、inference、request、分支场景/权威存档、图形报告及抽帧检查保留。原始PNG序列（约2.6fps）和第一次失败的静态GDI捕获留在本工作树artifacts/ai-training/defense-video，不重复入库。frames.txt需这些本地原始帧才能重新编码；已交付MP4可直接播放。

`.gz`为无损压缩，命令、存档或教学输入解压去除.gz即可读取；原本的policy/spectator.jsonl.gz仍由gzip reader读取。策略与观众/权威资料分开，模型不读后者。

留出防御12/16→16/16；正式新种子0胜4负→1胜3负，8场全完局0非法/异常。四局不是稳定棋力证明。未启动长训，录制/重放进程已关闭，GameScreen未修改。
