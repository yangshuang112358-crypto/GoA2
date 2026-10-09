"""Editable R2B-01 component samples. No Unity resources or existing art are replaced.
Blender 4.5 LTS --background --python this.py -- --root REPO --out ARTIFACTS
Builds actual boolean recesses, not number decals or an AI image plane.
"""
import argparse, bpy, math, json, sys
from pathlib import Path
from mathutils import Vector
from math import sin, cos, pi, tau
p=argparse.ArgumentParser()
p.add_argument('--root',type=Path,required=True); p.add_argument('--out',type=Path,required=True)
p.add_argument('--replace-generated',action='store_true')
p.add_argument('--preview-only',action='store_true')
p.add_argument('--gpu',action='store_true',help='Opt-in; local CUDA kernel did not load during sample development.')
p.add_argument('--no-render',action='store_true',help='Save source and geometry report without image rendering.')
args=p.parse_args(sys.argv[sys.argv.index('--')+1:])
root=args.root.resolve(); out=args.out.resolve(); out.mkdir(parents=True,exist_ok=True)
source=root/'art/production/samples/R2B01/R2B01_ComponentSamples.blend'
if source.exists() and not args.replace_generated: raise RuntimeError('Generated sample already exists; use --replace-generated for this file only.')
sys.path.insert(0,str(root/'tools/art'))
from r2b_ring_sample import build_ring
from r2b_skill_relief import build_relief
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.preferences.filepaths.save_version=0
font=bpy.data.fonts.load('C:/Windows/Fonts/georgiab.ttf')
cnfont=bpy.data.fonts.load('C:/Windows/Fonts/msyhbd.ttc')
active=None
cut_count=0
cut_audit=[]
scenes={}
def collection(name,scene=None):
    c=bpy.data.collections.new(name); (scene or bpy.context.scene).collection.children.link(c); return c
def adopt(ob,mat=None):
    for c in list(ob.users_collection): c.objects.unlink(ob)
    active.objects.link(ob)
    if mat: ob.data.materials.append(mat)
    return ob
def finish(ob,width=.02):
    if width:
        m=ob.modifiers.new('Hand cut chamfer','BEVEL');m.width=width;m.segments=3
        m=ob.modifiers.new('Weighted corner normals','WEIGHTED_NORMAL');m.keep_sharp=True
    return ob
def apply(ob,modifier):
    bpy.context.view_layer.objects.active=ob; ob.select_set(True)
    bpy.ops.object.modifier_apply(modifier=modifier.name); ob.select_set(False)
def mesh(name,verts,faces,mat,bevel=0):
    me=bpy.data.meshes.new(name);me.from_pydata(verts,[],faces);me.update()
    ob=bpy.data.objects.new(name,me);active.objects.link(ob);me.materials.append(mat)
    return finish(ob,bevel)
def polygon(name,pts,lo,hi,mat,bevel=.02):
    # Ensure CCW outline for outward normals; semantic silhouettes remain symmetric.
    area=sum(pts[i][0]*pts[(i+1)%len(pts)][1]-pts[(i+1)%len(pts)][0]*pts[i][1] for i in range(len(pts)))
    if area<0:pts=list(reversed(pts))
    n=len(pts)
    return mesh(name,[(x,y,z) for z in (lo,hi) for x,y in pts],
      [tuple(reversed(range(n))),tuple(range(n,2*n))]+[(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)],mat,bevel)
def circ(name,r,z,depth,mat):
    return polygon(name,[(r*cos(i*tau/96),r*sin(i*tau/96)) for i in range(96)],z,z+depth,mat,.018)
def annulus(name,ro,ri,z,depth,mat,n=128):
    verts=[(r*cos(i*tau/n),r*sin(i*tau/n),h) for r,h in [(ro,z),(ro,z+depth),(ri,z+depth),(ri,z)] for i in range(n)]
    faces=[(k*n+i,k*n+(i+1)%n,((k+1)%4)*n+(i+1)%n,((k+1)%4)*n+i) for k in range(4) for i in range(n)]
    return mesh(name,verts,faces,mat,.012)
def path(name,points,width,mat,cyclic=False):
    cu=bpy.data.curves.new(name,'CURVE');cu.dimensions='3D';cu.bevel_depth=width;cu.bevel_resolution=3;cu.use_fill_caps=True
    sp=cu.splines.new('POLY');sp.points.add(len(points)-1)
    for pt,xyz in zip(sp.points,points):pt.co=(*xyz,1)
    sp.use_cyclic_u=cyclic
    ob=bpy.data.objects.new(name,cu);active.objects.link(ob);cu.materials.append(mat)
    return ob
def material(name,dark,light,metal=0,rough=.6,emission=0):
    ma=bpy.data.materials.new(name);ma.use_nodes=True;ma.diffuse_color=(*light,1)
    ns,ls=ma.node_tree.nodes,ma.node_tree.links;bs=ns.get('Principled BSDF')
    bs.inputs['Metallic'].default_value=metal;bs.inputs['Roughness'].default_value=rough
    tex=ns.new('ShaderNodeTexNoise');tex.inputs['Scale'].default_value=6;tex.inputs['Detail'].default_value=4
    ramp=ns.new('ShaderNodeValToRGB');ramp.color_ramp.elements[0].position=.18;ramp.color_ramp.elements[0].color=(*dark,1)
    ramp.color_ramp.elements[1].position=.84;ramp.color_ramp.elements[1].color=(*light,1)
    ls.new(tex.outputs['Fac'],ramp.inputs[0]);ls.new(ramp.outputs['Color'],bs.inputs['Base Color'])
    micro=ns.new('ShaderNodeTexNoise');micro.inputs['Scale'].default_value=175;micro.inputs['Detail'].default_value=2
    bump=ns.new('ShaderNodeBump');bump.inputs['Strength'].default_value=.33;bump.inputs['Distance'].default_value=.013
    ls.new(micro.outputs['Fac'],bump.inputs['Height']);ls.new(bump.outputs['Normal'],bs.inputs['Normal'])
    if emission:
        bs.inputs['Emission Color'].default_value=(*light,1);bs.inputs['Emission Strength'].default_value=emission
        if 'magic below groove' in name:
            # Slow flowing light and darker pools inside the actual channel,
            # instead of a uniformly luminous flat number.
            tex.noise_dimensions='4D';tex.inputs['Scale'].default_value=24
            tex.inputs['W'].default_value=0;tex.inputs['W'].keyframe_insert('default_value',frame=1)
            tex.inputs['W'].default_value=1.4;tex.inputs['W'].keyframe_insert('default_value',frame=193)
            ramp.color_ramp.elements[0].position=.32
            ramp.color_ramp.elements[0].color=(*[c*.025 for c in light],1)
            ramp.color_ramp.elements[1].position=.78
            ramp.color_ramp.elements[1].color=(*[min(c*1.8,1) for c in light],1)
            ramp.color_ramp.elements.new(.54).color=(*[c*.28 for c in light],1)
            ls.new(ramp.outputs['Color'],bs.inputs['Emission Color'])
    return ma
M={}
M['stone']=material('R2B01 / graphite slate',(.016,.020,.026),(.069,.077,.087),0,.76)
M['name']=material('R2B01 / inscription slate',(.033,.038,.046),(.14,.15,.17),0,.75)
M['red']=material('R2B01 / red card stone',(.045,.009,.006),(.19,.035,.020),0,.73)
M['blue']=material('R2B01 / blue card stone',(.006,.022,.04),(.028,.080,.15),0,.73)
M['green']=material('R2B01 / green card stone',(.008,.025,.012),(.033,.12,.055),0,.73)
M['gold']=material('R2B01 / gold card stone',(.087,.052,.011),(.29,.205,.046),.12,.59)
M['silver']=material('R2B01 / silver card stone',(.085,.095,.11),(.30,.33,.37),.22,.56)
M['purple']=material('R2B01 / purple card stone',(.043,.02,.068),(.20,.064,.31),0,.66)
M['bronze']=material('R2B01 / aged bronze',(.028,.018,.009),(.24,.14,.058),.78,.41)
M['edge']=material('R2B01 / bronze polished edge',(.16,.079,.021),(.41,.26,.10),.80,.32)
M['dark_metal']=material('R2B01 / blackened steel',(.012,.018,.024),(.063,.079,.099),.62,.40)
M['rune']=material('R2B01 / blue rune magic',(.012,.042,.10),(.02,.29,.68),.12,.28,2.0)
M['buff']=material('R2B01 / green magic below groove',(.001,.025,.006),(.012,.42,.063),.1,.26,2.5)
M['debuff']=material('R2B01 / red magic below groove',(.04,.001,.001),(.67,.015,.008),.1,.26,2.0)
M['ink']=material('R2B01 / studio ivory',(.32,.33,.35),(.59,.60,.63),.2,.55)
M['backdrop']=material('R2B01 / charcoal stage',(.004,.006,.009),(.016,.024,.029),0,.9)
for color in ('red','blue','gold','silver','green'):
    base=M[color].diffuse_color[:3]
    M['face_'+color]=material('R2B01 / deep icon well '+color,tuple(c*.07 for c in base),tuple(c*.27 for c in base),0,.80)

def text_mesh(name,text,x,y,z,height,maxwidth=None,depth=.20,use_cn=False):
    cu=bpy.data.curves.new(name,'FONT');cu.body=text;cu.font=cnfont if use_cn else font
    cu.size=1;cu.align_x='CENTER';cu.align_y='CENTER';cu.extrude=depth/2;cu.resolution_u=8
    ob=bpy.data.objects.new(name,cu);active.objects.link(ob)
    bpy.context.view_layer.update()
    if ob.dimensions.y>0:
        s=height/ob.dimensions.y
        if maxwidth and ob.dimensions.x*s>maxwidth:s=maxwidth/ob.dimensions.x
        ob.scale=(s,s,1)
    ob.location=(x,y,z)
    bpy.ops.object.select_all(action='DESELECT');ob.select_set(True);bpy.context.view_layer.objects.active=ob
    bpy.ops.object.convert(target='MESH');bpy.ops.object.transform_apply(location=False,rotation=False,scale=True);ob.select_set(False)
    # Font 'CENTER' differs by font bearings. Center actual mesh bbox on intended location.
    xs=[v.co.x for v in ob.data.vertices];ys=[v.co.y for v in ob.data.vertices]
    if xs:
        cx=(min(xs)+max(xs))/2;cy=(min(ys)+max(ys))/2
        for v in ob.data.vertices:v.co.x-=cx;v.co.y-=cy
    return ob
def subtract(body,cutter,label):
    global cut_count
    initial=len(body.data.polygons)
    mod=body.modifiers.new('Actual recess '+label,'BOOLEAN');mod.operation='DIFFERENCE';mod.solver='EXACT';mod.object=cutter
    apply(body,mod)
    cut_count+=1
    cut_audit.append({'body':body.name,'feature':label,'before_faces':initial,'after_faces':len(body.data.polygons)})
    bpy.data.objects.remove(cutter,do_unlink=True)
def carved_text(body,text,x,y,face,height,maxwidth,state=None,use_cn=False):
    # Extruded cutter centered .025 below face reaches from face-.13 to face+.08.
    cut=text_mesh('CUTTER '+text,text,x,y,face-.025,height,maxwidth,.21,use_cn)
    if state:
        fill=cut.copy();fill.data=cut.data.copy();active.objects.link(fill);fill.name='R2B Magic in recessed '+text
        # Same silhouette, but thin geometry placed on cavity floor. Retains neutral cut walls.
        zs=[v.co.z for v in fill.data.vertices];bottom=min(zs);span=max(zs)-bottom
        for v in fill.data.vertices:v.co.z=(v.co.z-bottom)/span*.006
        fill.location.z=face-.127
        fill.data.materials.clear();fill.data.materials.append(M[state])
        fill['channel_depth_below_surface']=.121
    subtract(body,cut,'incised '+text)
def label(text,x,y,z=.0,height=.16,cn=True):
    ob=text_mesh('Studio caption '+text,text,x,y,z,height,depth=.002,use_cn=cn)
    ob.data.materials.append(M['ink']);return ob
def glyph_cut(body,kind,face):
    # Only the upper portion is used, leaving central numeral unaffected.
    cutters=[]
    zz=face-.012
    if kind=='range':
        for rr in (.13,.23):
            cutters.append(path('CUT ripple',[(rr*cos(i*tau/64),.35+rr*sin(i*tau/64),zz) for i in range(64)],.022,M['dark_metal'],True))
    elif kind=='distance':
        cutters.append(path('CUT target',[(.19*cos(i*tau/64),.37+.19*sin(i*tau/64),zz) for i in range(64)],.018,M['dark_metal'],True))
        cutters.append(path('CUT spear', [(-.20,.18,zz),(.19,.57,zz)],.024,M['dark_metal']))
        cutters.append(polygon('CUT spearpoint',[(.16,.43),(.27,.65),(.055,.545)],zz-.035,zz+.04,M['dark_metal'],0))
        for a in (0,pi/2,pi,3*pi/2):
            cutters.append(path('CUT target tick',[(.22*cos(a),.37+.22*sin(a),zz),(.28*cos(a),.37+.28*sin(a),zz)],.015,M['dark_metal']))
    elif kind=='skill':
        # A hand/spell crest uses a compact incised spark rather than a misleading attack sword.
        pts=[(0,.45),(.052,.12),(.28,0),(.052,-.085),(0,-.32),(-.052,-.085),(-.28,0),(-.052,.12)]
        cutters.append(polygon('CUT skill star',pts,face-.12,face+.08,M['dark_metal'],0))
    elif kind=='attack':
        cutters.append(path('CUT sword ridge',[(0,.54,zz),(0,.18,zz)],.013,M['dark_metal']))
        cutters.append(path('CUT sword guard',[(-.17,.20,zz),(.17,.20,zz)],.014,M['dark_metal']))
    elif kind=='shield':
        for sign in (-1,1):
            cutters.append(path('CUT shoulder engraving',[(sign*.36,.38,zz),(sign*.20,.43,zz),(sign*.08,.48,zz)],.011,M['dark_metal']))
    for cu in cutters:
        if cu.type=='CURVE':
            bpy.ops.object.select_all(action='DESELECT');cu.select_set(True);bpy.context.view_layer.objects.active=cu;bpy.ops.object.convert(target='MESH');cu.select_set(False)
        subtract(body,cu,'symbol '+kind)

OUTLINES={
'boot':[(-.32,.65),(.16,.65),(.18,.20),(.25,.04),(.53,-.08),(.55,-.38),(.40,-.50),(-.42,-.50),(-.45,-.33),(-.35,-.18)],
'shield':[(-.51,.46),(-.25,.56),(0,.69),(.25,.56),(.51,.46),(.47,-.24),(.30,-.49),(0,-.70),(-.30,-.49),(-.47,-.24)],
'attack':[(0,.77),(.18,.55),(.18,.23),(.44,.23),(.52,.09),(.44,-.025),(.32,-.025),(.32,-.43),(0,-.77),(-.32,-.43),(-.32,-.025),(-.44,-.025),(-.52,.09),(-.44,.23),(-.18,.23),(-.18,.55)],
'hourglass':[(-.57,.50),(.57,.50),(.57,.34),(.42,.26),(.35,0),(.42,-.26),(.57,-.34),(.57,-.50),(-.57,-.50),(-.57,-.34),(-.42,-.26),(-.35,0),(-.42,.26),(-.57,.34)],
'distance':[(0,.78),(.50,.33),(.50,-.32),(0,-.77),(-.50,-.32),(-.50,.33)],
'range':[(.58*cos(i*tau/80),.68*sin(i*tau/80)) for i in range(80)],
'skill':[(0,.75),(.25,.52),(.23,.38),(.46,.12),(.53,-.24),(.34,-.54),(0,-.70),(-.34,-.54),(-.53,-.24),(-.46,.12),(-.23,.38),(-.25,.52)],
}
def badge(kind,value,color='stone',state=None,name=None):
    start=set(active.objects)
    pts=OUTLINES[kind];key=name or kind
    polygon('R2B '+key+' solid forged socket',[(x*1.07,y*1.07) for x,y in pts],-.05,.07,M['dark_metal'],.025)
    polygon('R2B '+key+' bronze retention rim',[(x*1.04,y*1.04) for x,y in pts],.015,.115,M['bronze'],.020)
    body=polygon('R2B '+key+' cut stone',pts,.065,.29,M[color],0)
    # Cut the broad stone bevel BEFORE fine lettering, so narrow glyph edges
    # cannot clamp the entire outer silhouette's chamfer to nearly zero.
    chamfer=body.modifiers.new('Broad dressed stone bevel','BEVEL')
    chamfer.width=.047;chamfer.segments=3;apply(body,chamfer)
    # Real geometry cuts first; fine bevel after cuts keeps neutral light on numeral walls.
    if kind in ('range','distance'): glyph_cut(body,kind,.29);cy=-.24;height=.46;mw=.69
    elif kind=='hourglass':cy=0;height=.51;mw=.76
    elif kind=='boot':cy=-.03;height=.62;mw=.62
    elif kind=='attack':cy=-.25;height=.49;mw=.46
    else:cy=-.06;height=.72;mw=.73
    if value is not None:carved_text(body,str(value),0,cy,.29,height,mw,state)
    elif kind=='skill':glyph_cut(body,'skill',.29)
    if kind in ('attack','shield'):glyph_cut(body,kind,.29)
    finish(body,.008)
    return list(set(active.objects)-start)
def parent_objects(objs,name,loc=(0,0,0),scale=1):
    ob=bpy.data.objects.new(name,None);active.objects.link(ob)
    for part in objs:part.parent=ob
    ob.location=loc;ob.scale=(scale,scale,scale)
    return ob
def disc(color,name,kind,values,location,full=True):
    start=set(active.objects)
    circ('R2B disc underbody',1.51,-.20,.19,M['dark_metal'])
    annulus('R2B machined channel bed',1.49,1.24,-.08,.15,M['bronze'])
    annulus('R2B narrow outer bronze rim',1.49,1.425,.04,.12,M['bronze'])
    annulus('R2B outer polished edge',1.49,1.455,.125,.03,M['edge'])
    annulus('R2B inset card color stone band',1.423,1.285,.08,.075,M[color])
    annulus('R2B inner turned bronze lip',1.28,1.24,.035,.08,M['bronze'])
    circ('R2B recessed icon backing',1.245,-.04,.10,M['face_'+color])
    # Four symmetric clamps with carefully aligned geometry.
    for i in range(4):
        a=pi/4+i*pi/2
        riv=circ('R2B housing rivet',.035,.155,.025,M['edge']);riv.location.x=1.443*cos(a);riv.location.y=1.443*sin(a)
    for i in range(36):
        a=i*tau/36
        path('R2B rim incision',[(1.413*cos(a),1.413*sin(a),.145),(1.457*cos(a),1.457*sin(a),.145)],.005,M['dark_metal'])
    namepts=[(-.85,-.30),(.85,-.30),(.95,-.42),(.80,-.73),(-.80,-.73),(-.95,-.42)]
    plate=polygon('R2B engraved skill name stone',namepts,.08,.225,M['name'],0)
    chamfer=plate.modifiers.new('Name stone dressed edges','BEVEL');chamfer.width=.03;chamfer.segments=3;apply(plate,chamfer)
    carved_text(plate,name,0,-.505,.225,.265,1.60,use_cn=True);finish(plate,.016)
    if full:
        parts=build_relief(kind,{'bronze':M['bronze'],'dark_metal':M['dark_metal'],'stone':M['stone'],'rune':M['rune'],'silver':M['silver']},radius=1.25)
        for ob in parts:
            for co in list(ob.users_collection):co.objects.unlink(ob)
            active.objects.link(ob)
    else:
        # Editable empty artwork slot, deliberately not a fake third card illustration.
        annulus('R2B blank icon socket',.49,.47,.14,.018,M['bronze']).location.y=.36
    cfg=[('boot',values[0],'stone',(-1.00,.83,.20),.56),
         ('shield',values[1],'stone',(1.00,.83,.20),.56),
         ('attack' if kind=='boomerang' else 'skill',values[2],color,(-.98,-.66,.20),.55),
         ('distance' if kind=='boomerang' else 'range',values[3],color,(.98,-.66,.20),.55),
         ('hourglass',values[4],'stone',(0,-1.17,.21),.53)]
    for typ,value,c,loc,scale in cfg:
        parts=badge(typ,value,c);parent_objects(parts,'R2B '+name+' '+typ,loc,scale)
    objs=list(set(active.objects)-start)
    # Parent only roots to avoid losing badge nested transforms.
    roots=[o for o in objs if o.parent is None]
    rig=parent_objects(roots,'R2B_SKILL_FIXED_'+name,location)
    rig['sample_only']=True;rig['canonical_values']=str(values)
    return rig
def scene_new(name):
    sc=bpy.data.scenes.new(name);bpy.context.window.scene=sc;scenes[name]=sc
    sc.render.engine='CYCLES';sc.cycles.samples=48;sc.cycles.use_denoising=True
    sc.render.resolution_x=1400;sc.render.resolution_y=1100;sc.render.resolution_percentage=100
    sc.render.image_settings.file_format='PNG';sc.render.film_transparent=False
    sc.render.fps=24;sc.frame_start=1;sc.frame_end=192
    sc.view_settings.view_transform='AgX'
    sc.world=bpy.data.worlds.new(name+' world');sc.world.use_nodes=True
    sc.world.node_tree.nodes['Background'].inputs[0].default_value=(.08,.105,.14,1)
    sc.world.node_tree.nodes['Background'].inputs[1].default_value=.16
    return sc
def camera_light(sc,scale,target=(0,0,0),tilt=.14):
    global active
    active=collection('STUDIO '+sc.name,sc)
    cam=bpy.data.objects.new('Camera '+sc.name,bpy.data.cameras.new(sc.name));active.objects.link(cam)
    cam.location=(target[0],target[1]-tilt*12,14)
    direction=Vector(target)-cam.location;cam.rotation_euler=direction.to_track_quat('-Z','Y').to_euler()
    cam.data.type='ORTHO';cam.data.ortho_scale=scale;sc.camera=cam
    for name,loc,energy,size,color in [
        ('Broad warm key',(-4,5,7),950,3.0,(1,.83,.64)),
        ('Cool fill',(4,1,5),250,4.0,(.57,.72,1)),
        ('Top edge',(1,5,4),330,3,(1,.95,.82))]:
        light=bpy.data.lights.new(name,'AREA');light.energy=energy;light.shape='DISK';light.size=size;light.color=color
        ob=bpy.data.objects.new(name,light);active.objects.link(ob);ob.location=loc
        ob.rotation_euler=(Vector(target)-ob.location).to_track_quat('-Z','Y').to_euler()
    polygon('STUDIO matte backdrop',[(-30,-30),(30,-30),(30,30),(-30,30)],-1.9,-1.8,M['backdrop'],0)
    return cam

# Distinct scenes make each part selectable. Component scene is not one merged mesh.
sc=scene_new('01_COMPLETE_SKILLS');active=collection('R2B_COMPLETE_SKILLS',sc)
red=disc('red','回旋镖','boomerang',[4,3,3,3,9],(-1.85,.55,0))
blue=disc('blue','控物','telekinesis',[3,5,None,2,9],(1.85,.55,0))
label('R2B 01 · 凹刻模型样板',0,2.63,.1,.28)
label('原创立体技能图标 / 灰石数值 / 牌色主行动与距离',0,-1.55,.1,.145)
for idx,(val,state,cap) in enumerate([(6,None,'原值'),(8,'buff','增加'),(4,'debuff','减少')]):
    objs=badge('shield',val,'stone',state);parent_objects(objs,'Sample state '+cap,((idx-1)*1.55,-2.53,.0),.56)
    label(cap,(idx-1)*1.55,-3.12,.04,.16)
cam=camera_light(sc,8.0,target=(0,-.2,0),tilt=.14)
sc.render.resolution_y=1240
# Slow, modest angle changes expose genuine thickness without making names unreadable.
for ob in (red,blue):
    for frame,angle in [(1,-.10),(49,.16),(97,-.10)]:
        ob.rotation_euler[1]=angle;ob.keyframe_insert('rotation_euler',frame=frame)
sc.frame_end=96;sc.frame_set(30)

sc=scene_new('02_BADGE_COMPONENTS');active=collection('R2B_BADGE_LIBRARY',sc)
entries=[('boot',4,'stone',None,'移动'),('shield',6,'stone',None,'防御'),('hourglass',12,'stone',None,'先攻'),
('attack',3,'red',None,'主要攻击'),('skill',None,'blue',None,'主要技能'),('range',2,'blue',None,'范围'),
('distance',3,'red',None,'攻击距离'),('hourglass','-2','stone',None,'负先攻'),('shield','∞','stone',None,'条件防御'),
('attack',4,'red','buff','红牌 · 增加'),('skill',2,'green','debuff','绿牌 · 减少'),('shield',8,'stone','buff','灰石 · 增加')]
for i,(kind,value,c,state,cap) in enumerate(entries):
    x=(i%3-1)*2.08;y=3.04-(i//3)*2.1
    ob=parent_objects(badge(kind,value,c,state),'COMPONENT '+cap,(x,y,0),1)
    label(cap,x,y-.96,.01,.165)
label('独立石标 · 真实内凹几何',0,4.45,.02,.26)
camera_light(sc,9.70,target=(0,.0,0),tilt=.10)
sc.render.resolution_x=1260;sc.render.resolution_y=1680

sc=scene_new('03_R2_ROTATING_ASSEMBLY');active=collection('R2_FIXED_SKILLS',sc)
for i,(c,name,kind,vals,full) in enumerate([
('gold','图标插槽','boomerang',[None]*5,False),('silver','图标插槽','telekinesis',[None]*5,False),
('red','回旋镖','boomerang',[4,3,3,3,9],True),('green','图标插槽','telekinesis',[None]*5,False),
('blue','控物','telekinesis',[3,5,None,2,9],True)]):
    a=math.radians(90-i*72)
    ob=disc(c,name,kind,vals,(4.2*cos(a),4.2*sin(a),.15),full);ob.scale=(.72,)*3
ring=build_ring({'bronze':M['bronze'],'dark_metal':M['dark_metal'],'rune':M['rune']},radius=4.2,z=-.25,frame_end=480,fps=24)
active=collection('R2_ASSEMBLY_CAPTIONS',sc)
label('R2 · 五技能固定 / 装饰分层旋转',0,.28,.0,.28)
label('两枚完整技能样板，三枚空白插槽',0,-.30,0,.19)
label('Blender 模型演示 · 尚未接入游戏',0,-5.58,0,.16)
camera_light(sc,12.6,target=(0,0,0),tilt=.055)
sc.render.resolution_x=1320;sc.render.resolution_y=1320;sc.frame_end=192

sc=scene_new('04_GROOVE_MACRO');active=collection('R2B_MACRO',sc)
ob=parent_objects(badge('attack',4,'red','buff'),'Red attack green actual groove',(-.90,.3,0),1.24)
ob.rotation_euler=(math.radians(6),math.radians(-15),math.radians(-8))
ob2=parent_objects(badge('shield',4,'stone','debuff'),'Grey shield red actual groove',(.95,.3,0),1.24)
ob2.rotation_euler=(math.radians(6),math.radians(13),math.radians(8))
label('石材不变色 · 魔力位于数字槽底',0,-1.16,0,.19)
camera_light(sc,4.75,target=(0,.1,0),tilt=.19);sc.render.resolution_x=1440;sc.render.resolution_y=920

# Default CPU: this machine has a visible CUDA device but kernel_sm_86 failed
# to load. Do not change system security or drivers just to render a sample.
device_report=[]
try:
    pref=bpy.context.preferences.addons['cycles'].preferences
    pref.compute_device_type='OPTIX';pref.get_devices()
    if not any(dev.type=='OPTIX' for dev in pref.devices):
        pref.compute_device_type='CUDA';pref.get_devices()
    for dev in pref.devices:
        dev.use=args.gpu and dev.type in ('OPTIX','CUDA');device_report.append({'name':dev.name,'type':dev.type,'enabled':dev.use})
    gpu=any(d['enabled'] for d in device_report)
except Exception:gpu=False
for sc in scenes.values():sc.cycles.device='GPU' if gpu else 'CPU'
# Include explicit editable source metadata without packing proprietary system fonts.
bpy.context.window.scene=scenes['01_COMPLETE_SKILLS']
for area in bpy.context.screen.areas:
    if area.type=='VIEW_3D':
        area.spaces.active.region_3d.view_perspective='CAMERA'
        area.spaces.active.shading.type='MATERIAL'
source.parent.mkdir(parents=True,exist_ok=True)
notes=bpy.data.texts.new('READ ME - R2B sample scope')
notes.write('R2B01 actual geometry sample. Scenes 01 complete skills; 02 semantic stone library; 03 fixed skill mounts and animated decorative ring; 04 carved groove macro. No Unity integration. Icons are original 3D relief samples. Numbers and Chinese names are actual boolean cuts for this sample; production live values need a reusable glyph/shader interface. Material nodes are Blender-specific; FBX/GLB cannot automatically match them. Rebuild only with owned generator; never modify Workbench.blend.')
bpy.ops.wm.save_as_mainfile(filepath=str(source),compress=True)
# Inspect actual topology, not a test claiming artistic acceptance.
stats={'source':str(source.relative_to(root)),'blender':bpy.app.version_string,'gpu':device_report,
 'boolean_cuts':cut_count,'cuts':cut_audit,'scenes':{},'runtime_changed':False,'unity_tested':False,
 'external_image_textures':len(bpy.data.images),'ring_metadata':ring.get('animation_metadata',{})}
for name,sc in scenes.items():
    deps=bpy.context.evaluated_depsgraph_get()
    stats['scenes'][name]={'objects':len(sc.objects),'meshes':sum(o.type=='MESH' for o in sc.objects),
      'vertices':sum(len(o.data.vertices) for o in sc.objects if o.type=='MESH'),
      'frames':[sc.frame_start,sc.frame_end],'fps':sc.render.fps}
(out/'model-report.json').write_text(json.dumps(stats,ensure_ascii=False,indent=2,default=str),encoding='utf-8')
for name,sc in scenes.items():
    bpy.context.window.scene=sc
    sc.render.filepath=str(out/(name+'.png'))
    if args.preview_only:
        sc.render.resolution_percentage=55;sc.cycles.samples=24
    if not args.no_render:bpy.ops.render.render(write_still=True)
print('R2B_MODEL_DONE '+str(source))
