# 小兵动作证据

运行源码：`9ce644ca`。规则、网络与存档格式未变。

- `01-archer-motion.mp4`：24.63秒，连续取箭/搭箭/满弓、多角度、取消和再进入。真实Unity Editor渲染、脚本摄影、静音。
- `02-combat-and-guards.mp4`：30.13秒，实际规则的攻击、防御、三兵种出招与击杀助攻金币，随后近战/重型准备、举盾、待机近看。新局面在两段之间建立，片内连续运镜。
- `archer-043-three.png`、`archer-066-three.png`、`archer-120-side.png`：Unity导入后的动作采样近景，分别为取箭/搭箭/满弓。测试用逐帧时钟驱动，不是实战截图。
- `minions-ready.png`、`minions-strike.png`：最终Windows Player实际规则流程截图，原有UI保留。
- `unity-35.xml`：35项相关Unity测试，含真实蒙皮网格的箭/弦与兜帽相交检查、连续采样、长帧第二箭反方向检查。
- `player-combat.json`：最终Player9项渲染/规则投影检查；不是真实OS输入。
- `build-info.json`：最终运行载荷和源文件清单。
- `timing.txt`：原始录像编码帧数与墙钟时长。编码30fps不代表性能基准。
- `motion-evidence.txt`：录制脚本中的战况不变/真实事件断言结果。

原始录像、5份战况快照和日志留于 `artifacts/videos/minion-motion-20261009-044553/`。交付录像仅重新压缩，没有剪掉片内失败步骤或替换为概念图。完整资料、已改内容和质量差距见上一层《小兵动作衔接与弓箭约束-2026-10-09.md》。
