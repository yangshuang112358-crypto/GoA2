"""Blender 4.5: original stylized Goa2 minions, editable rigs, FBX and preview.

blender -b --python tools/art/build_minions.py -- --root <Goa2V1>
Front is Blender -Y, feet at Z=0. No downloaded geometry or textures.
"""
import argparse
import json
import math
import sys
from pathlib import Path

import bpy
from mathutils import Vector

args = argparse.ArgumentParser()
args.add_argument('--root', required=True)
args = args.parse_args(sys.argv[sys.argv.index('--') + 1:])
ROOT = Path(args.root)
SOURCE = ROOT / 'art/minions'
EXPORT = ROOT / 'unity/Assets/Scripts/UI3D/Resources/UI3D/Minions'
PREVIEW = ROOT / 'artifacts/minion-models'
for path in (SOURCE, EXPORT, PREVIEW):
    path.mkdir(parents=True, exist_ok=True)
bpy.context.preferences.filepaths.save_version=0
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)

PALETTE = {
    'TeamCloth': ('#276997', .0, .72),
    'TeamInset': ('#459DC0', .12, .5),
    'Steel': ('#586B80', .62, .4),
    'SteelLight': ('#A1B2BB', .48, .38),
    'Bronze': ('#B18A4B', .55, .42),
    'Leather': ('#493E3B', .0, .87),
    'Shadow': ('#101E2D', .0, .9),
    'Glow': ('#B4EEFF', .1, .28),
    'Parchment': ('#C9B68D', .0, .95),
}


def rgb(code):
    # Blender shader colors are scene-linear; palette hex codes are sRGB.
    values = [int(code[i:i+2], 16) / 255 for i in (1, 3, 5)]
    return tuple(v / 12.92 if v <= .04045 else ((v + .055) / 1.055) ** 2.4 for v in values)


materials = {}
for name, (color, metallic, roughness) in PALETTE.items():
    m = bpy.data.materials.new(name)
    m.diffuse_color = (*rgb(color), 1)
    m.use_nodes = True
    p = next(n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    p.inputs['Base Color'].default_value = m.diffuse_color
    p.inputs['Metallic'].default_value = metallic
    p.inputs['Roughness'].default_value = roughness
    if name == 'Glow':
        p.inputs['Emission Color'].default_value = m.diffuse_color
        p.inputs['Emission Strength'].default_value = .45
    materials[name] = m

parts = []
collection = None


def finish(obj, name, material, bone='Spine', bevel=0):
    obj.name = name
    for c in list(obj.users_collection):
        c.objects.unlink(obj)
    collection.objects.link(obj)
    obj.data.materials.append(materials[material])
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if bevel:
        mod = obj.modifiers.new('Forged edge bevel', 'BEVEL')
        mod.width = bevel
        mod.segments = 2
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.modifier_apply(modifier=mod.name)
        normal = obj.modifiers.new('Weighted face normals', 'WEIGHTED_NORMAL')
        normal.keep_sharp = True
        bpy.ops.object.modifier_apply(modifier=normal.name)
    group = obj.vertex_groups.new(name=bone)
    group.add(list(range(len(obj.data.vertices))), 1, 'REPLACE')
    parts.append(obj)
    return obj


def box(name, at, size, material, bone='Spine', bevel=.025, rotation=(0,0,0)):
    bpy.ops.mesh.primitive_cube_add(size=1, location=at, rotation=rotation)
    obj = bpy.context.object
    obj.scale = size
    return finish(obj,name,material,bone,bevel)


def sphere(name, at, size, material, bone='Spine', segments=16, rings=8):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=segments, ring_count=rings, radius=1, location=at)
    obj = bpy.context.object
    obj.scale = size
    return finish(obj,name,material,bone)


def cone(name, at, r1, r2, depth, material, bone='Spine', vertices=12):
    bpy.ops.mesh.primitive_cone_add(vertices=vertices, radius1=r1, radius2=r2, depth=depth, location=at)
    return finish(bpy.context.object,name,material,bone,.008)


def rod(name, a, b, radius, material, bone='Spine', vertices=10):
    a,b = Vector(a),Vector(b)
    obj = cone(name,(a+b)/2,radius,radius,(b-a).length,material,bone,vertices)
    obj.rotation_euler=(b-a).to_track_quat('Z','Y').to_euler()
    return obj


def prism(name, outline, front, back, material, bone='Spine', bevel=.015):
    # Extruded XZ polygon, front faces toward -Y.
    verts=[(x,front,z) for x,z in outline]+[(x,back,z) for x,z in outline]
    n=len(outline)
    faces=[tuple(reversed(range(n))),tuple(range(n,2*n))]
    faces += [(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)]
    mesh=bpy.data.meshes.new(name);mesh.from_pydata(verts,[],faces);mesh.update()
    obj=bpy.data.objects.new(name,mesh);collection.objects.link(obj)
    bpy.ops.object.select_all(action='DESELECT');obj.select_set(True);bpy.context.view_layer.objects.active=obj
    # Recalculate consistent outward winding before the bevel pass.
    bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT');bpy.ops.mesh.normals_make_consistent(inside=False);bpy.ops.object.mode_set(mode='OBJECT')
    return finish(obj,name,material,bone,bevel)


def stripe(name, points, radius, material, bone='Spine'):
    for i in range(len(points)-1):rod(name+str(i),points[i],points[i+1],radius,material,bone,8)


def lathe(name, profiles, material, bone='Spine', segments=16, folds=0):
    verts=[]
    for z,r in profiles:
        for j in range(segments):
            a=2*math.pi*j/segments
            rr=r*(1+folds*(1 if j%2 else -1))
            verts.append((rr*math.cos(a),rr*math.sin(a)*.76,z))
    faces=[]
    for k in range(len(profiles)-1):
        for j in range(segments):
            a=k*segments+j;b=k*segments+(j+1)%segments
            faces.append((a,b,b+segments,a+segments))
    faces+=[tuple(reversed(range(segments))),tuple((len(profiles)-1)*segments+j for j in range(segments))]
    mesh=bpy.data.meshes.new(name);mesh.from_pydata(verts,[],faces);mesh.update()
    obj=bpy.data.objects.new(name,mesh);collection.objects.link(obj)
    bpy.ops.object.select_all(action='DESELECT');obj.select_set(True);bpy.context.view_layer.objects.active=obj
    return finish(obj,name,material,bone)


def rivet(at, bone='Spine'):
    return sphere('Hand-set rivet',at,(.024,.015,.024),'Bronze',bone,8,4)


def diamond(name, x,y,z,w,h, material, bone='Spine'):
    return prism(name,[(x,z+h/2),(x+w/2,z),(x,z-h/2),(x-w/2,z)],y-.02,y+.018,material,bone,.007)


def eyes(z,y,width=.1):
    for side in (-1,1):
        x=side*width
        prism('Luminous visor eye',[(x-.060,z+.019),(x+.054,z+.012),(x+.043,z-.020),(x-.04,z-.026)],y,y+.018,'Glow','Head',.004)


def legs(heavy=False):
    spread=.225 if heavy else .165
    for side,sign in [('L',-1),('R',1)]:
        x=spread*sign
        box('Broad square boot '+side,(x,-.07,.15 if heavy else .12),(.32 if heavy else .23,.40 if heavy else .31,.3 if heavy else .24),'Steel' if heavy else 'Leather','Foot.'+side,.035)
        box('Boot toe cap '+side,(x,-.23 if heavy else -.20,.17 if heavy else .14),(.31 if heavy else .23,.09,.15),'Bronze' if heavy else 'Steel','Foot.'+side,.02)
        rod('Leg '+side,(x,0,.18),(x,.02,.65 if heavy else .56),.105 if heavy else .085,'Shadow','LowerLeg.'+side)
        prism('Shin plate '+side,[(x-.125,.27),(x-.14,.49),(x,.59),(x+.14,.49),(x+.125,.27)],-.17,-.055,'SteelLight','LowerLeg.'+side)
        if heavy:diamond('Greave inset',x,-.19,.43,.10,.16,'TeamInset','LowerLeg.'+side)


def melee():
    legs()
    lathe('Pleated armored tunic',[(.38,.29),(.49,.31),(.72,.24),(.98,.31)],'TeamCloth',folds=.05)
    sphere('Rounded cuirass',(0,0,.88),(.30,.23,.26),'Steel')
    lathe('Waist belt',[(.59,.285),(.68,.285)],'Leather')
    box('Belt buckle',(0,-.238,.64),(.15,.055,.115),'Bronze',bevel=.014)
    prism('Chest bronze border',[(-.23,.98),(0,1.07),(.23,.98),(.18,.77),(0,.69),(-.18,.77)],-.226,-.17,'Bronze')
    prism('Enamel chest plate',[(-.185,.963),(0,1.025),(.185,.963),(.145,.80),(0,.737),(-.145,.80)],-.249,-.22,'TeamCloth',bevel=.012)
    diamond('Chest sigil',0,-.28,.88,.13,.16,'SteelLight')
    sphere('Oversized forged helmet',(0,0,1.31),(.33,.29,.34),'Steel','Head',20,12)
    prism('Deep visor opening',[(-.245,1.4),(.245,1.4),(.20,1.14),(0,1.08),(-.20,1.14)],-.282,-.245,'Shadow','Head')
    eyes(1.315,-.306)
    stripe('Helmet brow',[(-.29,-.22,1.43),(0,-.315,1.47),(.29,-.22,1.43)],.037,'Bronze','Head')
    for s in (-1,1):
        prism('Helmet cheek plate',[(s*.23,1.43),(s*.32,1.38),(s*.26,1.10),(s*.15,1.13)],-.29,-.16,'SteelLight','Head')
        rivet((s*.268,-.312,1.34),'Head')
    box('Helmet ridge',(0,.01,1.565),(.09,.41,.11),'Bronze','Head',.02)
    # Bent, broad crest has a recognizable silhouette at board scale.
    for j in range(5):
        z=1.69+.085*math.sin(j*math.pi/6)
        box('Crest feather '+str(j),(0,.14-j*.075,z),(.115,.105,.22),'TeamCloth','Head',.025,rotation=(.12,0,0))
    for side,s in [('L',-1),('R',1)]:
        sphere('Shoulder cloth '+side,(s*.35,0,.98),(.16,.18,.19),'TeamCloth','UpperArm.'+side)
        sphere('Shoulder armor '+side,(s*.36,-.012,1.055),(.205,.22,.12),'Bronze','UpperArm.'+side)
        sphere('Shoulder inset '+side,(s*.36,-.023,1.083),(.16,.18,.09),'Steel','UpperArm.'+side)
        rod('Arm sleeve '+side,(s*.38,0,.95),(s*.43,-.10,.75),.105,'TeamCloth','UpperArm.'+side)
        sphere('Gauntlet '+side,(s*.43,-.13,.70),(.13,.115,.135),'Leather','Hand.'+side)
    # Large knight sword with a readable bevel and brass guard.
    rod('Sword grip',(.49,-.13,.45),(.49,-.13,.78),.05,'Leather','Weapon.R')
    box('Sword guard',(.49,-.13,.80),(.38,.13,.075),'Bronze','Weapon.R',.014)
    prism('Sword blade',[(.405,.84),(.41,1.49),(.49,1.69),(.57,1.49),(.575,.84)],-.19,-.10,'Steel','Weapon.R')
    stripe('Sword ridge',[(.49,-.215,.85),(.49,-.215,1.54)],.018,'SteelLight','Weapon.R')
    # Left kite shield, double border and central embossed diamond.
    outline=[(-.80,.99),(-.47,1.08),(-.28,.96),(-.33,.52),(-.55,.34),(-.77,.52)]
    prism('Kite shield rim',outline,-.35,-.24,'Bronze','Weapon.L',.025)
    inner=[(-.754,.955),(-.47,1.025),(-.335,.93),(-.38,.55),(-.55,.41),(-.723,.55)]
    prism('Kite shield enamel',inner,-.376,-.35,'TeamCloth','Weapon.L',.012)
    stripe('Shield spine',[(-.55,-.405,.46),(-.53,-.405,.89),(-.47,-.405,1.02)],.022,'SteelLight','Weapon.L')
    diamond('Shield crest',-.53,-.429,.77,.20,.24,'Bronze','Weapon.L')
    diamond('Shield enamel gem',-.53,-.456,.77,.085,.115,'TeamInset','Weapon.L')
    for x,z in [(-.72,.91),(-.37,.90),(-.69,.57),(-.41,.57)]:rivet((x,-.397,z),'Weapon.L')


def ranged():
    # Cloth silhouette, forward boots and a separate rear mantle.
    for side,s in [('L',-1),('R',1)]:box('Caster boot '+side,(s*.14,-.06,.105),(.22,.29,.20),'Leather','Foot.'+side,.025)
    lathe('Long fluted robe',[(.17,.36),(.24,.37),(.55,.29),(.88,.23),(1.08,.27)],'TeamCloth',segments=20,folds=.10)
    lathe('Weighted robe hem',[(.18,.362),(.23,.373),(.26,.36)],'Bronze',segments=20,folds=.09)
    prism('Layered central tabard',[(-.105,.98),(.105,.98),(.15,.26),(0,.14),(-.15,.26)],-.289,-.26,'TeamInset',bevel=.008)
    stripe('Tabard trim left',[(-.10,-.302,.98),(-.14,-.302,.29),(0,-.302,.17)],.013,'Bronze')
    stripe('Tabard trim right',[(.10,-.302,.98),(.14,-.302,.29),(0,-.302,.17)],.013,'Bronze')
    lathe('Caster leather belt',[(.67,.27),(.74,.26)],'Leather')
    diamond('Caster clasp',0,-.232,1.055,.18,.19,'Bronze')
    sphere('Hood volume',(0,.018,1.38),(.32,.30,.355),'TeamCloth','Head',20,12)
    sphere('Hood dark recess',(0,-.263,1.35),(.225,.055,.237),'Shadow','Head',20,12)
    # Gold edged opening set above a deep dark face.
    outline=[]
    for i in range(19):
        a=math.pi*2*i/18
        outline.append((.239*math.sin(a),-.302,1.36+.266*math.cos(a)))
    stripe('Hood opening piping',outline,.024,'Bronze','Head')
    eyes(1.375,-.326,.088)
    cone('Hood crown',(0,.04,1.68),.205,.07,.23,'TeamCloth','Head')
    sphere('Bent hood tip',(0,.15,1.77),(.095,.16,.08),'TeamCloth','Head',12,6)
    for side,s in [('L',-1),('R',1)]:
        sphere('Mantle shoulder '+side,(s*.275,.04,1.02),(.22,.23,.17),'TeamCloth','UpperArm.'+side)
        rod('Wide sleeve '+side,(s*.30,0,.99),(s*.40,-.12,.78),.13,'TeamCloth','UpperArm.'+side,12)
        sphere('Brass cuff '+side,(s*.40,-.12,.79),(.13,.10,.10),'Bronze','LowerArm.'+side)
        sphere('Gloved caster hand '+side,(s*.425,-.17,.75),(.09,.09,.10),'Leather','Hand.'+side)
    # Bow in left hand and a rear quiver; no staff or spell scroll.
    bow=[(-.24,-.26,.29),(-.42,-.30,.47),(-.51,-.31,.76),(-.40,-.30,1.12),(-.22,-.26,1.36)]
    stripe('Recurve bow limbs',bow,.043,'Leather','Weapon.L')
    stripe('Bow limb inlay',[(x,y-.036,z) for x,y,z in bow],.014,'Bronze','Weapon.L')
    rod('Bow string',bow[0],bow[-1],.008,'Parchment','Weapon.L',6)
    rod('Bow grip',(-.45,-.31,.69),(-.47,-.31,.86),.054,'Bronze','Weapon.L')
    rod('Back quiver',(.16,.22,.63),(.27,.28,1.31),.12,'Leather','Spine',12)
    rod('Quiver mouth',(.262,.28,1.25),(.28,.28,1.35),.14,'Bronze','Spine',12)
    for j in range(5):
        x=.20+(j%3)*.055;y=.28+(j//3)*.06
        rod('Quiver arrow',(x,y,1.02),(x+.08,y,1.60),.009,'Parchment','Spine',6)
        prism('Arrow fletching',[(x+.04,1.43),(x+.08,1.60),(x+.115,1.47)],y-.01,y+.01,'TeamCloth')



def heavy():
    # Non-human round chassis, articulated radial legs retract into its skirt.
    cone('Round armored chassis',(0,0,.24),.57,.48,.30,'Steel','Hips',32)
    cone('Chassis brass skirt',(0,0,.135),.59,.59,.06,'Bronze','Hips',32)
    for i in range(8):
        a=math.tau*i/8;bone='SpiderLeg.'+str(i)
        points=[(.35*math.cos(a),.35*math.sin(a),.30),(.65*math.cos(a),.65*math.sin(a),.38),(.82*math.cos(a),.82*math.sin(a),.035)]
        rod('Eight-leg upper '+str(i),points[0],points[1],.055,'SteelLight',bone)
        rod('Eight-leg lower '+str(i),points[1],points[2],.045,'Steel',bone)
        sphere('Leg joint '+str(i),points[1],(.075,.075,.075),'Bronze',bone)
    lathe('Heavy armored skirt',[(.45,.36),(.57,.38),(.75,.32)],'TeamCloth',segments=12,folds=.035)
    sphere('Barrel torso',(0,.035,1.0),(.40,.29,.36),'Steel')
    box('Reinforced belt',(0,0,.69),(.69,.51,.12),'Leather',bevel=.045)
    prism('Heavy breastplate border',[(-.34,1.27),(0,1.38),(.34,1.27),(.31,.89),(0,.75),(-.31,.89)],-.279,-.20,'Bronze',bevel=.022)
    prism('Heavy breastplate face',[(-.285,1.24),(0,1.325),(.285,1.24),(.26,.93),(0,.81),(-.26,.93)],-.308,-.27,'SteelLight',bevel=.025)
    diamond('Heavy chest stone',0,-.35,1.08,.25,.30,'TeamInset')
    diamond('Heavy chest heart',0,-.383,1.08,.105,.16,'Glow')
    # Articulated lower plates define a different silhouette from the small melee unit.
    for x in (-.24,0,.24):
        prism('Hanging fauld',[(x-.11,.71),(x+.11,.71),(x+.095,.44),(x,.37),(x-.095,.44)],-.285,-.22,'Steel',bevel=.018)
        rivet((x,-.316,.66))
    sphere('Heavy angular helm',(0,0,1.53),(.28,.25,.31),'Steel','Head',12,8)
    prism('Heavy visor cavity',[(-.21,1.63),(.21,1.63),(.185,1.39),(-.185,1.39)],-.25,-.21,'Shadow','Head')
    eyes(1.54,-.281,.09)
    stripe('Heavy brow',[(-.24,-.24,1.66),(0,-.28,1.70),(.24,-.24,1.66)],.036,'Bronze','Head')
    prism('Heavy jaw plate',[(-.19,1.46),(.19,1.46),(.15,1.27),(-.15,1.27)],-.30,-.235,'SteelLight','Head')
    for x in (-.075,0,.075):box('Jaw vent',(x,-.34,1.37),(.025,.012,.095),'Shadow','Head',.003)
    # Crest is restrained, so the gauntlets and shoulders dominate.
    prism('Heavy crest',[(-.07,1.77),(0,1.99),(.07,1.77)],-.09,.20,'Bronze','Head')
    for side,s in [('L',-1),('R',1)]:
        sphere('Heavy upper arm '+side,(s*.46,0,1.04),(.19,.20,.27),'Shadow','UpperArm.'+side,12,8)
        # Layered pauldrons rather than unconnected spheres.
        box('Giant pauldron border '+side,(s*.47,.012,1.32),(.47,.57,.25),'Bronze','UpperArm.'+side,.065,rotation=(0,s*.15,0))
        box('Giant pauldron plate '+side,(s*.48,.005,1.36),(.40,.50,.20),'Steel','UpperArm.'+side,.05,rotation=(0,s*.15,0))
        box('Pauldron team stripe '+side,(s*.48,-.04,1.459),(.23,.38,.028),'TeamCloth','UpperArm.'+side,.011)
        for y in (-.17,.17):
            rivet((s*.67,y,1.385),'UpperArm.'+side)
        sphere('Elbow hinge '+side,(s*.54,.035,.96),(.145,.16,.15),'Bronze','LowerArm.'+side,12,8)
        box('Power gauntlet border '+side,(s*.59,-.08,.81),(.39,.50,.43),'Bronze','Hand.'+side,.055)
        box('Power gauntlet shell '+side,(s*.59,-.085,.83),(.35,.46,.38),'Steel','Hand.'+side,.045)
        diamond('Gauntlet inset '+side,s*.59,-.40,.70,.16,.105,'TeamInset','Hand.'+side)
        # Mechanical piston detail ties the super-minion mass into the shared fantasy armor.
        rod('Forearm piston '+side,(s*.70,.11,.91),(s*.67,.12,1.15),.033,'SteelLight','LowerArm.'+side)
    # Round shield and broad gem sword replace the empty powered fists.
    rod('Heavy round shield',(-.64,-.43,1.01),(-.64,-.54,1.01),.37,'Bronze','Weapon.L',32)
    rod('Heavy shield face',(-.64,-.54,1.01),(-.64,-.565,1.01),.32,'Steel','Weapon.L',32)
    sphere('Heavy shield boss',(-.64,-.61,1.01),(.11,.065,.11),'TeamInset','Weapon.L')
    rod('Gem sword grip',(.64,-.25,.56),(.64,-.25,.86),.055,'Leather','Weapon.R')
    box('Gem sword guard',(.64,-.25,.89),(.39,.14,.085),'Bronze','Weapon.R')
    prism('Heavy gem blade',[(.52,.94),(.50,1.48),(.64,1.79),(.78,1.48),(.76,.94)],-.30,-.18,'TeamInset','Weapon.R')
    stripe('Gem sword spine',[(.64,-.33,.96),(.64,-.33,1.59)],.03,'Glow','Weapon.R')
    # Small rear standard makes red/blue allegiance readable from behind.
    rod('Banner mast',(0,.26,.84),(0,.26,1.76),.031,'Bronze')
    prism('Rear team pennant',[(-.16,1.76),(.16,1.76),(.16,1.26),(0,1.17),(-.16,1.26)],.27,.29,'TeamCloth',bevel=.008)


def rig_and_export(kind):
    # Same semantic skeleton for all three; weights are rigid per armor piece.
    data=bpy.data.armatures.new(kind+' Skeleton')
    rig=bpy.data.objects.new(kind+' Rig',data);collection.objects.link(rig)
    bpy.ops.object.select_all(action='DESELECT');rig.select_set(True);bpy.context.view_layer.objects.active=rig
    bpy.ops.object.mode_set(mode='EDIT')
    specs=[('Root',(0,0,0),(0,0,.2),None),('Hips',(0,0,.4),(0,0,.68),'Root'),('Spine',(0,0,.68),(0,0,1.07),'Hips'),('Head',(0,0,1.07),(0,0,1.65),'Spine')]
    for side,s in [('L',-1),('R',1)]:
        specs += [('UpperArm.'+side,(s*.24,0,1.05),(s*.4,0,.85),'Spine'),('LowerArm.'+side,(s*.4,0,.85),(s*.44,-.12,.7),'UpperArm.'+side),('Hand.'+side,(s*.44,-.12,.7),(s*.44,-.12,.6),'LowerArm.'+side),('Weapon.'+side,(s*.44,-.12,.7),(s*.44,-.12,.95),'Hand.'+side),('UpperLeg.'+side,(s*.16,0,.59),(s*.16,0,.37),'Hips'),('LowerLeg.'+side,(s*.16,0,.37),(s*.16,0,.15),'UpperLeg.'+side),('Foot.'+side,(s*.16,0,.15),(s*.16,-.20,.1),'LowerLeg.'+side)]
    if kind=='Heavy':
        for i in range(8):
            a=math.tau*i/8;specs.append(('SpiderLeg.'+str(i),(.35*math.cos(a),.35*math.sin(a),.30),(.65*math.cos(a),.65*math.sin(a),.38),'Root'))
    for name,head,tail,parent in specs:
        b=data.edit_bones.new(name);b.head=head;b.tail=tail
        if parent:b.parent=data.edit_bones[parent]
    bpy.ops.object.mode_set(mode='OBJECT')
    # Enlarge weapons/shields another 12% around their hand anchor.
    for obj in parts:
        names=[g.name for g in obj.vertex_groups]
        side='L' if 'Weapon.L' in names else 'R' if 'Weapon.R' in names else None
        if side:
            pivot=Vector((-.44 if side=='L' else .44,-.12,.70));inverse=obj.matrix_world.inverted()
            for v in obj.data.vertices:v.co=inverse @ (pivot+(obj.matrix_world @ v.co-pivot)*1.12)
    # Keep a clean skinned runtime mesh while source objects remain available in a collection.
    copies=[]
    for obj in parts:
        copy=obj.copy();copy.data=obj.data.copy();collection.objects.link(copy);copies.append(copy)
    bpy.ops.object.select_all(action='DESELECT')
    for obj in copies:obj.select_set(True)
    bpy.context.view_layer.objects.active=copies[0];bpy.ops.object.join()
    mesh=copies[0];mesh.name=kind+' Runtime Mesh'
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    colors=mesh.data.color_attributes.new(name='Color',type='BYTE_COLOR',domain='CORNER')
    for loop in mesh.data.loops:
        v=mesh.data.vertices[loop.vertex_index].co
        noise=math.sin(v.x*41.17+v.y*79.33+v.z*28.19)*.027
        value=max(.7,min(1,.84+.09*v.z+noise))
        colors.data[loop.index].color=(value,value,value,1)
    arm=mesh.modifiers.new('Shared semantic skeleton','ARMATURE');arm.object=rig
    mesh.parent=rig
    bpy.ops.object.select_all(action='DESELECT');mesh.select_set(True);rig.select_set(True);bpy.context.view_layer.objects.active=rig
    bpy.ops.export_scene.fbx(filepath=str(EXPORT/(kind+'.fbx')),use_selection=True,object_types={'ARMATURE','MESH'},use_mesh_modifiers=True,add_leaf_bones=False,bake_anim=False,axis_forward='-Z',axis_up='Y',apply_unit_scale=True,mesh_smooth_type='FACE',path_mode='AUTO')
    tris=sum(len(p.vertices)-2 for p in mesh.data.polygons)
    info={'kind':kind,'triangles':tris,'vertices':len(mesh.data.vertices),'bones':len(data.bones),'materials':[m.name for m in mesh.data.materials],'height':round(mesh.dimensions.z,3),'width':round(mesh.dimensions.x,3)}
    # Only runtime mesh is rendered. Named source pieces stay editable but hidden.
    for obj in parts:obj.hide_render=True;obj.hide_set(True)
    rig['asset_notes']='Original Goa2 mesh; LoL-style proportions/material hierarchy; no ripped game assets. Rigid piece weights, not final deformation rig.'
    return rig,mesh,info


models=[];manifest=[]
for name,build in [('Melee',melee),('Ranged',ranged),('Heavy',heavy)]:
    collection=bpy.data.collections.new(name+' editable assembly');bpy.context.scene.collection.children.link(collection)
    parts=[];build();rig,mesh,info=rig_and_export(name);models.append((rig,mesh));manifest.append(info)
(SOURCE/'manifest.json').write_text(json.dumps({'blender':bpy.app.version_string,'models':manifest},indent=2)+'\n',encoding='utf-8')

# A consistent studio presentation is saved with the editable source.
for i,(rig,mesh) in enumerate(models):
    rig.location.x=(i-1)*2.05
    if i == 2: rig.scale = (1.3, 1.3, 1.3)
scene=bpy.context.scene
scene.render.engine='CYCLES';scene.cycles.samples=24
scene.cycles.use_denoising=True
scene.world.color=(.14,.14,.14)
scene.world.use_nodes=True
next(n for n in scene.world.node_tree.nodes if n.type == 'BACKGROUND').inputs[0].default_value=(.075,.11,.16,1)
next(n for n in scene.world.node_tree.nodes if n.type == 'BACKGROUND').inputs[1].default_value=.5

def light(name,location,power,color,size):
    data=bpy.data.lights.new(name,'AREA');data.energy=power;data.color=color;data.shape='DISK';data.size=size
    obj=bpy.data.objects.new(name,data);scene.collection.objects.link(obj);obj.location=location;obj.rotation_euler=(Vector((0,0,.8))-obj.location).to_track_quat('-Z','Y').to_euler()
light('Warm key',(-3,-4,6),650,(1,.83,.65),5)
light('Cool fill',(4,-2,4),450,(.58,.77,1),4)
light('Rim',(1,3,5),850,(.52,.76,1),4)

bpy.ops.mesh.primitive_plane_add(size=200,location=(0,0,-.018));ground=bpy.context.object;ground.name='Studio floor (not exported)'
floor=bpy.data.materials.new('Studio slate');floor.diffuse_color=(.035,.052,.074,1);floor.use_nodes=True;next(n for n in floor.node_tree.nodes if n.type == 'BSDF_PRINCIPLED').inputs['Base Color'].default_value=floor.diffuse_color;next(n for n in floor.node_tree.nodes if n.type == 'BSDF_PRINCIPLED').inputs['Roughness'].default_value=.9;ground.data.materials.append(floor)
camera_data=bpy.data.cameras.new('Presentation camera');camera=bpy.data.objects.new('Presentation camera',camera_data);scene.collection.objects.link(camera)
camera.location=(3.2,-10,5.1);target=Vector((0,0,1.16));camera.rotation_euler=(target-camera.location).to_track_quat('-Z','Y').to_euler();camera_data.type='ORTHO';camera_data.ortho_scale=7.6;scene.camera=camera
scene.render.resolution_x=1920;scene.render.resolution_y=1080;scene.render.resolution_percentage=100
scene.view_settings.view_transform='AgX'
bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE/'Goa2_Minion_Collection.blend'))
scene.render.filepath=str(PREVIEW/'minions-blue.png');bpy.ops.render.render(write_still=True)
materials['TeamCloth'].diffuse_color=(*rgb('#984D50'),1);next(n for n in materials['TeamCloth'].node_tree.nodes if n.type == 'BSDF_PRINCIPLED').inputs['Base Color'].default_value=materials['TeamCloth'].diffuse_color
materials['TeamInset'].diffuse_color=(*rgb('#D77562'),1);next(n for n in materials['TeamInset'].node_tree.nodes if n.type == 'BSDF_PRINCIPLED').inputs['Base Color'].default_value=materials['TeamInset'].diffuse_color
scene.render.filepath=str(PREVIEW/'minions-red.png');bpy.ops.render.render(write_still=True)
print('GOA_MINIONS_DONE '+json.dumps(manifest))
