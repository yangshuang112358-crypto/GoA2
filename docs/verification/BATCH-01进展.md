# BATCH-01 进展与证据

2026-09-10进行中。以下仅列已经执行的事实；最终Editor、Player和交互验收另行追加。

- Unity Hub 3.21.1.65535通过winget安装，官方MSIX哈希校验通过。用户已回复“已登录”。
- Unity 6000.3.23f1（09d2ecc7fb28）官方安装包4125667560字节下载完整，Authenticode为Valid、Unity Technologies SF签名、与官方API公布的MD5一致。
- curl跟随地域跳转至中国镜像返回404；PowerShell下载同一官方URL成功。安装器需要管理员权限，常规启动和RunAs失败；安装路径方案仍在排查，尚未宣称Editor已安装。
- 独立.NET SDK 10.0.401已安装；核心目标netstandard2.1，语言限制C# 9.0，未引用UnityEngine。
- 初始2项规则测试因未实现明确失败，随后通过；新增导入/回合7项测试先失败，再实施后通过。
- 当前NUnit共13项通过，覆盖起始牌、权限/原子性、幂等与恢复、数据哈希/越界清单、出生、暗选隐私、动态先攻与四回合边界。
- 原资料校验仍通过；所有卡牌效果状态保持data_only。本批基础移动/回合行为不能提升单卡完成状态。

本机原始测试结果位于被Git忽略的artifacts/tests：core-foundation.trx、import-flow.trx及red目录的失败证据。正式交付将提供汇总和可重跑脚本。

四回合后的RoundEnd目前是显式能力边界，尚未进行回收、兵线、升级或开启下一轮，不宣称GAME-07或完整对局完成。
