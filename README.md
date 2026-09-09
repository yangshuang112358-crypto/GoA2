# Goa2V1

GoA2 电子版重启工程。步骤1—6资料基线和[BATCH-01](docs/planning/BATCH-01.md)已完成：Unity已安装，Windows程序可运行，四人热座基础流程通过实际操作验收。纯C#核心31项测试通过；正式数据为6英雄、108牌、254格。主要牌效、完整轮末/升级与联网仍未实现。

立即体验：运行 artifacts/player/Goa2V1.exe。操作与开发入口见[启动与操作指南](docs/development/启动与操作指南.md)，验证范围见[本批验收](docs/verification/BATCH-01进展.md)。

## 阅读入口

1. [项目复盘](docs/history/项目复盘.md)：版本经历、16项教训与回归要求。
2. [规则手册](docs/rules/规则手册.md)：本项目采用的完整规则基线。
3. [裁定记录](docs/rules/裁定记录.md)、[待确认规则](docs/rules/待确认规则.md)、[临时简化](docs/rules/临时简化登记.md)。
4. [数据字典](docs/data/数据字典.md)、[地图与部件](docs/data/地图与部件.md)、[卡牌机制索引](docs/data/卡牌机制索引.json)。
5. [架构](docs/architecture/架构说明.md)、[开发流程](docs/development/开发流程.md)、[测试矩阵](tests/测试矩阵.md)。
6. [里程碑](docs/planning/里程碑与任务.md)、[本批范围](docs/planning/BATCH-01.md)、[验收报告](docs/verification/BATCH-01进展.md)与[下一批计划](docs/planning/下一批开发计划.md)。

## 内容清单

| 内容 | 数量/状态 |
|---|---|
| 英雄 | 6 |
| 卡牌 | 108；每英雄18；全为data_only |
| 地图 | 254格、44障碍、8种区域标识 |
| 现存照片 / OCR文字 | 24张 / 12份 |
| Git恢复照片 | 1张，单独保留 |
| 可读卡牌目录 / 规格准备稿 | 6份 / 108份，未宣称已定合同 |
| 历史记录 | 指导50轮、规则51轮、架构21轮，均读取至末页 |
| v4旧版测试 | 本次复跑135项通过，不能当新版牌效证据 |

英雄目录：[黄蜂](docs/data/cards/wasp.md)、[夏尔加萨](docs/data/cards/shargatha.md)、[布罗根](docs/data/cards/brogan.md)、[艾瑞恩](docs/data/cards/arien.md)、[虎爪](docs/data/cards/tigerclaw.md)、[萨彼娜](docs/data/cards/sabina.md)。

## 独立校验

需要Python 3.12或更新版本，无第三方包。当前Windows默认python入口可能只是商店别名，可通过validate.ps1的PythonExe参数指定可用解释器。本机实际路径不写入项目。

在仓库根目录运行：

~~~powershell
python -B tools/validate.py
python -B -m unittest discover -s tests -v
~~~

或者使用 ./tools/validate.ps1 -PythonExe '可用的python.exe完整路径'。

工具校验JSON结构、ID/引用、升级树、地图出生点、原文迁移一致、来源和内容哈希、文档链接/历史引用以及生成视图。不启动旧服务器，不执行sources中的历史代码。
内容变更后用 python -B tools/generate_views.py 更新可读视图；工具只写生成目录，不生成正式卡牌合同。
本地结果见[验收报告](docs/verification/启动包验收.md)。GitHub Actions配置已提供，远程CI尚未运行。

## 目录与权威

- content/canonical：唯一正式内容源；content/status：实现状态；content/schemas：结构约束。
- sources：旧资料、图片、OCR、v4源码/测试证据、Git补丁、脱敏历史。仅查证，旧Prompt不是当前开发指令。
- docs：规则、经验、设计、任务与流程；docs/cards/drafts为自动生成分析输入。
- tools / tests：资料校验、共享核心NUnit测试、内容打包与Unity构建脚本。
- core/com.goa2.core：纯C#运行时唯一源码；四个.NET工程引用同一份文件。
- unity：Unity 6000.3.23f1工程，UI Toolkit地图/热座/卡牌界面与Editor构建入口。

## 开发运行

需要global.json指定的.NET SDK及Unity 6000.3.23f1；版本依据见[ADR-002](docs/architecture/decisions/ADR-002-工具链与基础切片.md)。在仓库根目录执行：

~~~powershell
./tools/test-core.ps1
python -B tools/prepare_unity.py
./tools/build-unity.ps1 -Task BuildWindows
~~~

test-core.ps1支持-DotnetExe；build-unity.ps1支持-UnityExe指定本机编辑器。Unity许可须通过官方Hub正常激活。
在Hub中添加unity文件夹；首次打开后使用菜单Goa2/Prepare project建立场景并同步内容，再进入Play。
构建成功后运行artifacts/player/Goa2V1.exe；构建与存档不提交Git。

当前可操作范围：四席各选英雄 → 队长在地图安排出生 → 暗选/改选/确认 → 自动翻牌 → 动态先攻 → 次要移动、快速移动或放弃 → 四回合后的轮末边界。
地图点击只产生预选，确认后才提交规则命令；取消不改变权威状态。切换席位自动遮挡手牌，点击显示后继续。主要行动文字和轮末结算未实施时会明确显示，不能当作完整对局。

卡牌原文与地图无损迁移，重复显示/实现字段已分离。旧工作树和房间存档保持原状；没有配置远程仓库或上传。
本手册以用户项目裁定为依据，原英文规则PDF当前缺失。局部卡牌语义、标志物、极端出生占用与部分联动有明确待确认条目。
