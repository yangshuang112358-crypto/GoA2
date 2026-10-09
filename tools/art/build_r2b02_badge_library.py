"""Finished-style semantic badge set using the approved R2B01 material direction.

Loads the previous single button as a material/source donor, edits only a new
output .blend, and also fixes the two reported parts on a copy of that button.
"""
import argparse, json, math, sys
from pathlib import Path
import bpy
from mathutils import Vector

p=argparse.ArgumentParser()
p.add_argument('--root',type=Path,required=True)
p.add_argument('--out',type=Path,required=True)
p.add_argument('--replace-generated',action='store_true')
p.add_argument('--preview',action='store_true')
p.add_argument('--no-render',action='store_true')
p.add_argument('--only',default='')
a=p.parse_args(sys.argv[sys.argv.index('--')+1:])
root=a.root.resolve();out=a.out.resolve();out.mkdir(parents=True,exist_ok=True)
source=root/'art/production/samples/R2B02-Badges/R2B02_SemanticBadges.blend'
donor=root/'art/production/samples/R2B01-Hero/R2B01_Boomerang.blend'
if source.exists() and not a.replace_generated:raise RuntimeError('Existing model; use explicit --replace-generated only for a generated rebuild.')
bpy.ops.wm.open_mainfile(filepath=str(donor))
bpy.context.preferences.filepaths.save_version=0
sys.path.insert(0,str(root/'tools/art'))
from r2b02_stones import build_badge, glyph


def material(prefix):
    return next(m for m in bpy.data.materials if m.name.startswith(prefix))


M={
    'stone':material('01 Graphite'), 'edge':material('02 Pale'), 'dark':material('05 Blackened'),
    'red':material('03 Iron-red'), 'red_edge':material('04 Red dressed'), 'metal':material('07 Forged'),
    'bronze':material('08 Antique'), 'cavity':material('06b Deep'),
    'green_magic':material('12 Green'), 'red_magic':material('13 Red'), 'ground':material('90 Studio')
}


def tinted(key,dark,light):
    for suffix,base,mul in [('',M['red'],1),('_edge',M['red_edge'],1.6)]:
        ma=base.copy();ma.name='R2B02 '+key+suffix+' / card stone'
        ramp=next(n for n in ma.node_tree.nodes if n.type=='VALTORGB')
        ramp.color_ramp.elements[0].color=(*[min(1,c*mul) for c in dark],1)
        ramp.color_ramp.elements[1].color=(*[min(1,c*mul) for c in light],1)
        ma.diffuse_color=(*light,1);M[key+suffix]=ma


tinted('blue',(.008,.023,.045),(.025,.100,.20))
tinted('green',(.009,.031,.014),(.045,.15,.068))
tinted('gold',(.066,.037,.007),(.26,.16,.036))
tinted('silver',(.041,.048,.057),(.15,.18,.22))
tinted('purple',(.032,.010,.055),(.14,.043,.24))


def plain(name,color,metal=0,rough=.4):
    ma=bpy.data.materials.new(name);ma.use_nodes=True;ma.diffuse_color=(*color,1)
    bs=next(n for n in ma.node_tree.nodes if n.type=='BSDF_PRINCIPLED');bs.inputs['Base Color'].default_value=(*color,1)
    bs.inputs['Metallic'].default_value=metal;bs.inputs['Roughness'].default_value=rough
    return ma


M['glass']=plain('R2B02 Hourglass / actual crystal vessels',(.17,.30,.36),0,.20)
bs=next(n for n in M['glass'].node_tree.nodes if n.type=='BSDF_PRINCIPLED');bs.inputs['Transmission Weight'].default_value=.64;bs.inputs['IOR'].default_value=1.46
M['sand']=plain('R2B02 Hourglass / warm sand',(.50,.26,.06),.30,.53)
M['glow']=plain('R2B02 Spell crest / restrained arcane enamel',(.021,.095,.18),.30,.27)
bs=next(n for n in M['glow'].node_tree.nodes if n.type=='BSDF_PRINCIPLED');bs.inputs['Emission Color'].default_value=(.025,.21,.46,1);bs.inputs['Emission Strength'].default_value=.40
M['ultimate_glow']=plain('R2B02 Ultimate crest / violet enamel',(.10,.022,.23),.30,.27)
bs=next(n for n in M['ultimate_glow'].node_tree.nodes if n.type=='BSDF_PRINCIPLED');bs.inputs['Emission Color'].default_value=(.22,.06,.60,1);bs.inputs['Emission Strength'].default_value=.45
M['caption']=plain('R2B02 Display labels / warm ivory',(.58,.55,.47),.1,.65)
bs=next(n for n in M['caption'].node_tree.nodes if n.type=='BSDF_PRINCIPLED');bs.inputs['Emission Color'].default_value=(.38,.34,.27,1);bs.inputs['Emission Strength'].default_value=.6
FONT=bpy.data.fonts.load('C:/Windows/Fonts/georgiab.ttf')
CN=bpy.data.fonts.load('C:/Windows/Fonts/simsun.ttc')
SCENES={};ROOTS=[]


def register(scene):
    SCENES[scene.name]=scene
    return scene


# Corrected complete button is its own genuine model scene, not a composited image.
base=bpy.context.scene;base.name='00_CORRECTED_BOOMERANG';register(base)
base.frame_set(1)
assembly=bpy.data.objects['BUTTON / rotate this complete assembly'];assembly.rotation_euler=(0,0,0)
coll=bpy.data.collections['01 / Editable button components']
remove=[]
for ob in list(coll.objects):
    if ob.name.startswith(('Movement /','Initiative /','Defence /','Primary /','Attack distance /','Distance /','VARIANT ')):remove.append(ob)
    elif ob.name.startswith('Actual recessed patina floor'):
        if '回旋镖' not in ob.name:remove.append(ob)
for ob in remove:bpy.data.objects.remove(ob,do_unlink=True)
for kind,val,loc,color in [('boot',4,(-1.91,1.66,0),'stone'),('shield',3,(1.91,1.57,0),'stone'),
                          ('sword',3,(-2.05,-1.08,0),'red'),('distance',3,(2.05,-1.06,0),'red'),
                          ('hourglass',9,(0,-2.16,0),'stone')]:
    ob=build_badge(kind,val,coll,M,FONT,tag='CORRECTED '+kind,stone_key=color,edge_key='edge' if color=='stone' else color+'_edge')
    ob.parent=assembly;ob.location=loc;ROOTS.append(ob)
base.render.resolution_x=1280;base.render.resolution_y=1280


def new_scene(name,scale,size=(1280,1280),target=(0,0,0)):
    sc=bpy.data.scenes.new(name);bpy.context.window.scene=sc;register(sc)
    col=bpy.data.collections.new(name+' / separate editable badge roots');sc.collection.children.link(col)
    studio=bpy.data.collections.new(name+' / presentation');sc.collection.children.link(studio)
    sc.render.engine='CYCLES';sc.cycles.device='CPU';sc.cycles.samples=64;sc.cycles.use_denoising=True;sc.cycles.max_bounces=8
    sc.render.resolution_x,sc.render.resolution_y=size;sc.render.resolution_percentage=100;sc.render.image_settings.file_format='PNG'
    sc.view_settings.view_transform='AgX';sc.view_settings.look='AgX - Medium High Contrast'
    sc.world=base.world
    sc.render.fps=24;sc.frame_start=1;sc.frame_end=120
    cam=bpy.data.objects.new(name+' camera',bpy.data.cameras.new(name+' camera'));studio.objects.link(cam)
    cam.data.type='ORTHO';cam.data.ortho_scale=scale;cam.location=(target[0],target[1]-1,20)
    cam.rotation_euler=(Vector(target)-cam.location).to_track_quat('-Z','Y').to_euler();sc.camera=cam
    for nm,loc,energy,sz,color in [
        ('Warm grazing key',(-4,6,7),820,3.5,(1,.88,.72)),
        ('Cool reflected fill',(4,2,7),270,5.0,(.60,.76,1)),
        ('Crest light',(0,5,4.5),240,3.0,(1,.95,.85))]:
        ld=bpy.data.lights.new(name+nm,'AREA');ld.energy=energy;ld.size=sz;ld.color=color;ld.shape='DISK'
        ob=bpy.data.objects.new(name+nm,ld);studio.objects.link(ob);ob.location=loc
        ob.rotation_euler=(Vector(target)-ob.location).to_track_quat('-Z','Y').to_euler()
    me=bpy.data.meshes.new(name+' ground');me.from_pydata([(-50,-50,-.15),(50,-50,-.15),(50,50,-.15),(-50,50,-.15)],[],[(0,1,2,3)])
    ob=bpy.data.objects.new(name+' ground',me);studio.objects.link(ob);me.materials.append(M['ground'])
    sc.use_nodes=True;n=sc.node_tree.nodes;l=sc.node_tree.links;n.clear()
    rl=n.new('CompositorNodeRLayers');gl=n.new('CompositorNodeGlare');gl.glare_type='FOG_GLOW';gl.threshold=1.2;gl.size=7;gl.mix=-.75
    composite=n.new('CompositorNodeComposite');l.new(rl.outputs[0],gl.inputs[0]);l.new(gl.outputs[0],composite.inputs[0])
    return sc,col,studio


def label(text,x,y,collection,size=.16,width=7):
    ob=glyph(text,(x,y,.05),size,width,collection,CN,depth=.002)
    ob.name='Presentation label / '+text;ob.data.materials.append(M['caption'])
    return ob


def item(coll,kind,value,name,loc,scale=1,color='stone',state='normal'):
    ob=build_badge(kind,value,coll,M,FONT,tag=name,stone_key=color,edge_key='edge' if color=='stone' else color+'_edge',state=state)
    ob.location=loc;ob.scale=(scale,)*3;ROOTS.append(ob)
    return ob


sc,c,studio=new_scene('01_BOOT_AND_HOURGLASS',6.0,(1360,1120),target=(0,.05,0))
label('移动与先攻 · 独立内部徽记',0,1.89,studio,.25)
item(c,'boot',4,'BOOT / sculpted side silhouette',(-1.45,0,0),1.65)
item(c,'hourglass',9,'HOURGLASS / narrow vertical silhouette',(1.45,0,0),1.65)
label('移动',-1.40,-1.59,studio,.20);label('先攻',1.45,-1.59,studio,.20)

sc,c,studio=new_scene('02_SEVEN_CORE_BADGES',8.0,(1400,1450),target=(0,0,0))
label('七种通用数值标记',0,3.56,studio,.27)
entries=[('boot',4,'移动','stone'),('shield',3,'防御','stone'),('sword',3,'攻击','red'),
         ('skill',None,'技能','blue'),('range',2,'范围','blue'),('distance',3,'攻击距离','red'),
         ('hourglass',9,'先攻','stone')]
for i,(kind,val,title,color) in enumerate(entries):
    x=(i%3-1)*2.15 if i<6 else 0;y=2.12-(i//3)*2.28
    item(c,kind,val,'CORE '+kind,(x,y,0),1.08,color)
    label(title,x,y-1.13,studio,.16)

sc,c,studio=new_scene('03_CARD_COLORS_AND_MAGIC',7.6,(1400,1480),target=(0,.0,0))
label('牌色复用 · 数字槽内魔力',0,3.60,studio,.27)
for i,(kind,color,title,val) in enumerate([
    ('sword','gold','金色主要行动',3),('shield','silver','银色主要防御',6),('sword','red','红色主要行动',3),
    ('boot','green','绿色主要移动',4),('range','blue','蓝色技能范围',2),('skill','purple','紫色技能标记',None)]):
    x=(i%3-1)*2.30;y=2.20-(i//3)*2.27
    item(c,kind,val,'COLOR '+color,(x,y,0),1.04,color)
    label(title,x,y-1.08,studio,.13)
for i,(v,state,title) in enumerate([(3,'normal','原值'),(4,'green_magic','增加'),(2,'red_magic','减少')]):
    x=(i-1)*2.3;y=-2.32
    item(c,'sword',v,'MAGIC '+state,(x,y,0),1.04,'red',state)
    label(title,x,y-1.08,studio,.14)

sc,c,studio=new_scene('04_COMPLETE_NUMERIC_FORMS',8.9,(1600,1300),target=(0,0,0))
label('凹刻字形 · 双位数 · 负数 · 条件防御',0,3.02,studio,.23)
values=list('0123456789')+['12','13','−2','∞','!']
for i,val in enumerate(values):
    x=(i%5-2)*1.68;y=1.82-(i//5)*1.89
    kind='hourglass' if val in ('12','13','−2') else 'shield'
    item(c,kind,val,'GLYPH '+val,(x,y,0),.84)
    if val=='!':label('兼容字形',x,y-.86,studio,.12)
    elif val=='∞':label('条件防御',x,y-.86,studio,.12)
label('此页为字形工艺，不表示实际卡牌数值',0,-3.00,studio,.16)

sc,c,studio=new_scene('05_ACTION_AND_HISTORY_MARKS',7.8,(1400,1080),target=(0,.0,0))
label('行动与记录标记 · 不占数值槽',0,2.45,studio,.24)
extras=[('discard','弃置','stone'),('recover','取回','stone'),('reaction','响应行动','stone'),
        ('quickmove','快速移动','stone'),('ultimate','紫卡触发','purple')]
for i,(kind,title,color) in enumerate(extras):
    x=(i%3-1)*2.18 if i<3 else (i-3.5)*2.30;y=1.11 if i<3 else -1.22
    item(c,kind,None,'ACTION '+kind,(x,y,0),1.18,color)
    label(title,x,y-.98,studio,.16)

# Rotate the two corrected badges, leaving the presentation labels stationary.
sc=SCENES['01_BOOT_AND_HOURGLASS'];bpy.context.window.scene=sc
for ob in sc.objects:
    if ob.type=='EMPTY' and ob.get('semantic'):
        for f,angle in [(1,0),(31,-.24),(61,0),(91,.24),(121,0)]:
            ob.rotation_euler=(0,angle,0);ob.keyframe_insert('rotation_euler',frame=f)
sc.frame_set(1)
for area in bpy.context.screen.areas:
    if area.type=='VIEW_3D':area.spaces.active.region_3d.view_perspective='CAMERA';area.spaces.active.shading.type='MATERIAL'
notes=bpy.data.texts.new('READ ME / R2B02 semantic library')
notes.write('Seven stat semantics: movement, defense, attack, skill, initiative, skill range, attack distance. Each has an independent 3D inner emblem. Boot side silhouette and narrow vertical hourglass are corrected on the complete Boomerang copy too. 0-9, 12,13,minus2,infinity and legacy exclamation are real incisions. Infinity is conditional defense, not unconditional immunity. Skill with no value has no invented zero. Five history/action emblems have no numeric slots. Six card stone colors reuse geometry; red/green magic affects numbers only. No Unity integration. Source/render generation uses tools/art/build_r2b02_badge_library.py, r2b02_stones.py, r2b02_emblems.py. Workbench and gameplay remain untouched.')
source.parent.mkdir(parents=True,exist_ok=True)
bpy.ops.wm.save_as_mainfile(filepath=str(source),compress=True)
report={'source':str(source.relative_to(root)).replace('\\','/'),'donor':str(donor.relative_to(root)).replace('\\','/'),
        'blender':bpy.app.version_string,'semantic_kinds':[x[0] for x in entries],
        'utility_kinds':[x[0] for x in extras], 'roots':[{'name':o.name,'semantic':o['semantic'],'value':o['value'],'width':o['shape_width'],'height':o['shape_height'],'inside_emblem':o['inside_emblem']} for o in ROOTS],
        'scenes':list(SCENES),'unity_integrated':False,'art_approved':False}
(out/'model-report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf8',newline='\n')
if not a.no_render:
    for name,sc in SCENES.items():
        if a.only and name not in a.only.split(','):continue
        bpy.context.window.scene=sc;sc.frame_set(1)
        if a.preview:sc.render.resolution_percentage=55;sc.cycles.samples=24
        sc.render.filepath=str(out/(name+'.png'));bpy.ops.render.render(write_still=True)
        print('R2B02_RENDER_DONE '+name,flush=True)
print('R2B02_LIBRARY_SAVED '+str(source),flush=True)
