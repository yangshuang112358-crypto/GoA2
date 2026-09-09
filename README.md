# Goa2V1

GoA2 电子版重启工程。当前交付是步骤1—6的资料、规则、数据与研发基线；Unity工程、C#规则运行时、卡牌效果和联网尚未实现。

## 阅读入口

1. [项目复盘](docs/history/项目复盘.md)：版本经历、16项教训与回归要求。
2. [规则手册](docs/rules/规则手册.md)：本项目采用的完整规则基线。
3. [裁定记录](docs/rules/裁定记录.md)、[待确认规则](docs/rules/待确认规则.md)、[临时简化](docs/rules/临时简化登记.md)。
4. [数据字典](docs/data/数据字典.md)、[地图与部件](docs/data/地图与部件.md)、[卡牌机制索引](docs/data/卡牌机制索引.json)。
5. [架构](docs/architecture/架构说明.md)、[开发流程](docs/development/开发流程.md)、[测试矩阵](tests/测试矩阵.md)。
6. [里程碑](docs/planning/里程碑与任务.md)与[下一步 ENV-01](docs/planning/下一步_ENV-01.md)。

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
- tools / tests：资料校验工具与回归测试，不是游戏引擎。
- core / unity：在ENV-01及后续任务创建，本批没有空壳游戏程序集。

卡牌原文与地图无损迁移，重复显示/实现字段已分离。旧工作树和房间存档保持原状；没有配置远程仓库或上传。
本手册以用户项目裁定为依据，原英文规则PDF当前缺失。局部卡牌语义、标志物、极端出生占用与部分联动有明确待确认条目。
