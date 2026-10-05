"""Three original Atlantis relief emblems, editable Blender master and runtime FBX."""
import bpy, math, sys
from pathlib import Path
from mathutils import Vector
root=Path(sys.argv[sys.argv.index('--')+1]);source=root/'art/coins';out=root/'unity/Assets/Resources/UI3D/GoldCoins';preview=root/'artifacts/coin-designs'
for folder in (source,out,preview):folder.mkdir(parents=True,exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True);bpy.context.preferences.filepaths.save_version=0
def mat(name,color,metal,rough):
    m=bpy.data.materials.new(name);m.diffuse_color=(*color,1);m.use_nodes=True;p=next(n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED');p.inputs['Base Color'].default_value=m.diffuse_color;p.inputs['Metallic'].default_value=metal;p.inputs['Roughness'].default_value=rough;return m
gold=mat('Warm minted gold',(.55,.29,.065),.85,.28);light=mat('Polished raised relief',(.84,.53,.17),.8,.23);dark=mat('Oxidized recess',(.095,.055,.026),.7,.5);parts=[]
def disk(name,r,z,depth,material):
    bpy.ops.mesh.primitive_cylinder_add(vertices=96,radius=r,depth=depth,location=(0,0,z));o=bpy.context.object;o.name=name;o.data.materials.append(material);mod=o.modifiers.new('Minted bevel','BEVEL');mod.width=.016;mod.segments=3;parts.append(o);return o
def line(name,points,material=light,r=.035):
    closed=(Vector(points[0])-Vector(points[-1])).length<.00001
    if closed:points=points[:-1]
    data=bpy.data.curves.new(name,'CURVE');data.dimensions='3D';data.bevel_depth=r;data.bevel_resolution=3;data.use_fill_caps=True;s=data.splines.new('POLY');s.use_cyclic_u=closed;s.points.add(len(points)-1)
    for p,co in zip(s.points,points):p.co=(*co,1)
    o=bpy.data.objects.new(name,data);bpy.context.collection.objects.link(o);data.materials.append(material);parts.append(o);return o
def path(points):return line('Original Atlantis relief',[(x,y,.093) for x,y in points])
def ring(r,z,material=light,width=.018):return line('Mint border',[(math.cos(i*math.tau/96)*r,math.sin(i*math.tau/96)*r,z) for i in range(97)],material,width)
coins=[]
for i,variant in enumerate(('A-TideTrident','B-SunkenGate','C-WaveEye')):
    parts=[];disk('Gold body',.50,0,.095,gold);disk('Inset field',.435,.05,.018,dark);ring(.468,.054);ring(.410,.067,light,.012)
    for j in range(40):
        a=j*math.tau/40;line('Edge milling',[(math.cos(a)*.497,math.sin(a)*.497,-.025),(math.cos(a)*.497,math.sin(a)*.497,.025)],light,.007)
    if i==0:
        path([(0,-.29),(0,.28)]);path([(-.24,.20),(-.23,-.025),(0,-.08),(.23,-.025),(.24,.20)])
        for x,y in ((0,.29),(-.24,.22),(.24,.22)):path([(x-.055,y-.075),(x,y),(x+.055,y-.075)])
        path([(-.23,-.24),(-.12,-.20),(0,-.24),(.12,-.28),(.23,-.24)])
    elif i==1:
        path([(-.29,-.20),(-.29,.15),(0,.30),(.29,.15),(.29,-.20)])
        path([(-.14,-.20),(-.14,.08),(0,.15),(.14,.08),(.14,-.20)])
        for y in (-.21,-.29):path([(-.32,y),(.32,y)])
        path([(-.35,.11),(0,.34),(.35,.11)])
    else:
        path([(-.34,0),(-.18,.14),(0,.20),(.18,.14),(.34,0),(.18,-.14),(0,-.20),(-.18,-.14),(-.34,0)])
        ring(.11,.093,light,.027)
        # User-selected emblem: three straight segments on each side;
        # derive the lower ornament by a half-turn for exact central symmetry.
        upper=[(-.22,.26),(-.075,.30),(.075,.26),(.22,.30)]
        for sign in (1,-1):
            line('Three-segment tide',[(sign*x,sign*y,.093) for x,y in upper],light,.021)
    bpy.ops.object.select_all(action='DESELECT')
    for obj in parts:obj.select_set(True)
    bpy.context.view_layer.objects.active=parts[0];bpy.ops.object.convert(target='MESH');bpy.ops.object.join();coin=bpy.context.object;coin.name=variant
    bpy.ops.export_scene.fbx(filepath=str(out/(variant+'.fbx')),use_selection=True,object_types={'MESH'},add_leaf_bones=False,bake_anim=False,axis_forward='-Z',axis_up='Y')
    coin.location.x=(i-1)*1.22;coins.append(coin)
# Separate two-sided decision coin: original ornate minting, red/blue six-sided gems.
parts=[];disk('Decision gold body',.50,0,.13,gold)
red=mat('RedGem',(.48,.018,.035),.25,.18);blue=mat('BlueGem',(.025,.11,.55),.25,.18)
for side,gem in ((1,red),(-1,blue)):
    disk('Decision recessed field',.433,side*.070,.010,dark)
    ring(.467,side*.073,light,.032);ring(.425,side*.076,gold,.017)
    ring(.307,side*.085,light,.021)
    for j in range(12):
        a=j*math.tau/12
        line('Rounded raised sun ray',[(math.cos(a)*.33,math.sin(a)*.33,side*.086),(math.cos(a+.11)*.373,math.sin(a+.11)*.373,side*.090),(math.cos(a+.20)*.409,math.sin(a+.20)*.409,side*.084)],light,.020)
    bpy.ops.mesh.primitive_cylinder_add(vertices=6,radius=.276,depth=.058,location=(0,0,side*.086));socket=bpy.context.object;socket.name='Hexagonal jewel socket';socket.data.materials.append(light);parts.append(socket)
    bevel=socket.modifiers.new('Softly worn jewel setting','BEVEL');bevel.width=.012;bevel.segments=3
    verts=[]
    for radius,z in ((.225,side*.104),(.238,side*.122),(.170,side*.156)):
        for j in range(6):verts.append((math.cos(j*math.tau/6)*radius,math.sin(j*math.tau/6)*radius,z))
    faces=[tuple(range(12,18))]+[(ringidx*6+j,ringidx*6+(j+1)%6,(ringidx+1)*6+(j+1)%6,(ringidx+1)*6+j) for ringidx in range(2) for j in range(6)]
    if side<0:faces=[tuple(reversed(f)) for f in faces]
    mesh=bpy.data.meshes.new('Six sided gem');mesh.from_pydata(verts,[],faces);mesh.update();obj=bpy.data.objects.new(gem.name,mesh);bpy.context.collection.objects.link(obj);mesh.materials.append(gem);parts.append(obj)
for j in range(48):
    a=j*math.tau/48;line('Decision milled edge',[(math.cos(a)*.497,math.sin(a)*.497,-.05),(math.cos(a)*.497,math.sin(a)*.497,.05)],light,.007)
bpy.ops.object.select_all(action='DESELECT')
for obj in parts:obj.select_set(True)
bpy.context.view_layer.objects.active=parts[0];bpy.ops.object.convert(target='MESH');bpy.ops.object.join();decision=bpy.context.object;decision.name='DecisionCoin'
bpy.ops.export_scene.fbx(filepath=str(out.parent/'DecisionCoin.fbx'),use_selection=True,object_types={'MESH'},bake_anim=False,axis_forward='-Z',axis_up='Y')
decision.hide_render=True;decision.hide_set(True)
scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.device='CPU';scene.cycles.samples=32;scene.cycles.use_denoising=True;scene.world=bpy.data.worlds.new('Coin studio');scene.world.color=(.18,.18,.18)
for name,pos,power,color in [('Key',(-2,-3,5),500,(1,.88,.66)),('Rim',(3,1,4),550,(.68,.83,1))]:
    d=bpy.data.lights.new(name,'AREA');d.energy=power;d.size=3;d.color=color;o=bpy.data.objects.new(name,d);scene.collection.objects.link(o);o.location=pos;o.rotation_euler=(-o.location).to_track_quat('-Z','Y').to_euler()
d=bpy.data.cameras.new('Emblems');o=bpy.data.objects.new('Emblems',d);scene.collection.objects.link(o);o.location=(0,-2.1,6);o.rotation_euler=(Vector((0,0,0))-o.location).to_track_quat('-Z','Y').to_euler();d.type='ORTHO';d.ortho_scale=3.8;scene.camera=o
scene.render.resolution_x=1800;scene.render.resolution_y=760;scene.render.resolution_percentage=100;scene.render.film_transparent=False;scene.view_settings.view_transform='AgX'
bpy.ops.wm.save_as_mainfile(filepath=str(source/'Atlantis_Three_Emblems.blend'),compress=True)
scene.render.filepath=str(preview/'three-atlantis-coins.png');bpy.ops.render.render(write_still=True)
for coin in coins:coin.hide_render=True
decision.hide_render=False;decision.hide_set(False);decision.location.x=-.68
reverse=decision.copy();reverse.data=decision.data.copy();scene.collection.objects.link(reverse);reverse.location.x=.68;reverse.rotation_euler.x=math.pi
scene.camera.data.ortho_scale=2.65;scene.render.resolution_x=1400;scene.render.resolution_y=760
scene.render.filepath=str(preview/'decision-two-sides.png');bpy.ops.render.render(write_still=True)
print('GOA_ATLANTIS_EMBLEMS_READY')
