"""Editable tapered water ribbons and shadow wisps for Unity's live FX shader."""
import bpy, math, json, sys
from pathlib import Path
root=Path(sys.argv[sys.argv.index('--')+1]).resolve()
out=root/'unity/Assets/Resources/UI3D/HeroFX';out.mkdir(parents=True,exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.preferences.filepaths.save_version=0
records=[]
for kind in ('WaterRibbon','ShadowWisp'):
    water=kind=='WaterRibbon';verts=[];faces=[];uvs=[]
    count=96
    for i in range(count+1):
        t=i/count;a=t*math.tau*(.86 if water else .60)
        r=.25+t*.51 if water else .38+.12*math.sin(t*math.pi)
        width=(.018+.105*math.sin(t*math.pi)**.7) if water else (.02+.13*math.sin(t*math.pi))
        for side in (-1,1):
            radius=r+side*width
            z=.025+math.sin(t*math.pi)*.035 if water else .13+t*1.8
            verts.append((math.cos(a)*radius,math.sin(a)*radius,z))
            uvs.append((t,(side+1)/2))
    for i in range(count):faces.append((2*i,2*i+1,2*i+3,2*i+2))
    mesh=bpy.data.meshes.new(kind);mesh.from_pydata(verts,[],faces);mesh.update()
    uv=mesh.uv_layers.new(name='FlowUV')
    for loop in mesh.loops:uv.data[loop.index].uv=uvs[loop.vertex_index]
    obj=bpy.data.objects.new(kind,mesh);bpy.context.collection.objects.link(obj)
    m=bpy.data.materials.new('FlowMask');m.diffuse_color=(.1,.55,.65,1) if water else (.14,.06,.24,1)
    mesh.materials.append(m)
    bpy.ops.object.select_all(action='DESELECT');obj.select_set(True);bpy.context.view_layer.objects.active=obj
    bpy.ops.export_scene.fbx(filepath=str(out/(kind+'.fbx')),use_selection=True,object_types={'MESH'},bake_anim=False,axis_forward='-Z',axis_up='Y')
    records.append({'name':kind,'triangles':count*2,'uv':'u=length, v=cross section; tapered endpoints','external_assets':[]})
bpy.context.scene['README']='Static ribbon meshes only. Unity drives flow, dissolution and opacity; no gameplay collision, no root motion.'
bpy.ops.wm.save_as_mainfile(filepath=str(root/'art/production/assets/HeroFX.blend'),compress=True)
(root/'art/heroes/fx-manifest.json').write_text(json.dumps(records,indent=2)+'\n',encoding='utf8')
print('GOA_HERO_FX_READY')
