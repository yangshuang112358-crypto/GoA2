# Goa2 小兵模型第一版

三种原创可编辑模型：近战盾斧兵、远程兜帽法杖兵、重型装甲拳兵。近战和远程参考 LoL 小兵的大头小身、职业装备和阵营布料层次；重型参考王者荣耀超级兵的肩宽、装甲与重拳，采用同一套青灰金属、旧黄铜和布料色系。没有提取游戏网格或贴图。

`Goa2_Minion_Collection.blend` 是 Blender 4.5.14 LTS 源文件。三个命名集合内保留隐藏的可编辑分件、18骨骼和合并后的运行网格。预览摄像机、灯光和地面留在源文件内，不导出到游戏。重型兵在预览中放大1.3倍；FBX保持原始单位，游戏另按棋盘比例归一化。

导出的三个FBX在 `unity/Assets/Scripts/UI3D/Resources/UI3D/Minions/`。Unity以队伍替换 TeamCloth/TeamInset 材质；蓝红两队共用模型。模型保留顶点明暗色、平滑/硬面法线和可替换的语义材质槽，无外部贴图依赖。顶点和三角面统计见 manifest.json。

制作脚本：`tools/art/build_minions.py`。从项目根目录执行：

```powershell
& 'C:/Users/29383/Applications/Blender/blender-4.5.14-windows-x64/blender.exe' -b --python-exit-code 1 --python tools/art/build_minions.py -- --root (Get-Location).Path
```

输出蓝、红两张真实 Blender 渲染图至 `artifacts/minion-models/`。脚本会重建对应源文件、FBX和预览，手工精修后请先保存副本，避免再次生成覆盖精修。

## 后续动作接口

三者采用相同骨骼语义名称 Root/Hips/Spine/Head、左右 UpperArm/LowerArm/Hand/Weapon、UpperLeg/LowerLeg/Foot；武器随手部骨骼。当前为刚性分件权重，适合装甲摆动及动作原型。尚未制作行走、攻击、施法、受击或死亡动作，布袍也没有柔性变形权重。

不同英雄以后可以使用不同模型与骨架；Unity可通过相同的表现事件触发各自动画，不要求所有角色共享同一套动作曲线。

当前材质为几何分色与顶点明暗的风格化第一版，未制作最终手绘贴图、磨损法线或LOD。Blender棚拍与游戏独立着色器的光照效果不同，棋盘实机截图单独验收。

## 参考入口

- LoL近战/远程外形参考：https://www.deviantart.com/ninjacharliet/art/League-of-Legends-Red-Melee-Caster-Minion-3D-Model-763512663
- 王者兵线造型参考：https://www.biubiu001.com/wzry/33848.html
- Blender LTS：https://www.blender.org/download/lts/

这些页面仅用于造型研究，项目内的几何和材质由脚本制作。
