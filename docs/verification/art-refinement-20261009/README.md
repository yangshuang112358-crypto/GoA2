# 本批美术证据

运行源码：`edc1bc1386f5560eee81f015ececac499622bc4f`，Unity 6000.3.23f1，engine98。后续仅补交文档与证据的提交不会改变此 Player 源码。完整结论见[实装记录](../美术精修实装-2026-10-09.md)。

## 图片和视频是什么

- `art-preview.mp4`：实际 Unity Editor 连续渲染，约 58 秒，1280×800 H.264，静音，脚本控制运镜与 UI 事件。展示四英雄、岩石、小兵准备姿态、技能环与英雄常驻特效；不是鼠标录制、不是性能跑分，也没有展示全部六英雄动作。
- `skill-wheel.png`、`hero-arien.png`、`hero-tigerclaw.png`、`rocks.png`：同一份最终录制中的实际游戏画面。源目录 `artifacts/videos/art-refinement-20261009-033127`。
- `action-slabs.png`：实际 Editor 行动链审计，源目录 `artifacts/ui3d/audit-1600x1000-20261009-032339`。
- `heroes-studio.png`、`minions-studio.png`：Blender 棚拍，仅用于模型造型检查，不等同游戏光照与动作。
- `player-skill-wheel.png`、`player-rocks.png`、`player-draft.png`：最终构建的独立 Windows Player 截图，分别来自 033959、034037、034131 的 1280×720 审计目录。

## 检查范围

- `unity-68.xml`：68 项通过；是受影响模块的专项集合，不是全部工程测试。
- `editor-world-decisions.json`：132 项；行动、确认、防御、自由观看与升级。
- `editor-action-sequence.json`：202 项；行动链真实内容及嵌套/排序/滚轮。
- `editor-poses.json`：19 项；相关姿态/静态平台/等级预告/镜头保持预选。
- `player-skills.json`：103 项。
- `player-terrain.json`：10 项。
- `player-opening.json`：37 项。
- `build-info.json`：Windows Player 的 158 载荷文件与完整运行源文件哈希清单；构建于 UTC 2026-10-08 19:34:24。原构建与独立预览副本分别通过本地工具 verify-build 的同源验证。

这些是程序断言、脚本事件与实际渲染证据，**不是 OS 键鼠操作、四台真人联网测试、用户美术认可、所有姿态无穿模或全场景帧率保证**。

## 待继续

当前六英雄仍为风格化程序建模的第一轮精修；不是最终高模、柔性角色绑定、面部动画与专属动作完成品。常驻特效已有水流/暗影/电弧，全部技能效果并未制作。完整未完成项在[质量清单](../../planning/美术精修审计与验收-2026-10-09.md)。
