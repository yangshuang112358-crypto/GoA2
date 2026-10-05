"""Original beveled stone/brass controls. Blender geometry baked for a lightweight UI."""
import bpy
import math
from pathlib import Path
import sys
from mathutils import Vector

root=Path(sys.argv[sys.argv.index('--')+1]).resolve()
source=root/'art/ui/DecisionControls.blend'
output=root/'unity/Assets/Resources/UI3D/DecisionButtons'
source.parent.mkdir(parents=True,exist_ok=True);output.mkdir(parents=True,exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
def material(name,color,metal=0,rough=.5,emission=0):
    m=bpy.data.materials.new(name);m.diffuse_color=(*color,1);m.use_nodes=True
    p=m.node_tree.nodes.get('Principled BSDF');p.inputs['Base Color'].default_value=(*color,1)
    p.inputs['Metallic'].default_value=metal;p.inputs['Roughness'].default_value=rough
    p.inputs['Emission Color'].default_value=(*color,1);p.inputs['Emission Strength'].default_value=emission
    return m
stone=material('Charcoal basalt',(.045,.057,.064),.12,.7)
brass=material('Aged brass',(.36,.22,.08),.8,.32)
trim=material('Polished gold edges',(.68,.45,.16),.72,.24)
face=material('Enchanted inset',(.025,.22,.07),.28,.38,.22)
rune=material('Inset rune light',(.15,.8,.3),.25,.28,1.4)
def slab(name,w,h,z,depth,mat,cut=.2):
    points=[(-w/2+cut,-h/2),(w/2-cut,-h/2),(w/2,-h/2+cut),(w/2,h/2-cut),(w/2-cut,h/2),(-w/2+cut,h/2),(-w/2,h/2-cut),(-w/2,-h/2+cut)]
    verts=[(x,y,z+d) for d in (-depth/2,depth/2) for x,y in points]
    faces=[tuple(reversed(range(8))),tuple(range(8,16))]+[(i,(i+1)%8,(i+1)%8+8,i+8) for i in range(8)]
    mesh=bpy.data.meshes.new(name);mesh.from_pydata(verts,[],faces);mesh.update()
    obj=bpy.data.objects.new(name,mesh);bpy.context.collection.objects.link(obj);obj.data.materials.append(mat)
    mod=obj.modifiers.new('Hand-cut bevel','BEVEL');mod.width=.045;mod.segments=3
    obj.modifiers.new('Weighted normals','WEIGHTED_NORMAL');return obj
slab('Stone foundation',4.1,1.7,0,.4,stone,.28)
slab('Brass perimeter',3.95,1.55,.23,.15,brass,.25)
slab('Raised chamfer',3.76,1.37,.32,.12,trim,.22)
inset=slab('Recessed enamel stone',3.6,1.22,.39,.12,face,.2)
for x in (-1.75,1.75):
    for y in (-.47,.47):
        bpy.ops.mesh.primitive_uv_sphere_add(segments=12,ring_count=6,radius=.085,location=(x,y,.43))
        bpy.context.object.scale=(1,1,.4);bpy.context.object.data.materials.append(trim)
def line(name,points,mat,width=.018):
    data=bpy.data.curves.new(name,'CURVE');data.dimensions='3D';data.bevel_depth=width;data.bevel_resolution=3
    sp=data.splines.new('POLY');sp.points.add(len(points)-1)
    for p,co in zip(sp.points,points):p.co=(*co,1)
    obj=bpy.data.objects.new(name,data);bpy.context.collection.objects.link(obj);data.materials.append(mat);return obj
for side in (-1,1):
    line('Inset circuit',[(side*1.65,-.32,.461),(side*1.55,-.42,.461),(side*.95,-.42,.461)],rune)
    line('Inset circuit',[(side*1.65,.32,.461),(side*1.55,.42,.461),(side*.95,.42,.461)],rune)
scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.device='CPU';scene.cycles.samples=24
scene.render.film_transparent=True;scene.render.resolution_x=640;scene.render.resolution_y=320;scene.render.resolution_percentage=100
scene.render.image_settings.file_format='PNG';scene.render.image_settings.color_mode='RGBA';scene.view_settings.view_transform='AgX'
scene.world=bpy.data.worlds.new('Dark studio');scene.world.color=(.16,.16,.16)
lights=[]
for name,loc,power,color in [('Softbox',(-3,-4,7),750,(1,.88,.65)),('Rim',(3,2,5),650,(.55,.72,1))]:
    d=bpy.data.lights.new(name,'AREA');d.energy=power;d.color=color;d.size=4
    o=bpy.data.objects.new(name,d);bpy.context.collection.objects.link(o);o.location=loc;o.rotation_euler=(-o.location).to_track_quat('-Z','Y').to_euler();lights.append(d)
d=bpy.data.cameras.new('UI orthographic bake');o=bpy.data.objects.new(d.name,d);bpy.context.collection.objects.link(o)
o.location=(0,-3,9);o.rotation_euler=(Vector((0,0,.15))-o.location).to_track_quat('-Z','Y').to_euler();d.type='ORTHO';d.ortho_scale=4.8;scene.camera=o
scene['usage']='Blender-made geometry/light bakes; UI animates translation/tilt/press. No Hearthstone ripped assets.'
bpy.ops.wm.save_as_mainfile(filepath=str(source))
for name,color,glow in [('confirm',(.025,.22,.07),(.15,.8,.3)),('withdraw',(.055,.07,.085),(.24,.33,.4))]:
    shader=face.node_tree.nodes.get('Principled BSDF');shader.inputs['Base Color'].default_value=(*color,1);shader.inputs['Emission Color'].default_value=(*color,1)
    rune.node_tree.nodes.get('Principled BSDF').inputs['Emission Color'].default_value=(*glow,1)
    for state,energy,offset in [('idle',750,0),('hover',1050,.04),('pressed',580,-.065)]:
        lights[0].energy=energy;inset.location.z=offset
        scene.render.filepath=str(output/(name+'-'+state+'.png'));bpy.ops.render.render(write_still=True)
print('GOA_DECISION_BUTTONS_READY')
