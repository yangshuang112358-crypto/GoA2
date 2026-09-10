# Goa2V1

GoA2 电子版重启工程。[BATCH-02](docs/planning/BATCH-02.md)正在持续开发：Unity与Windows程序已可运行，测试模式、四边折叠界面和地图缩放已接入。拔枪、快速突刺、闪耀之刃、三张防御及静电封锁/打断施法可操作，支持击败/复活、兵线推进、轮末回收/移兵、连续升级、补偿和下一轮。共享核心Unity与.NET各158项通过；真实窗口已验收基础界面、战场、战斗、升级、轮末衔接、持续效果与来源取消；十一份场景可无窗口运行或可见播放。正式数据为6英雄、108牌、254格。其余牌效、持续免疫及完整联动、紫卡文字与联网仍未实现，当前不宣称全部规则对局完成。

立即体验：运行 artifacts/player/Goa2V1.exe。操作与开发入口见[启动与操作指南](docs/development/启动与操作指南.md)，验证范围见[本批记录](docs/verification/BATCH-02工作记录.md)。

## 阅读入口

1. [项目复盘](docs/history/项目复盘.md)：版本经历、16项教训与回归要求。
2. [规则手册](docs/rules/规则手册.md)：本项目采用的完整规则基线。
3. [裁定记录](docs/rules/裁定记录.md)、[待确认规则](docs/rules/待确认规则.md)、[临时简化](docs/rules/临时简化登记.md)。
4. [数据字典](docs/data/数据字典.md)、[地图与部件](docs/data/地图与部件.md)、[卡牌机制索引](docs/data/卡牌机制索引.json)。
5. [架构](docs/architecture/架构说明.md)、[开发流程](docs/development/开发流程.md)、[测试矩阵](tests/测试矩阵.md)。
6. [里程碑](docs/planning/里程碑与任务.md)、[本批范围](docs/planning/BATCH-02.md)、[当前工作记录](docs/verification/BATCH-02工作记录.md)与[上一批验收](docs/verification/BATCH-01进展.md)。

## 内容清单

| 内容 | 数量/状态 |
|---|---|
| 英雄 | 6 |
| 卡牌 | 108；每英雄18；8张implemented、100张data_only，未宣称全部行为矩阵/集成完成 |
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
- core/com.goa2.core/Tests/Editor：核心测试唯一源码，.NET和Unity引用；入口见[自动化测试](docs/development/自动化测试.md)。
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

当前可操作范围：四席选英雄/出生 → 暗选/揭示 → 动态先攻与已支持行动 → 四回合后的轮末 → 回收与小兵战斗 → 多角色升级 → 下一轮。默认测试模式四人选完立即揭示；调试面板可关闭此模式，改为四人逐一确认。轮末点击“结算轮末”推进，须选择时交给对应队长或升级角色。
按1/2/3/4或点击左侧立即切换角色。地图支持滚轮缩放、中/右键拖动、Home全图；四周区域可折叠。地图点击只产生预选，确认后才提交规则命令。拔枪/快速突刺可执行主要攻击，防御者切至自己席位用手牌响应；复活先选出生点再继续原牌。静电封锁/打断施法可执行基础技能，右侧显示来源、到期和地图范围按钮。升级时右侧选颜色、下方选两张候选之一，确认后获得未选牌的永久图标；左侧展示永久数值，8级紫卡独立展示，文字仍待实装。旧存档在全员未选牌的暗选阶段可显式采用当前规则，保留原命令历史。

卡牌原文与地图无损迁移，重复显示/实现字段已分离。旧工作树和房间存档保持原状；没有配置远程仓库或上传。
本手册以用户项目裁定为依据，原英文规则PDF当前缺失。局部卡牌语义、标志物、极端出生占用与部分联动有明确待确认条目。
