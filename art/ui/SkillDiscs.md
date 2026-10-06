# 技能环 · 金属圆框与嵌入石标

2026-10-06；生成器 `tools/art/build_skill_discs.py`，可编辑模型 `SkillDiscs.blend`。

参考用户提供的佣兵战纪技能界面与[Blizzard官方玩法展示](https://hearthstone.blizzard.com/en-us/news/23707670/mercenaries-gameplay-spotlight)中[速度羽翼石标](https://bnetcmsus-a.akamaihd.net/cms/content_entry_media/18/180IWJHMDGXN1630017823258.png)。参考用于材质、雕刻与位置研究，运行包不包含下载的图像；Blender脚本生成本项目金属、石面、雕刻符号及羽翼几何。

## 运行规格

- 单技能逻辑占位仍为156×156，正常圆框外径约141。相较旧108圆面扩大内部可用面积，但移除远远悬在外侧的牌匾；四角石标与底部先攻均在原占位内。
- 左上移动、右上防御、左下主要行动、右下范围/远程、底部先攻。卡色为内圈珐琅带，不能以金属框颜色混淆卡牌颜色。
- 小石标34×38，羽翼56×36；数字位于石面内，不再悬浮在牌匾上方。正常白字、加成绿字、减值红字，∞与缺失项目、技能零值隐藏沿用现有卡牌投影。
- 名称仍为临时技能图标内容；本批没有制作108张技能插画。
- 正/背面512×512、小石标256×288、先攻448×288；RGBA透明、sRGB、无mipmap、Clamp。Unity动态提供文字、数值、颜色、压下/翻转/悬停倾斜。
- 贴图烘焙自Blender的带倒角几何与灯光；运行时是透视变形的UI贴图，不声称在Unity中逐个实时渲染高模。
- 暗选、防御、弃牌、取回、浏览及升级沿用同一SkillDisc组件；不新增规则数值算法，不改隐私投影。
- 只参考美术构成；GoA2仍按先攻数值由高到低，不能引入佣兵战纪速度由低到高的规则。

## 重新生成

在项目根目录运行，输出只覆盖此生成器自己拥有的资源，不触碰用户Workbench.blend：

```powershell
& "$env:USERPROFILE/Applications/Blender/blender-4.5.14-windows-x64/blender.exe" --background --factory-startup --python-exit-code 1 --python tools/art/build_skill_discs.py -- (Get-Location).Path
```

调试入口：设置 → 调试 → 技能环 / 雕刻石标样例。八个实时样例可检查双位数、负数、∞、压下、已出、弃置与远程。加减值为纯展示压力样例，不改变对局状态，也不代表获得了这些被动。
