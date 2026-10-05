"""Original hero blockouts from approved silhouettes. Blender 4.5, no external assets.
blender -b --factory-startup --python this.py -- <repo>
"""
import bpy, math, sys, json
from pathlib import Path
from mathutils import Vector
root=Path(sys.argv[sys.argv.index('--')+1]);out=root/'unity/Assets/Resources/UI3D/Heroes';source=root/'art/heroes';preview=root/'artifacts/hero-models'
for path in (out,source,preview):path.mkdir(parents=True,exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True);bpy.context.preferences.filepaths.save_version=0
palette={'WhiteArmor':'E6E5DE','SkinYellow':'D4A860','SkinGreen':'67965C','Skin':'D8AF8A','BrownCloth':'765035','Leather':'49382E','TealArmor':'397D89','ShadowCloth':'252839','RedBeard':'AA4E25','Steel':'647F90','Bronze':'B39250','Glow':'8CE6ED','Shadow':'141924','Linen':'C6C4B0'}
mats={}
def linear(h):
    values=[int(h[i:i+2],16)/255 for i in (0,2,4)]
    return [v/12.92 if v<=.04045 else ((v+.055)/1.055)**2.4 for v in values]
for name,code in palette.items():
    m=bpy.data.materials.new(name);m.diffuse_color=(*linear(code),1);m.use_nodes=True
    p=next(n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED');p.inputs['Base Color'].default_value=m.diffuse_color
    p.inputs['Metallic'].default_value=.6 if name in ('Steel','Bronze','WhiteArmor','TealArmor') else 0;p.inputs['Roughness'].default_value=.43 if name.endswith('Armor') else .65;mats[name]=m
parts=[]
def finish(obj,name,mat,bone='Spine',bevel=0):
    obj.name=name;obj.data.materials.append(mats[mat]);bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
    if bevel:
        mod=obj.modifiers.new('Soft forged edges','BEVEL');mod.width=bevel;mod.segments=2;bpy.ops.object.modifier_apply(modifier=mod.name)
    group=obj.vertex_groups.new(name=bone);group.add(list(range(len(obj.data.vertices))),1,'REPLACE');parts.append(obj);return obj
def ell(name,at,scale,mat,bone='Spine',segments=16):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=segments,ring_count=10,location=at);o=bpy.context.object;o.scale=scale;return finish(o,name,mat,bone)
def box(name,at,scale,mat,bone='Spine'):
    bpy.ops.mesh.primitive_cube_add(size=1,location=at);o=bpy.context.object;o.scale=scale;return finish(o,name,mat,bone,.025)
def rod(name,a,b,r,mat,bone='Spine',r2=None):
    a,b=Vector(a),Vector(b);d=b-a;bpy.ops.mesh.primitive_cone_add(vertices=12,radius1=r,radius2=r if r2 is None else r2,depth=d.length,location=(a+b)/2);o=bpy.context.object;o.rotation_euler=d.to_track_quat('Z','Y').to_euler();return finish(o,name,mat,bone)
def plate(name,points,front,back,mat,bone='Spine'):
    n=len(points);v=[(x,y,z) for y in (front,back) for x,z in points];f=[tuple(reversed(range(n))),tuple(range(n,2*n))]+[(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)]
    mesh=bpy.data.meshes.new(name);mesh.from_pydata(v,[],f);mesh.update();o=bpy.data.objects.new(name,mesh);bpy.context.collection.objects.link(o);bpy.context.view_layer.objects.active=o;o.select_set(True);return finish(o,name,mat,bone,.012)
def tube(name,path,radii,mat,bone='Hips'):
    verts=[];faces=[]
    for i,at in enumerate(path):
        tangent=(Vector(path[min(len(path)-1,i+1)])-Vector(path[max(0,i-1)])).normalized();right=tangent.cross(Vector((0,1,0))).normalized();up=right.cross(tangent).normalized()
        for j in range(12):verts.append(Vector(at)+(right*math.cos(j*math.tau/12)+up*math.sin(j*math.tau/12))*radii[i])
    for i in range(len(path)-1):
        for j in range(12):faces.append((i*12+j,i*12+(j+1)%12,(i+1)*12+(j+1)%12,(i+1)*12+j))
    faces+=[tuple(reversed(range(12))),tuple(range((len(path)-1)*12,len(path)*12))]
    mesh=bpy.data.meshes.new(name);mesh.from_pydata(verts,[],faces);mesh.update();o=bpy.data.objects.new(name,mesh);bpy.context.collection.objects.link(o);bpy.context.view_layer.objects.active=o;return finish(o,name,mat,bone)
def sword(x,mat='Steel',short=False):
    z=1.10 if short else 1.60;bone='Hand.R' if x>0 else 'Hand.L'
    rod('Wrapped grip',(x,-.15,.70),(x,-.15,.95),.045,'Leather',bone);box('Cross guard',(x,-.15,.94),(.32,.08,.07),'Bronze',bone)
    plate('Faceted blade',[(x-.08,.98),(x-.07,z-.12),(x,z),(x+.07,z-.12),(x+.08,.98)],-.19,-.12,mat,bone)
    rod('Blade ridge',(x,-.202,1),(x,-.202,z-.13),.013,'Bronze',bone)
def body(skin,cloth,wide=1,legs=True):
    if legs:
        for side,s in [('L',-1),('R',1)]:
            rod('Upper leg '+side,(s*.14,0,.85),(s*.16,0,.47),.10*wide,cloth,'UpperLeg.'+side)
            rod('Lower leg '+side,(s*.16,0,.47),(s*.16,-.01,.13),.072*wide,'Leather','LowerLeg.'+side)
            box('Boot '+side,(s*.16,-.08,.10),(.18,.32,.19),'Leather','LowerLeg.'+side)
    ell('Torso',(0,0,1.12),(.25*wide,.17,.35),cloth)
    ell('Waist',(0,0,.86),(.18*wide,.15,.16),'Leather','Hips')
    rod('Neck',(0,0,1.39),(0,0,1.50),.075,skin,'Head')
    ell('Head',(0,-.005,1.66),(.16,.145,.22),skin,'Head')
    ell('Nose',(0,-.151,1.65),(.035,.044,.05),skin,'Head')
    for sign in (-1,1):
        ell('Ear',(sign*.157,0,1.66),(.04,.035,.063),skin,'Head')
        ell('Eye',(sign*.063,-.136,1.705),(.031,.017,.019),'Shadow','Head')
        rod('Brow',(sign*.029,-.153,1.744),(sign*.094,-.13,1.752),.011,'Leather','Head')
    for side,s in [('L',-1),('R',1)]:
        rod('Upper arm '+side,(s*.24*wide,0,1.34),(s*.35*wide,-.015,1.07),.085*wide,cloth,'UpperArm.'+side)
        rod('Forearm '+side,(s*.35*wide,-.015,1.07),(s*.42*wide,-.13,.85),.07*wide,skin,'LowerArm.'+side)
        ell('Hand '+side,(s*.43*wide,-.14,.80),(.065,.065,.09),skin,'Hand.'+side)
    box('Belt',(0,-.005,.88),(.40*wide,.33,.09),'Leather','Hips');box('Buckle',(0,-.185,.88),(.09,.035,.09),'Bronze','Hips')
def coat(mat):
    for s in (-1,1):plate('Coat tails',[(s*.04,.94),(s*.22,.94),(s*.31,.27),(s*.10,.32)],.1,.18,mat,'Hips')
def build(kind):
    if kind=='wasp':
        body('SkinYellow','WhiteArmor');sword(.43,'Glow')
        for s in (-1,1):
            ell('White shoulder',(s*.26,0,1.35),(.16,.19,.10),'WhiteArmor','UpperArm.'+('R' if s>0 else 'L'))
            plate('Gilded shoulder fin',[(s*.21,1.36),(s*.43,1.52),(s*.37,1.32)],-.08,.09,'Bronze')
        ell('Swept dark hair',(0,.03,1.80),(.162,.14,.12),'Leather','Head');plate('White forehead circlet',[(-.14,1.79),(0,1.82),(.14,1.79),(0,1.75)],-.14,-.10,'WhiteArmor','Head')
    elif kind=='shargatha':
        body('SkinGreen','TealArmor',legs=False)
        path=[(0,0,.84),(0,.03,.54),(.16,.14,.24),(.51,.10,.16),(.69,-.18,.12),(.46,-.49,.09),(.02,-.51,.07),(-.44,-.35,.04),(-.73,-.07,.025)]
        tube('Coiled serpentine tail',path,[.2,.24,.25,.21,.17,.13,.095,.055,.01],'SkinGreen')
        for i in range(9):
            z=.82+i*.095;ell('Scale armor',(0,-.16,z),(.13,.035,.055),'Bronze')
        for j in range(7):
            x=(j-3)*.055;rod('Crown tendril',(x,.03,1.82),(x*1.6,.15,1.96+(.1 if j%2 else 0)),.034,'SkinGreen','Head',.012)
        sword(.43,'Steel',True)
    elif kind=='brogan':
        body('Skin','Steel',1.4);coat('BrownCloth')
        for s in (-1,1):ell('Layered pauldron',(s*.36,0,1.36),(.23,.24,.13),'Bronze','UpperArm.'+('R' if s>0 else 'L'))
        for i in range(7):
            x=(i-3)*.041;rod('Red beard braid',(x,-.13,1.57),(x*.73,-.17,1.29+abs(i-3)*.024),.034,'RedBeard','Head',.019)
        ell('Red hair',(0,.09,1.76),(.17,.13,.17),'RedBeard','Head');ell('Viking helmet',(0,0,1.82),(.19,.16,.13),'Steel','Head')
        rod('Axe handle',(.61,-.14,.52),(.61,-.14,1.51),.045,'Leather','Hand.R')
        plate('Viking axe head',[(.60,1.48),(.84,1.62),(1.01,1.50),(.97,1.24),(.73,1.20),(.60,1.33)],-.20,-.10,'Steel','Hand.R')
        rod('Round shield',(-.6,-.25,.94),(-.6,-.37,.94),.34,'Bronze','Hand.L')
        rod('Shield wood',(-.6,-.37,.94),(-.6,-.39,.94),.29,'BrownCloth','Hand.L');ell('Shield boss',(-.6,-.41,.94),(.10,.06,.10),'Steel','Hand.L')
    elif kind=='arien':
        body('Skin','TealArmor');coat('TealArmor');sword(.43,'TealArmor')
        ell('Swept sea hair',(0,.05,1.84),(.18,.19,.12),'TealArmor','Head')
        plate('High left collar',[(-.25,1.38),(-.10,1.52),(-.05,1.26),(-.20,1.22)],-.12,.03,'Bronze')
        for s in (-1,1):plate('Coat lapel',[(s*.19,1.34),(s*.04,1.30),(s*.05,.91)],-.177,-.16,'Linen')
    elif kind=='tigerclaw':
        body('Skin','ShadowCloth');coat('ShadowCloth');sword(.43,'Steel',True);sword(-.43,'Steel',True)
        ell('Hood',(0,.055,1.74),(.22,.19,.25),'ShadowCloth','Head')
        ell('Hood opening',(0,-.133,1.66),(.137,.035,.157),'Shadow','Head')
        for s in (-1,1):ell('Shadow eyes',(s*.06,-.174,1.70),(.023,.012,.012),'Glow','Head')
        plate('Shoulder cloak',[(-.29,1.43),(.29,1.43),(.40,.40),(0,.51),(-.32,.34)],.13,.22,'ShadowCloth')
    else:
        body('Skin','BrownCloth')
        # Stockings cover legs below an original short leather skirt; no armor.
        for s in (-1,1):rod('Long stocking',(s*.15,0,.69),(s*.16,-.01,.14),.081,'ShadowCloth','LowerLeg.'+('R' if s>0 else 'L'))
        rod('Short leather skirt',(0,0,.62),(0,0,.88),.29,'BrownCloth','Hips',.205)
        coat('BrownCloth');ell('Hair',(0,.08,1.64),(.18,.12,.30),'Leather','Head')
        ell('Cowboy hat brim',(0,0,1.855),(.38,.30,.035),'BrownCloth','Head',24)
        ell('Cowboy hat crown',(0,.02,1.95),(.18,.17,.15),'BrownCloth','Head');box('Hat band',(0,-.145,1.91),(.28,.025,.045),'Bronze','Head')
        rod('Long rifle barrel',(.46,-.15,.64),(.46,-.15,1.62),.033,'Steel','Hand.R')
        box('Rifle stock',(.46,-.14,.54),(.12,.11,.34),'BrownCloth','Hand.R');box('Rifle action',(.46,-.15,.85),(.085,.08,.22),'Bronze','Hand.R')
        box('Sidearm grip',(-.43,-.14,.78),(.07,.08,.16),'Leather','Hand.L');rod('Sidearm barrel',(-.43,-.19,.87),(-.43,-.42,.87),.035,'Steel','Hand.L')
def export(kind):
    bpy.ops.object.select_all(action='DESELECT')
    for o in parts:o.select_set(True)
    bpy.context.view_layer.objects.active=parts[0];bpy.ops.object.join();mesh=parts[0];mesh.name=kind+' original blockout'
    data=bpy.data.armatures.new(kind+' skeleton');rig=bpy.data.objects.new(kind+' rig',data);bpy.context.collection.objects.link(rig)
    bpy.ops.object.select_all(action='DESELECT');rig.select_set(True);bpy.context.view_layer.objects.active=rig;bpy.ops.object.mode_set(mode='EDIT')
    specs=[('Root',(0,0,0),(0,0,.3),None),('Hips',(0,0,.7),(0,0,.95),'Root'),('Spine',(0,0,.95),(0,0,1.4),'Hips'),('Head',(0,0,1.4),(0,0,1.9),'Spine')]
    for side,s in [('L',-1),('R',1)]:specs += [('UpperArm.'+side,(s*.24,0,1.34),(s*.35,0,1.07),'Spine'),('LowerArm.'+side,(s*.35,0,1.07),(s*.42,-.13,.85),'UpperArm.'+side),('Hand.'+side,(s*.42,-.13,.85),(s*.43,-.14,.7),'LowerArm.'+side),('UpperLeg.'+side,(s*.14,0,.85),(s*.16,0,.47),'Hips'),('LowerLeg.'+side,(s*.16,0,.47),(s*.16,0,.1),'UpperLeg.'+side)]
    for name,head,tail,parent in specs:
        b=data.edit_bones.new(name);b.head=head;b.tail=tail
        if parent:b.parent=data.edit_bones[parent]
    bpy.ops.object.mode_set(mode='OBJECT');mod=mesh.modifiers.new('Semantic blockout rig','ARMATURE');mod.object=rig;mesh.parent=rig
    mesh.select_set(True);bpy.ops.export_scene.fbx(filepath=str(out/(kind+'.fbx')),use_selection=True,object_types={'ARMATURE','MESH'},add_leaf_bones=False,bake_anim=False,axis_forward='-Z',axis_up='Y')
    return rig,mesh
models=[];manifest=[]
for i,kind in enumerate(['wasp','shargatha','brogan','arien','tigerclaw','sabina']):
    parts=[];build(kind);rig,mesh=export(kind);rig.location.x=(i-2.5)*2.05;models.append(rig);manifest.append({'hero':kind,'triangles':sum(len(p.vertices)-2 for p in mesh.data.polygons),'bones':14,'stage':'approved-feature blockout; rigid weights, no finished animation'})
scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.device='CPU';scene.cycles.samples=24;scene.cycles.use_denoising=True;scene.world=bpy.data.worlds.new('Hero studio');scene.world.color=(.15,.15,.15)
for name,at,power,color in [('Key',(-4,-5,8),1500,(1,.85,.69)),('Fill',(5,-3,6),1300,(.65,.80,1)),('Rim',(0,4,6),1800,(.6,.8,1))]:
    data=bpy.data.lights.new(name,'AREA');data.energy=power;data.color=color;data.size=7;o=bpy.data.objects.new(name,data);scene.collection.objects.link(o);o.location=at;o.rotation_euler=(Vector((0,0,1))-o.location).to_track_quat('-Z','Y').to_euler()
bpy.ops.mesh.primitive_plane_add(size=200);floor=bpy.context.object;floor.name='Studio only';floor.data.materials.append(mats['Shadow'])
data=bpy.data.cameras.new('Hero lineup');cam=bpy.data.objects.new('Hero lineup',data);scene.collection.objects.link(cam);cam.location=(2,-15,7);cam.rotation_euler=(Vector((0,0,.9))-cam.location).to_track_quat('-Z','Y').to_euler();data.type='ORTHO';data.ortho_scale=13.2;scene.camera=cam
scene.render.resolution_x=2400;scene.render.resolution_y=900;scene.render.resolution_percentage=100;scene.view_settings.view_transform='AgX'
bpy.ops.wm.save_as_mainfile(filepath=str(source/'Goa2_Hero_Blockouts.blend'),compress=True)
scene.render.filepath=str(preview/'six-heroes.png');bpy.ops.render.render(write_still=True)
(source/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf-8')
print('GOA_HERO_BLOCKOUTS_READY')
