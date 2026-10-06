# 新手教程证据 · 2026-10-06

玩法实现：`faa5d77`。最终 Windows Player：`b37615c`（只调整验收脚本等待陪练揭牌的时机），Unity 6000.3.23f1，engine98。本目录的文档、截图和录像后续提交不改变运行源码。

## 已通过

| 文件 | 来源及结论 |
|---|---|
| core13.xml | Unity TutorialTests，13/13；十章真实命令、可重放、非法操作、独立进度等。玩法源码 faa5d77 |
| ui51.xml | Unity 技能环、主流程、镜头相关51/51；源码 faa5d77 |
| editor-1280.json | 1280×720 Editor，全十章223项；源码 faa5d77 |
| player-1600.json | 1600×1000独立 Windows Player，全十章220项；构建 b37615c |
| build-info.json | 最终 Player 自带的源码和158个载荷文件清单 |
| verify-build.json | 录屏临时源码清理后，再次核对 Player 与当前运行源码：PASS |
| capture-check.json、八张 PNG | 最终独立 Player 的真实渲染截图 |

渲染验收通过 UI 对象、合成事件与真实预选／确认回调走课程，**不是 OS 键鼠测试**。223和220的差别来自异步陪练等待循环的检查次数，不表示少测章节。未重跑全量卡牌、网络测试。

`earlier/` 保留修复前失败报告，不作为当前通过证据。故障原因见上级《新手教程实装验收》文档。

## 演示录像

[88秒教程实机渲染片段](tutorial-demo.mp4)：连续展示读牌、移动预选、撤回、实际移动；随后明确切换到独立防御教学局面，敌人攻击、本人防御，最后解释五色手牌条。不是十章完整通关录像。

录像采用 **Unity Editor 内部逐帧采集、脚本驱动真实 UI／规则、静音**，不是独立 Player 的 OS 操作录屏。运行源码 b37615c，临时附加录像脚本来自 `tools/ui3d/GameScreen.TutorialDemo.cs.txt`；脚本由 `record-tutorial-demo.ps1` 安装，录完自动移除，未进入发行构建。原始1280×720、30fps、88.2秒，经H.264压缩以便手机查看。结果与时长见 `video-result.txt`、`video-timing.txt`。

此前 GDI 录屏读到重复旧帧和桌面通知，已排除，不发布。截图取自独立 Player，录像取自 Editor，不能混称同一来源。

## 复现与边界

```powershell
./tools/test-unity.ps1 -Graphics -Assemblies 'Goa2.Core.Tests' -Filter 'Goa2.Tests.TutorialTests' -ReportName tutorial
./tools/ui3d/audit.ps1 -Width 1280 -Height 720 -TutorialOnly
./tools/build-unity.ps1
./tools/ui3d/audit.ps1 -Width 1600 -Height 1000 -TutorialOnly -Player
python tools/player_package.py verify-build artifacts/player --source-root .
./tools/ui3d/record-tutorial-demo.ps1
```

Unity测试、构建、录屏串行执行，录屏时不可同时构建该项目。报告所在原始目录：`artifacts/ui3d/audit-1280x720-20261006-141717`、`audit-1600x1000-20261006-142556`；录屏源目录 `artifacts/videos/tutorial-20261006-143104`。

桌面 `Goa2V1/13-新手教程` 是本次可玩入口，14为说明。旧联机ZIP与桌面01/02没有重打，不包含本次教程更新。未做配音；8—10分钟为设计目标，零基础真人耗时、理解和美术认可仍待用户验收。

`sha256.json` 记录本目录文件校验值（不含自身）。文本统一UTF-8无BOM、LF后生成，避免Git换行转换导致下载后校验不同。
