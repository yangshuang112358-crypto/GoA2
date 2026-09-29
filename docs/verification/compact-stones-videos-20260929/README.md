# 紧凑行动石板：实际 Player 连续录像

日期：2026-09-29。源码基线 a913466，engine97，1600×1000 Windows Player。本次只添加录像证据，不改游戏代码。

每段先加载准备好的合法场景存档，再通过 Windows 鼠标/键盘输入操作正式 Player；捕获可见客户区和 WASAPI 系统回放声音。每段连续录制，无剪辑、加速或后配音。不是四人联机验收。

| 文件 | 时长 | 内容 |
| --- | --- | --- |
| 01-hover.mp4 | 29.40 秒 | 普通紧凑石板；电击、投掷飞斧、海妖之歌悬停展开全文，移开收起。此段基本静音。 |
| 02-initiative.mp4 | 31.50 秒 | 四张先攻 11 的主卡；悬停组币；蓝队长选择阿连先行；放弃后红队长选择黄蜂；实际排序、脱组及主行动自动置顶。中途使用设置内尚未迁移的放弃操作。 |
| 03-nested.mp4 | 28.93 秒 | 从投掷飞斧的防御窗口开始；黄蜂以反射屏障防御，二级石板现场插入；布罗根选择弃铜墙铁壁，三级石板现场插入；进入下一主行动后向上浏览历史。 |

第三段初始已存在的猛攻节点，是投掷飞斧此前支付的可选弃牌，和后来的强制弃牌不同。结束浏览时最上方主卡部分位于视口之外，保留实际滚动结果，没有重新拼接画面。

三段已完整解码检查并抽帧人工查看；详见 recording-checks.json，含时长、SHA256 和系统音轨峰值。同目录 jpg 是抽帧索引。构建证据沿用 ../compact-action-stones-20260929/build-info.json 和 package.json；未因录像重新宣称全量测试通过。

外观仍是程序石材、临时头像和行动类别图标。右侧行动圆环迁移尚未实施，本录像不作为该功能的完成证据。

本地准备来源：tests/scenarios/throwing-axe-reflection.json 的前 9 步（悬停）与前 16 步（三级响应），tests/scenarios/action-sequence-four-tie.json 的前 6 步（同先攻）。录制工具沿用 artifacts/videos/action-stones-os/record.py，实际输出保留在 artifacts/videos/compact-hover、compact-tie、compact-nested。
