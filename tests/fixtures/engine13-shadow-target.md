# 引擎13影袭实际前移后的攻击选择

引擎14修改前，由引擎13 Windows Player执行10步捕获；PreAttackMoved=true，虎爪已移动到(8,-10)，正在选择攻击目标。保存原始字节，完整哈希登记manifest.json。

报告：artifacts/scenarios/engine13-shadow-target-scenario/headless/04e20c7c567e4c45b3dd0185d9ff395f/report.json；10步通过，状态哈希1b20fffd98bc16eaf4a336ad867fe4bd69907f268de68b9d99af3aaa38e56abe。

下一版本应可从此处继续攻击，影袭仍不得再后移；不能因暗影奇袭开放而改写影袭原有移动条件。此存档同时覆盖新条件字段真实序列化，而非只测试缺失字段的默认值。
