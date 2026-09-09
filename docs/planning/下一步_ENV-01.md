# ENV-01：Unity环境核验与工程版本锁定

本批资料基线后的第一项开发任务；尚未执行。

目标：建立一个可编译、可测试、可构建的空Unity工程及纯C#程序集边界。
输入：README、AGENTS、规则 R-ENGINE、架构说明、内容manifest、开发流程。
首先只读检查已安装Unity Hub/Editor、C# SDK、构建模块和许可证可用性。查阅当时官方资料后选择兼容版本，记录确切Editor patch与包版本；不得照抄旧v5的6.3 LTS建议当已经安装。

允许改动：unity/、core/、tests/运行时测试目录、构建脚本、版本ADR、环境说明、CI必要配置。
建立 Domain / Rules / Application / Presentation 的最小边界；Rules不引用UnityEngine。首个真实测试验证状态序列化和非法命令不改状态，避免只有永远true的示例。
明确Unity工程如何引用pure C#核心，不产生两份手改规则源码。

自动验收：编译、必要测试、资料校验继续通过。
手工/实际验收：Editor打开无错误；Player构建可启动；给出构建路径与日志。
完成报告：实际版本、安装/依赖、修改范围、测试/构建结果、尚缺部分、本地commit。
暂不实现地图交互、卡牌或联网服务；不因SDK未在PATH就认定机器未安装，须检查常用安装位置。
