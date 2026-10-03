# Blender 制作环境

固定版本 Blender 4.5.14 LTS。本机安装已存在，无需重装或购买插件。项目独立偏好放在artifacts/blender-user-config，不改变个人Blender全局设置。通过入口初始化后启用2分钟自动临时保存、3份手动保存备份；这些不能代替Git与美术源文件备份。

## 入口

在Goa2V1根目录执行：

```powershell
./tools/art/blender.ps1 -Mode Init
./tools/art/blender.ps1 -Mode Check
# 为一个新资产复制模板；目标已存在时不要覆盖
$goaAsset = 'art/production/assets/MyAsset.blend'
if (Test-Path -LiteralPath $goaAsset) { throw '目标已存在，请换一个资产名称' }
Copy-Item -LiteralPath art/production/templates/Goa2_Studio.blend -Destination $goaAsset
./tools/art/blender.ps1 -Mode Open -Asset art/production/assets/MyAsset.blend
./tools/art/blender.ps1 -Mode Export -Asset art/production/assets/MyAsset.blend
```

assets目录保存正式工作源，模板不直接精修；新建前先确认目标不存在。其他电脑可用-BlenderExe指定4.5.14完整路径。导出只进入artifacts/art-production的新目录，不直接覆盖Unity资源，验收后再整合并保留原.meta GUID。不要重跑旧build_minions.py去覆盖已手工精修的旧小兵源。

## 场景约定

- 单位米，比例1；30fps；Blender Z向上，FBX固定参数为axis_forward=-Z、axis_up=Y。导入后以尺寸与朝向校准验证，不能只凭文件扩展名判断。
- EXPORT：最终运行网格和需要随之导出的骨架；SOURCE：高模/可编辑零件；RIGS：制作控制器；FX_GUIDES：效果辅助；STUDIO：相机灯光。骨架要导出时也必须在EXPORT里。
- 模板里的1米方块是校准件，不是将要放进游戏的最终造型，开工时替换。源文件和辅助层保留，运行网格单独整理。
- 应用物体旋转和缩放；角色脚底/道具锚点确定后固定原点；技能环的文字、数值、队伍和卡色由Unity动态显示，不烘进贴图。
- 固定预览使用正交相机、三点光、AgX；CPU Cycles16采样仅用于环境检查，最终美术另设采样/机位。棚拍不代替真实棋盘视角验收。
- FBX只选EXPORT网格/骨架，不带灯、相机、叶骨；当前入口是静态网格/绑定导出，bake_anim=false，**不声称动作导出已完成**。绑定、Action/NLA分段与根运动会随首个角色动作样板补验收。

## 资产交付契约

每个资产提供源.blend、导出FBX/贴图、材质槽名称、包围盒/比例/原点、三角面与骨骼统计、使用的外部素材来源/许可、预览与游戏视角对比。贴图相对路径保留源图；不使用绝对临时目录作运行依赖。

Unity仍用当前Built-in管线。颜色贴图按sRGB、法线/遮罩按线性；最终导入配置需逐项验证，Blender节点不自动等价于游戏Shader。红蓝小兵继续共用模型，由TeamCloth/TeamInset槽替换，不为两队复制整套几何。

所有演员只消费已接受的规则事件。移动、命中和死亡不能由Root Motion或物理碰撞裁定。动画队列支持跳过和重连重置；各英雄可以有不同模型和曲线，共用Idle/Move/Attack/Cast/Hit/Defeat/Respawn的语义接口即可。

第一样板建议：精修一个近战兵，做待机/行走/攻击并验证权重；随后做一个技能按钮的正常/hover/压下/弃置/回收。先验收单个样板再铺开，不同时重做全棋盘。实际英雄形象与用户自制符文仍待各自参考稿。

本批已验证模板存取、CPU渲染、FBX导出重导入后1米尺寸与无棚拍灯/相机泄漏。尚未验证Unity中新资产、GPU烘焙、动作混合、LOD、最终特效或新美术品质。暂不安装额外插件或升级渲染管线。
