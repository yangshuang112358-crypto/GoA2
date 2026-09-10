# 兼容性与负例夹具

- legacy-v1-roundend.json：BATCH-01实际Windows Player保存的第1轮末状态，59条命令，四名默认玩家；缺少后来增加的Sandbox、推进和胜负字段。用于验证升级后的规则仍能精确重放历史命令，而不是只反序列化并信任保存的结果。
- engine2-purple-aura.json：真实版本2，满级紫卡拥有但未实装触发，静电封锁范围仍有效。
- engine4-barrier-pending.json：真实版本4，反射屏障后的攻击者强制弃牌窗口。
- engine5-headshot-pending.json：真实版本5，一枪爆头条件+2后等待防御。
- engine6-unsupported-defense.json：真实版本6，唯一剩余近身还击尚未实装，保留防御窗口。
- engine7-riposte-pending.json：真实版本7，近身还击已抵挡并完成原攻击后文，等待原攻击者弃牌。
- scenario-failure.json：故意将准备后的阶段期望为Action，后续999金币命令必须不执行；不纳入默认通过场景集。

夹具不含账号、许可证或机器路径。新版状态可以增加有默认值的字段，但已有命令字段和既有历史事件语义不能静默改变；RoundEnd闭环接入须单独解决版本兼容。

engine开头的存档、场景输入及普通确认旧档由.gitattributes保留捕获字节，禁止自动换行转换；每份说明记录原输入/存档/程序集哈希。不要把这些旧场景按当前能力运行后覆盖旧存档。

manifest.json登记六份历史存档及五份对应输入。资料校验直接核对原字节SHA-256、引擎版本、命令数量、来源说明与完整登记范围；JSON语义不变的换行或空白改动也会失败。新增真实旧档时同时登记清单及取证说明，不能通过更新摘要来掩盖旧档被改写。test_fixtures.py在独立临时副本验证九项完整性及负例，不改原始捕获。
