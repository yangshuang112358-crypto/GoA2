# Goa2V1

2026-09-26：传奇决斗家引擎66完成，77/108张，31张data_only，96手工起点。8专项，127项C#回归、32 Python、Editor 4场景69步、Windows构建/包校验通过。免疫其他敌人的所有行动，含防御附带弃牌；复活保留、友方取回取消与普通决斗家的差异已验证。沿用上一张的持久化机制，只增加精确版本绑定。 实际Player/UI未重新验收。

2026-09-26：决斗家引擎65完成，76/108张，32张data_only，95手工起点。11专项，181项C#回归、32 Python、Editor 3场景52步、Windows构建/包校验通过。其他敌人攻击免疫；防御失败、同回合复活保留，取回和回合末仍取消。相关免疫、防御、反击、紫卡及模拟回归通过；本卡未重复全量不相关场景。 实际Player/UI未重新验收。

2026-09-26：至死不渝引擎64完成，75/108张，33张data_only，94手工起点。17专项，1636项C#回归、32 Python、Editor 3场景48步、Windows构建/包校验通过。实际取回才授予攻击免疫；攻击附带效果、友方攻击、技能区别和来源取回均已验证。完整回归两处场景选牌错误已修复并补测，原失败报告保留。 实际Player/UI未重新验收。

2026-09-26：反击引擎63完成，74/108张，34张data_only，93手工起点。18专项、完整1616项C#、32 Python、Editor四场景85步、Windows构建/包校验通过。每张弃牌独立累计，整次致弃攻击结束再执行；嵌套、重复、幻化前置、出生等待和胜利均有专项。实际Player/UI未重新验收。

2026-09-26：防守反击引擎62完成，73/108张，35张data_only，91手工起点。12专项、完整1593项C#、32 Python、Editor四场景58步、Windows构建/包校验通过。目标本人可弃牌或主动被击败；复用支付原语，旧盾牌猛击保持不变。实际Player/UI未重新验收。

2026-09-26最新：巩固防线引擎61完成，72/108张（六张紫卡）、90手工起点。19专项、1577完整C#、32 Python、五Editor场景149步、Windows构建和包校验通过。重型保护与潮汐之主移除的区别已测试。实际Player/UI未验收，实验分支逐卡提交；永久engine34基线不变。

2026-09-26最新：潮汐之主引擎60完成，71/108张（六张紫卡均已有规则实现）、88手工起点。18专项、1551完整C#、32 Python、七Editor场景181步、Windows构建和发行包校验通过。D-043仅阿连位于当前交战区触发；与一人成军的轮中兵战联动已验证。继续巩固防线等已明确普通牌；实际Player/UI未验收。

2026-09-26最新：一人成军引擎59完成，70/108张（5张紫卡）、85手工起点。17专项、1524完整C#、32 Python、五Editor场景115步、Windows构建和发行包校验通过。下一张潮汐之主，D-043已答仅阿连位于当前交战区可触发。实际Player/UI未验收。

2026-09-26最新：斗篷与匕首引擎58完成，69/108张（4张紫卡）、83手工起点。18专项、1501完整C#、32 Python、六Editor场景217步、Windows构建和发行包校验通过。下一张一人成军（D-039/D-042已答）；潮汐之主仅Q-13待答。实际Player/UI未验收，历史应用控制限制未重新尝试。

2026-09-26：幻化（引擎57）规则实现通过22项专项、1476项完整C#回归；共68/108张、81个手工起点。最新构建与场景证据见[紫卡开发记录](docs/verification/紫卡开发记录-2026-09-26.md)。D-040已确认可选择空手邻敌。下一张斗篷与匕首；Player实际画面和输入仍未验收。

2026-09-20实验进度与逐卡证据见[夜间记录](docs/verification/夜间卡牌工作记录-2026-09-20.md)。Unity能构建，但Windows应用程序控制阻止新Player启动，新增原生运行/UI待验收。永久稳定基线仍是 `baseline/2026-09-20-engine34`。取金币三牌的后移语义待Q-09复核，当前72张代码实现中这3张尚未完成规则验收。

2026-09-20当前总览：[项目基线盘点与联机路线](docs/development/项目现状与联机路线-2026-09-13.md)；[全部可执行程序与指令](docs/development/可执行程序与命令总表.md)。引擎61，72/108牌效，1577项C#、32项Python通过；本批核心及构建证据见[夜间逐卡记录](docs/verification/夜间卡牌工作记录-2026-09-20.md)，新Player运行/UI仍被系统应用控制阻止；真正联网尚未实现。

GoA2电子版重启工程。已建立Unity与Windows本地客户端，具备四席热座、放大的可折叠面板、地图缩放、出牌/弃牌圆点、手工调试和场景播放。正式资料保留6英雄、108张牌、254格地图；可在游戏图鉴或[离线资料浏览器](docs/development/离线资料浏览器.md)中检索。

最新界面调整：手牌、揭示牌、升级候选直接显示描述预览，悬停显示完整大卡面。区域正式名称与阅读方式见[界面组件名称与卡牌阅读](docs/development/界面组件名称与卡牌阅读.md)。

2026-09-12：[关键词方案](docs/design/卡牌关键词展示调研与建议.md)已接入：动作金色、时点青色、条件白色加粗、击败红色，统一覆盖卡牌描述；顶栏“术语”、详情“本牌术语”或悬停时F1查看解释，Esc关闭，复制保持原文。关键词提交时415项C#与32项Python通过，108张卡两种窗口尺寸检查通过，见[BATCH-04工作记录](docs/verification/BATCH-04工作记录.md)。[调整清单](docs/planning/卡牌效果调整清单.md)中的近身还击现已完成；偷袭、忠实信徒已在本批新增。队伍条已移到卡牌底边，空牌位不显示色条。

当前引擎61接入72张牌：29张主要攻击、11张防御、3张主要移动、23张技能、6张紫卡（电闪雷鸣、重型枪械、幻化、斗篷与匕首、一人成军、潮汐之主），近身还击已支持本人有牌也选择被击败，其他强制弃牌不变。完整1577项C#通过；调试区可搜索90个局面。本轮新Player被系统应用控制阻止，未进行真实输入验证；早先21项反制输入为引擎10历史证据。常规击败/复活、兵线推进、轮末回收/移兵、连续升级、补偿与下一轮均可操作。剩余36张牌、完整触发联动及联网仍待开发，详见[剩余卡牌路线](docs/planning/剩余卡牌开发路线.md)与[BATCH-04工作记录](docs/verification/BATCH-04工作记录.md)。

BATCH-03新增8级紫卡选择确认、测试攻击、绿色数值加成、26像素字号、三色六候选、六行图鉴和按轮记录。当时共享核心Unity405项、Python32项及30份场景448步通过；本批实际窗口与最终证据见[BATCH-03验收](docs/verification/BATCH-03验收.md)。[当前功能与测试说明](docs/development/当前功能与测试说明.md)列出65张牌、非卡牌缺口、下一步及手工/自动操作。

BATCH-02原394项核心、两种小窗口各238项界面和2321条长模拟命令仍保留为历史证据。此前本机应用控制限制.NET及Player启动；2026-09-12复验两者已能正常运行，新的证据以BATCH-04记录为准。

立即体验：运行 artifacts/player/Goa2V1.exe。操作与开发入口见[启动与操作指南](docs/development/启动与操作指南.md)，最新程序包与验证结果见[项目现状](docs/development/项目现状与联机路线-2026-09-13.md)，范围见[BATCH-03](docs/planning/BATCH-03.md)。

上一批完整交付入口：artifacts/delivery/Goa2V1-BATCH02-20260910/开始使用.md，保留当时的Windows包、源码与规则资料、离线浏览器和带哈希的验收证据。当前版本以本页总览、2026-09-20夜间逐卡记录及本仓库为准，9月13日盘点保留为历史阶段记录；从源码重建时按下方开发运行步骤生成本机产物。

## 阅读入口

1. [项目复盘](docs/history/项目复盘.md)：版本经历、16项教训与回归要求。
2. [规则手册](docs/rules/规则手册.md)：本项目采用的完整规则基线。
3. [裁定记录](docs/rules/裁定记录.md)、[待确认规则](docs/rules/待确认规则.md)、[临时简化](docs/rules/临时简化登记.md)。
4. [数据字典](docs/data/数据字典.md)、[地图与部件](docs/data/地图与部件.md)、[卡牌机制索引](docs/data/卡牌机制索引.json)。
5. [架构](docs/architecture/架构说明.md)、[开发流程](docs/development/开发流程.md)、[测试矩阵](tests/测试矩阵.md)。
6. [里程碑](docs/planning/里程碑与任务.md)、[本批范围](docs/planning/BATCH-03.md)、[本批验收](docs/verification/BATCH-03验收.md)与[上一批工作记录](docs/verification/BATCH-02工作记录.md)。

## 内容清单

| 内容 | 数量/状态 |
|---|---|
| 英雄 | 6 |
| 卡牌 | 108；每英雄18；72张implemented、36张data_only，未宣称全部行为矩阵/集成完成 |
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
本地结果见[验收报告](docs/verification/启动包验收.md)。GitHub Actions已启用，远程结果见[内容与核心检查](https://github.com/yangshuang112358-crypto/GoA2/actions/workflows/validate.yml)。

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

无需Unity也可运行实际核心场景：`./tools/run-core-scenarios.ps1 -Scenario tests/scenarios/closesupport-basic.json`；[执行方式与证据边界](docs/development/无Unity核心场景执行.md)。此入口不替代Unity画面验收。
在Hub中添加unity文件夹；首次打开后使用菜单Goa2/Prepare project建立场景并同步内容，再进入Play。
构建成功后运行artifacts/player/Goa2V1.exe；构建与存档不提交Git。

当前可操作范围：四席选英雄/出生 → 暗选/揭示 → 动态先攻与已支持行动 → 四回合后的轮末 → 回收与小兵战斗 → 多角色升级 → 下一轮。默认测试模式四人选完立即揭示；调试面板可关闭此模式，改为四人逐一确认。轮末点击“结算轮末”推进，须选择时交给对应队长或升级角色。
按1/2/3/4或点击左侧立即切换角色，空格执行当前确认。地图支持滚轮缩放、中/右键拖动、Home全图；四周区域可折叠。地图点击只产生预选，确认后才提交规则命令。拔枪/快速突刺可执行主要攻击，防御者切至自己席位用手牌响应；复活先选出生点再继续原牌。静电封锁/打断施法可执行基础技能，右侧显示来源、到期和地图范围按钮。升级时三色六张候选按三行同时列出，确认后获得同色未选牌的永久图标；左侧展示永久数值，8级还须选择并确认紫卡，之后常亮紫圈可悬浮查看文字，紫卡效果仍待实装。旧存档在全员未选牌的暗选阶段可显式采用当前规则，保留原命令历史。

卡牌原文与地图无损迁移，重复显示/实现字段已分离。旧工作树和房间存档保持原状。2026-09-12按用户要求将Goa2V1作为[原GitHub仓库](https://github.com/yangshuang112358-crypto/GoA2)的根目录内容，原项目提交历史通过合并保留。构建产物、Unity缓存和本机存档由`.gitignore`排除；源码、正式资料、文档和测试均由本仓库维护。
本手册以用户项目裁定为依据，历史上传的英文规则PDF与Summary仍缺失；另取得的第二版参考PDF尚未完成全文对照。局部卡牌语义、标志物、极端出生占用与部分联动有明确待确认条目。

引擎54运行边界：更新后的.NET程序集也被Windows应用控制阻止，改用Unity EditMode与[编辑器场景入口](docs/development/Unity编辑器场景执行.md)获得证据；不表示Player或物理输入通过。
