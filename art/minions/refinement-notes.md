# 三兵种精修生成器与导出合同

入口为 `tools/art/refine_minion_models.py`。这是新的精修源生成器，旧 `build_minions.py` 只提供函数定义、原始轮廓和绑定合同，不执行其顶层建模、保存、渲染或导出。旧 `Goa2_Minion_Collection.blend`、用户 `Workbench.blend` 与 Unity `.meta` 均列入前后 SHA256 保护。

## 本轮精修范围

- 近战：平滑锻造头盔与胸甲，连续盔甲饰线、叠层肩片、手套弯曲手指/拇指/指节、剑柄尾饰、盾面刻线、带缝线的皮革扣带。
- 远程：保留木弓握点和上下弓梢；弓身与镶条由断续圆杆改为连续曲线，腰袍折线改为平滑布褶，增加握把缠带、箭筒环带、斜挎肩带、帽兜缝边与持弓手细节。箭弦仍由 Unity 按真实弓梢绘制，不导出重复弓弦。
- 重型：保留圆盾/宝石剑/八条收放腿，增加机械腿活塞套、盾边锻造铆钉及分区饰线、握持与剑柄细节，身体连续曲面与硬质大面分别保留相应法线。
- 三兵种保持材质语义槽，红蓝仍由 `TeamCloth` / `TeamInset` 同一模型染色；新增 `UV0_PaintReady` 智能展开作为后续手绘基础，不声称已经完成手绘贴图。

## 不能改变的运行合同

18/21/26 根语义骨骼的名字、父子关系、rest head/tail/matrix 与原脚本逐项相同。远程额外 `BowGrip`、`BowTip.Top`、`BowTip.Bottom` 位于原坐标并保持父级 `Weapon.L`。保留旧右手剑刃转向、手掌锚点及非远程武器额外 1.12 倍变换，不能在运行时再做重复放大。

输出前以原脚本在内存产生一份不写盘基准，检查每兵 ≤30,000 三角面、每顶点原有的单骨刚性权重、无世界根运动、尺寸变化 ≤3.5%、底部误差 ≤0.005 米。尺寸阈值仅容纳新增边饰，运行时仍使用现有统一高度。所有 FBX 导出为 `-Z forward / Y up`，仅包含运行网格与原骨架。

## 执行

在项目根目录，先独立生成与棚拍：

```powershell
$goaBlender = Join-Path $env:USERPROFILE 'Applications/Blender/blender-4.5.14-windows-x64/blender.exe'
& $goaBlender --background --factory-startup --disable-autoexec --python-exit-code 1 --python tools/art/refine_minion_models.py -- --root $PWD.Path
```

源文件为 `art/production/assets/MinionRefinement.blend`；临时输出、三视角为 `artifacts/minion-refinement`；资产清单为 `art/minions/refinement-manifest.json`。默认不替换现有 Unity FBX。

通过几何审阅后可用 `--publish` 生成并更新原三个 FBX 路径，其 `.meta` 保留；若本生成器的源已存在需显式增加 `--replace-generated`。该选项不能用来覆盖已经手工精修的 `.blend`，应先保留独立源副本。上一版 FBX 留在输出目录的 `previous-runtime`。

```powershell
& $goaBlender --background --factory-startup --disable-autoexec --python-exit-code 1 --python tools/art/refine_minion_models.py -- --root $PWD.Path --publish --replace-generated
```

`--skip-render` 只跳过三视角渲染；`--samples 48` 为默认 CPU Cycles 棚拍采样；可用 `--output artifacts/minion-refinement-review2` 保存另一批证据。

## 必须继续进行的验证

代码静态检查不能证明 Blender 成功执行；以新清单及实际生成文件为准。棚拍不能证明 Unity IK 或战斗动作无穿模。整合后仍须逐兵检查待机、攻击准备、实际攻击、格挡、回位，以及重型免疫底盘/站起四种状态；远程要额外检查取箭绕头、上箭及拉弦全过程，红蓝转向和镜头旋转均应核对。模型细节材质由父任务接入的 shader 渲染，Blender 预览材质不自动等同游戏效果。

本轮仍使用旧单骨分件绑定。它能安全精修既有角色，但不是软布料蒙皮、写实手部或最终影视级拓扑；没有借这次美术任务更改规则、目标选取、动画时序、骨骼合同或网络消息。
