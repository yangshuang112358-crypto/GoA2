"""One art-directed R2B01 button. Editable meshes, actual incisions, Cycles.

This deliberately owns a new sample, never the user's Workbench or Unity assets.
"""
import argparse
import math
import random
import sys
import json
from pathlib import Path

import bpy
import bmesh
from mathutils import Vector

P = argparse.ArgumentParser()
P.add_argument('--root', type=Path, required=True)
P.add_argument('--out', type=Path, required=True)
P.add_argument('--replace-generated', action='store_true')
P.add_argument('--preview', action='store_true')
P.add_argument('--no-render', action='store_true')
A = P.parse_args(sys.argv[sys.argv.index('--') + 1:])
ROOT = A.root.resolve()
OUT = A.out.resolve()
OUT.mkdir(parents=True, exist_ok=True)
SOURCE = ROOT / 'art/production/samples/R2B01-Hero/R2B01_Boomerang.blend'
if SOURCE.exists() and not A.replace_generated:
    raise RuntimeError('Refusing to replace generated source without explicit flag.')
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.preferences.filepaths.save_version = 0
random.seed(1401)
SC = bpy.context.scene
SC.name = 'R2B01 / 回旋镖 / finished sample'
MODEL = bpy.data.collections.new('01 / Editable button components')
SC.collection.children.link(MODEL)
STUDIO = bpy.data.collections.new('90 / Lights and cameras')
SC.collection.children.link(STUDIO)
C = MODEL
CUTS = []
FONT = bpy.data.fonts.load('C:/Windows/Fonts/georgiab.ttf')
CN = bpy.data.fonts.load('C:/Windows/Fonts/simsun.ttc')


def adopt(ob, mat=None):
    for coll in list(ob.users_collection):
        coll.objects.unlink(ob)
    C.objects.link(ob)
    if mat:
        ob.data.materials.append(mat)
    return ob


def mesh(name, verts, faces, mat):
    me = bpy.data.meshes.new(name)
    me.from_pydata(verts, [], faces)
    me.update()
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new(name, me)
    C.objects.link(ob)
    me.materials.append(mat)
    return ob


def bevel(ob, width, segments=2, apply=False, material=None):
    mod = ob.modifiers.new('Stone chamfer / real edge', 'BEVEL')
    mod.width = width
    mod.segments = segments
    if material:
        ob.data.materials.append(material)
        mod.affect = 'EDGES'
        mod.material = len(ob.data.materials) - 1
    if apply:
        bpy.context.view_layer.objects.active = ob
        bpy.ops.object.modifier_apply(modifier=mod.name)
    return ob


def poly(name, pts, bottom, top, mat, chamfer=0):
    if sum(pts[i][0] * pts[(i+1) % len(pts)][1] - pts[(i+1) % len(pts)][0] * pts[i][1] for i in range(len(pts))) < 0:
        pts = list(reversed(pts))
    n = len(pts)
    ob = mesh(name, [(x, y, z) for z in (bottom, top) for x, y in pts],
              [tuple(reversed(range(n))), tuple(range(n, 2*n))] +
              [(i, (i+1) % n, (i+1) % n+n, i+n) for i in range(n)], mat)
    if chamfer:
        bevel(ob, chamfer, 2, True)
    return ob


def line(name, pts, radius, mat, closed=False):
    cu = bpy.data.curves.new(name, 'CURVE')
    cu.dimensions = '3D'
    cu.bevel_depth = radius
    cu.bevel_resolution = 2
    cu.use_fill_caps = True
    s = cu.splines.new('POLY')
    s.points.add(len(pts)-1)
    for point, xyz in zip(s.points, pts):
        point.co = (*xyz, 1)
    s.use_cyclic_u = closed
    ob = bpy.data.objects.new(name, cu)
    C.objects.link(ob)
    cu.materials.append(mat)
    return ob


def noise_material(name, dark, light, rough=.72, metal=0, cracks=True):
    ma = bpy.data.materials.new(name)
    ma.use_nodes = True
    ma.diffuse_color = (*light, 1)
    n, l = ma.node_tree.nodes, ma.node_tree.links
    bs = n.get('Principled BSDF')
    bs.inputs['Metallic'].default_value = metal
    tex = n.new('ShaderNodeTexCoord')
    # Object coordinates keep weathering at one physical scale on every part.
    cloud = n.new('ShaderNodeTexNoise')
    cloud.inputs['Scale'].default_value = 3.8
    cloud.inputs['Detail'].default_value = 5
    l.new(tex.outputs['Object'], cloud.inputs['Vector'])
    ramp = n.new('ShaderNodeValToRGB')
    ramp.color_ramp.elements[0].position = .16
    ramp.color_ramp.elements[0].color = (*dark, 1)
    ramp.color_ramp.elements[1].position = .86
    ramp.color_ramp.elements[1].color = (*light, 1)
    l.new(cloud.outputs['Fac'], ramp.inputs[0])
    color = ramp.outputs['Color']
    if metal < .3:
        aggregate=n.new('ShaderNodeTexNoise')
        aggregate.inputs['Scale'].default_value=42
        aggregate.inputs['Detail'].default_value=5
        aggregate.inputs['Roughness'].default_value=.78
        l.new(tex.outputs['Object'],aggregate.inputs['Vector'])
        grains=n.new('ShaderNodeValToRGB')
        grains.color_ramp.elements[0].position=.30
        grains.color_ramp.elements[0].color=(.34,.36,.38,1)
        grains.color_ramp.elements[1].position=.68
        grains.color_ramp.elements[1].color=(1.25,1.23,1.18,1)
        l.new(aggregate.outputs['Fac'],grains.inputs[0])
        grainmix=n.new('ShaderNodeMixRGB');grainmix.blend_type='MULTIPLY'
        grainmix.inputs[0].default_value=.80
        l.new(color,grainmix.inputs[1]);l.new(grains.outputs[0],grainmix.inputs[2])
        color=grainmix.outputs[0]
    fine = n.new('ShaderNodeTexNoise')
    fine.inputs['Scale'].default_value = 135
    fine.inputs['Detail'].default_value = 3
    l.new(tex.outputs['Object'], fine.inputs['Vector'])
    micro = n.new('ShaderNodeBump')
    micro.inputs['Strength'].default_value = .32
    micro.inputs['Distance'].default_value = .013
    l.new(fine.outputs['Fac'], micro.inputs['Height'])
    normal = micro.outputs['Normal']
    if metal < .3:
        relief=n.new('ShaderNodeBump')
        relief.inputs['Strength'].default_value=.44
        relief.inputs['Distance'].default_value=.024
        l.new(aggregate.outputs['Fac'],relief.inputs['Height'])
        l.new(normal,relief.inputs['Normal'])
        normal=relief.outputs[0]
    if cracks:
        warp = n.new('ShaderNodeTexNoise')
        warp.inputs['Scale'].default_value = 9
        l.new(tex.outputs['Object'], warp.inputs['Vector'])
        scale = n.new('ShaderNodeVectorMath')
        scale.operation = 'SCALE'
        scale.inputs[3].default_value = .075
        l.new(warp.outputs['Color'], scale.inputs[0])
        add = n.new('ShaderNodeVectorMath')
        add.operation = 'ADD'
        l.new(scale.outputs['Vector'], add.inputs[0])
        l.new(tex.outputs['Object'], add.inputs[1])
        vor = n.new('ShaderNodeTexVoronoi')
        vor.feature = 'DISTANCE_TO_EDGE'
        vor.inputs['Scale'].default_value = 4.6
        l.new(add.outputs['Vector'], vor.inputs['Vector'])
        cr = n.new('ShaderNodeValToRGB')
        cr.color_ramp.elements[0].position = .004
        cr.color_ramp.elements[0].color = (.32, .32, .32, 1)
        cr.color_ramp.elements[1].position = .011
        cr.color_ramp.elements[1].color = (1, 1, 1, 1)
        l.new(vor.outputs['Distance'], cr.inputs[0])
        # Break up the cellular fracture network so the material does not read as tiled leather.
        gate = n.new('ShaderNodeMapRange')
        gate.inputs['From Min'].default_value = .38
        gate.inputs['From Max'].default_value = .65
        gate.inputs['To Min'].default_value = .0
        gate.inputs['To Max'].default_value = .55
        l.new(cloud.outputs['Fac'], gate.inputs[0])
        mix = n.new('ShaderNodeMixRGB')
        mix.blend_type = 'MULTIPLY'
        l.new(gate.outputs[0], mix.inputs[0])
        l.new(color, mix.inputs[1])
        l.new(cr.outputs[0], mix.inputs[2])
        color = mix.outputs[0]
        bump = n.new('ShaderNodeBump')
        bump.inputs['Strength'].default_value = .35
        bump.inputs['Distance'].default_value = .012
        l.new(cr.outputs[0], bump.inputs['Height'])
        l.new(normal, bump.inputs['Normal'])
        normal = bump.outputs['Normal']
    l.new(color, bs.inputs['Base Color'])
    raybevel=n.new('ShaderNodeBevel')
    raybevel.inputs['Radius'].default_value=.007
    raybevel.samples=3
    l.new(normal,raybevel.inputs['Normal'])
    l.new(raybevel.outputs['Normal'], bs.inputs['Normal'])
    rr = n.new('ShaderNodeMapRange')
    rr.inputs['To Min'].default_value = rough-.10
    rr.inputs['To Max'].default_value = rough+.08
    l.new(fine.outputs['Fac'], rr.inputs[0])
    l.new(rr.outputs[0], bs.inputs['Roughness'])
    return ma


def emission(name, color, power):
    ma = bpy.data.materials.new(name)
    ma.use_nodes = True
    bs = ma.node_tree.nodes.get('Principled BSDF')
    bs.inputs['Base Color'].default_value = (*[c*.2 for c in color], 1)
    bs.inputs['Roughness'].default_value = .29
    bs.inputs['Metallic'].default_value = .25
    bs.inputs['Emission Color'].default_value = (*color, 1)
    bs.inputs['Emission Strength'].default_value = power
    return ma


M = {}
M['stone'] = noise_material('01 Graphite / fractured dressed slate', (.014,.018,.021), (.072,.080,.086))
M['edge'] = noise_material('02 Pale stone / exposed cut edges', (.052,.060,.066), (.19,.205,.218), .65)
M['red'] = noise_material('03 Iron-red / mineral card inlay', (.055,.010,.008), (.22,.046,.031))
M['red_edge'] = noise_material('04 Red dressed stone / worn edge', (.10,.024,.017), (.26,.082,.049), .68)
M['dark'] = noise_material('05 Blackened seating / no gold outline', (.008,.011,.014), (.025,.032,.038), .5,.58,False)
M['well'] = noise_material('06 Carved icon recess / volcanic ground', (.005,.004,.004), (.026,.014,.010), .83,0,False)
M['cavity'] = noise_material('06b Deep incised groove / black patina', (.001,.0015,.002), (.007,.010,.012), .80,0,False)
M['steel'] = noise_material('07 Forged silver / blade face', (.19,.21,.23), (.45,.48,.51), .37,.75,False)
M['bronze'] = noise_material('08 Antique warm keyed metal', (.08,.043,.019), (.30,.20,.092), .40,.8,False)
M['rock'] = noise_material('09 Floating ember stone', (.07,.024,.012), (.22,.11,.055))
M['orange'] = emission('10 Ember arc / amber body', (1,.105,.007), 5.0)
M['orange_hot'] = emission('11 Ember arc / gold-white core', (1,.46,.11), 8)
M['green_magic'] = emission('12 Green magic / recessed liquid light', (.025,.62,.095), 1.8)
M['red_magic'] = emission('13 Red magic / recessed liquid light', (.68,.014,.006), 1.5)
M['ground'] = noise_material('90 Studio charcoal', (.004,.006,.008), (.014,.018,.021), .92,0,False)
for key,hue in [('green_magic',(.045,.62,.13)),('red_magic',(.72,.028,.012))]:
    ma=M[key];n=ma.node_tree.nodes;l=ma.node_tree.links;bs=n.get('Principled BSDF')
    tex=n.new('ShaderNodeTexCoord')
    cloud=n.new('ShaderNodeTexNoise');cloud.noise_dimensions='4D'
    cloud.inputs['Scale'].default_value=13;cloud.inputs['Detail'].default_value=2.5
    cloud.inputs['Roughness'].default_value=.62
    cloud.inputs['W'].default_value=.15;cloud.inputs['W'].keyframe_insert('default_value',frame=1)
    cloud.inputs['W'].default_value=1.5;cloud.inputs['W'].keyframe_insert('default_value',frame=193)
    l.new(tex.outputs['Object'],cloud.inputs['Vector'])
    pools=n.new('ShaderNodeValToRGB')
    pools.color_ramp.elements[0].position=.27
    pools.color_ramp.elements[0].color=(*[v*.008 for v in hue],1)
    pools.color_ramp.elements[1].position=.76
    pools.color_ramp.elements[1].color=(*hue,1)
    pools.color_ramp.elements.new(.51).color=(*[v*.10 for v in hue],1)
    l.new(cloud.outputs['Fac'],pools.inputs[0])
    l.new(pools.outputs[0],bs.inputs['Base Color'])
    l.new(pools.outputs[0],bs.inputs['Emission Color'])
    bs.inputs['Emission Strength'].default_value=2.3
    bs.inputs['Roughness'].default_value=.22
    bs.inputs['Coat Weight'].default_value=.32
    bump=n.new('ShaderNodeBump');bump.inputs['Strength'].default_value=.13;bump.inputs['Distance'].default_value=.008
    l.new(cloud.outputs[0],bump.inputs['Height']);l.new(bump.outputs[0],bs.inputs['Normal'])


def arc_block(name, ro, ri, a0, a1, z0, z1, mat, steps=8):
    pts = [(ro*math.cos(a0+(a1-a0)*i/steps), ro*math.sin(a0+(a1-a0)*i/steps)) for i in range(steps+1)]
    pts += [(ri*math.cos(a0+(a1-a0)*i/steps), ri*math.sin(a0+(a1-a0)*i/steps)) for i in range(steps,-1,-1)]
    return poly(name, pts,z0,z1,mat,.016)


def circle(name,r,z0,z1,mat):
    return poly(name,[(r*math.cos(i*math.tau/160),r*math.sin(i*math.tau/160)) for i in range(160)],z0,z1,mat,.025)


def difference(body,cut,name):
    if cut.type != 'MESH':
        bpy.ops.object.select_all(action='DESELECT')
        cut.select_set(True)
        bpy.context.view_layer.objects.active=cut
        bpy.ops.object.convert(target='MESH')
    mod=body.modifiers.new('True incision / '+name,'BOOLEAN')
    mod.operation='DIFFERENCE'
    mod.solver='EXACT'
    mod.object=cut
    bpy.context.view_layer.objects.active=body
    before=len(body.data.polygons)
    bpy.ops.object.modifier_apply(modifier=mod.name)
    CUTS.append({'body':body.name,'feature':name,'before':before,'after':len(body.data.polygons)})
    bpy.data.objects.remove(cut,do_unlink=True)


def lettering(text, x,y,z,h,width,cn=False):
    cu=bpy.data.curves.new('Cutter '+text,'FONT')
    cu.body=text
    cu.font=CN if cn else FONT
    cu.align_x='CENTER'
    cu.align_y='CENTER'
    cu.size=1
    cu.extrude=.16
    cu.resolution_u=10
    # CJK offsets can self-intersect at dense strokes and corrupt exact booleans.
    # Preserve the original glyph contour, rather than artificially emboldening it.
    if cn:cu.offset=0
    ob=bpy.data.objects.new('Cutter '+text,cu)
    C.objects.link(ob)
    bpy.context.view_layer.update()
    s=min(h/ob.dimensions.y,width/ob.dimensions.x)
    ob.scale=(s,s,1)
    bpy.ops.object.select_all(action='DESELECT')
    ob.select_set(True)
    bpy.context.view_layer.objects.active=ob
    bpy.ops.object.convert(target='MESH')
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    xs=[v.co.x for v in ob.data.vertices];ys=[v.co.y for v in ob.data.vertices]
    cx=(min(xs)+max(xs))/2;cy=(min(ys)+max(ys))/2
    for v in ob.data.vertices:v.co.x-=cx;v.co.y-=cy
    ob.location=(x,y,z)
    return ob


def carve(body,text,x,y,z,h,width,cn=False):
    cut=lettering(text,x,y,z+.035,h,width,cn)
    # The cutter extends .125 below the face; keep the floor as real body geometry.
    if cn:
        for v in cut.data.vertices:v.co.z*=.60
        cut.location.z=z+.026
    bottom=min(v.co.z for v in cut.data.vertices)+cut.location.z
    floor=cut.copy();floor.data=cut.data.copy();C.objects.link(floor)
    floor.name='Actual recessed patina floor / '+text
    zvals=[v.co.z for v in floor.data.vertices];low=min(zvals);span=max(zvals)-low
    for v in floor.data.vertices:v.co.z=(v.co.z-low)/span*.003
    floor.location.z=bottom+.001
    floor.data.materials.clear();floor.data.materials.append(M['cavity'])
    floor['depth_below_surface']=z-(bottom+.004)
    difference(body,cut,'recessed '+text)
    body['incision_depth']=.125
    bevel(body,.011,2,False)


def dressed_badge(name,pts,x,y,mat,edge):
    def at(scale):return [(x+a*scale,y+b*scale) for a,b in pts]
    poly(name+' / fitted dark socket',at(1.03),.11,.35,M['dark'],.035)
    rim=poly(name+' / dressed rim',at(1),.26,.53,edge,.048)
    # Hollow pocket prevents a solid backing from filling the incised numerals.
    pocket=poly('CUT recessed stone seat',at(.86),.355,.72,M['dark'])
    difference(rim,pocket,'hollow seat beneath stone face')
    body=poly(name+' / recessed face',at(.90),.39,.563,mat,.023)
    # Tiny hand selected corner spalls affect only a few outline vertices.
    for idx in (1,len(pts)//2):
        a,b=pts[idx]
        d=math.hypot(a,b)
        xx,yy=x+a*.96,y+b*.96
        width=.035
        cu=poly('CUT edge nick',[(xx-width,yy-width),(xx+width,yy-width*.3),(xx+.010,yy+.044)],.475,.62,M['dark'])
        difference(rim,cu,'edge chip')
    return body


# Large, connected stone body and close-fitted band, with genuine segment seams.
circle('Button / solid slate back',3.04,-.28,.015,M['dark'])
circle('Button / graphite body',3.0,-.18,.06,M['stone'])
circle('Button / deep curved icon well',2.66,-.015,.10,M['well'])
for i in range(48):
    a=i*math.tau/48
    arc_block('Outer dressed stone voussoir %02d'%i,3.02,2.87,a+.0018,a+math.tau/48-.0018,.012,.22+random.uniform(-.010,.010),M['edge'],5)
for i in range(28):
    a=i*math.tau/28
    arc_block('Red mineral band segment %02d'%i,2.875,2.63,a+.0015,a+math.tau/28-.0015,.005,.146+random.uniform(-.009,.009),M['red'],7)
for i in range(44):
    a=i*math.tau/44
    arc_block('Dark inner stone lip %02d'%i,2.641,2.58,a+.001,a+math.tau/44-.001,.055,.18,M['stone'],4)
# Broad curved lower stone panel; it belongs to the disc, not a floating label.
pts=[(-2.36,-.64),(-1.4,-.59),(0,-.62),(1.4,-.59),(2.36,-.64)]
for i in range(41):
    a=math.radians(-15-150*i/40)
    pts.append((2.46*math.cos(a),2.46*math.sin(a)))
plate=poly('Name / full lower slate panel',pts,.105,.28,M['stone'],.042)
carve(plate,'回旋镖',0,-1.09,.28,.51,2.21,True)
# A narrow cut along the plaque shoulder follows the same stone surface.
line('Plaque / pale exposed upper fracture',[(-1.51,-.62,.283),(-.7,-.638,.283),(0,-.63,.283),(.65,-.62,.283),(1.5,-.632,.283)],.012,M['edge'])

BOOT=[(-.39,.76),(.21,.76),(.21,.19),(.30,-.02),(.56,-.17),(.60,-.41),(.44,-.56),(-.38,-.56),(-.64,-.38),(-.63,-.15),(-.43,.12)]
SHIELD=[(0,.82),(.32,.66),(.66,.55),(.63,-.29),(.43,-.56),(0,-.82),(-.43,-.56),(-.63,-.29),(-.66,.55),(-.32,.66)]
ATTACK=[(0,.95),(.23,.66),(.23,.30),(.57,.13),(.64,-.08),(.48,-.18),(.46,-.61),(0,-.96),(-.46,-.61),(-.48,-.18),(-.64,-.08),(-.57,.13),(-.23,.30),(-.23,.66)]
DISTANCE=[(0,.97),(.42,.61),(.63,.23),(.57,-.47),(0,-.94),(-.57,-.47),(-.63,.23),(-.42,.61)]
HOUR=[(-.76,.55),(.76,.55),(.86,.40),(.67,.22),(.53,-.01),(.61,-.25),(.77,-.43),(.76,-.57),(-.76,-.57),(-.77,-.43),(-.61,-.25),(-.53,-.01),(-.67,.22),(-.86,.40)]
boot=dressed_badge('Movement / boot',BOOT,-1.91,1.66,M['stone'],M['edge'])
shield=dressed_badge('Defence / shield',SHIELD,1.91,1.57,M['stone'],M['edge'])
atk=dressed_badge('Primary / attack',ATTACK,-2.05,-1.08,M['red'],M['red_edge'])
dist=dressed_badge('Attack distance / spear and target',DISTANCE,2.05,-1.06,M['red'],M['red_edge'])
speed=dressed_badge('Initiative / hourglass',HOUR,0,-2.34,M['stone'],M['edge'])
carve(boot,'4',-1.89,1.44,.563,.73,.70)
carve(shield,'3',1.91,1.34,.563,.77,.75)
carve(atk,'3',-2.05,-1.40,.563,.74,.64)
carve(dist,'3',2.05,-1.43,.563,.70,.64)
carve(speed,'9',0,-2.37,.563,.74,.90)
# Reusable alternate engraved attack faces are hidden until material demonstration.
for value,state in [('4','green_magic'),('2','red_magic')]:
    pts=[(-2.05+x*.90,-1.08+y*.90) for x,y in ATTACK]
    alt=poly('VARIANT '+state+' / engraved attack face',pts,.39,.563,M['red'],.029)
    cut=lettering(value,-2.05,-1.40,.598,.74,.64)
    fill=cut.copy();fill.data=cut.data.copy();C.objects.link(fill)
    fill.name='VARIANT '+state+' / below-surface luminous numeral'
    zs=[v.co.z for v in fill.data.vertices];z0=min(zs);span=max(zs)-z0
    for v in fill.data.vertices:v.co.z=(v.co.z-z0)/span*.008
    fill.location.z=.444
    fill.data.materials.clear();fill.data.materials.append(M[state])
    fill['below_front_surface']=.111
    difference(alt,cut,'variant recessed '+value)
    bevel(alt,.011,2,False)
    for ob in (alt,fill):
        ob.hide_render=True;ob.hide_set(True)
        ob['sample_only_modifier_preview']=True

# Recognisable semantic sculptures above each number, contained by stone.
shield_pts=[(1.91,2.19),(2.13,2.08),(2.09,1.84),(1.91,1.68),(1.73,1.84),(1.69,2.08)]
poly('Defence / small faceted heraldic shield',shield_pts,.569,.617,M['dark'],.016)
poly('Defence / left light shield facet',[(1.91,2.16),(1.91,1.73),(1.77,1.86),(1.73,2.05)],.614,.640,M['edge'],.007)
line('Defence / centre ridge',[(1.91,1.76,.64),(1.91,2.16,.64)],.014,M['steel'])
# Sculpted seams on boot cuff, not a separate icon floating over it.
for yy in (2.25,2.10):
    line('Movement / cuff engraved seam',[(-2.20,yy,.566),(-1.94,yy-.018,.566),(-1.79,yy-.014,.566)],.012,M['dark'])
line('Movement / worn cuff edge',[(-2.22,2.27,.571),(-1.79,2.27,.571)],.013,M['edge'])
# Compact sword, kept above the primary value.
poly('Primary / sword blade',[(-2.05,-.25),(-1.96,-.40),(-1.97,-.82),(-2.13,-.82),(-2.14,-.40)],.568,.629,M['bronze'],.015)
poly('Primary / pale sword ridge',[(-2.05,-.30),(-2.05,-.80),(-2.13,-.80),(-2.13,-.42)],.629,.644,M['edge'],.005)
line('Primary / cross guard',[(-2.30,-.84,.624),(-1.80,-.84,.624)],.031,M['bronze'])
line('Primary / hilt',[(-2.05,-.84,.623),(-2.05,-1.00,.623)],.035,M['bronze'])
# Target ring + broad spear, distinguishable from range's concentric ripples.
for rr,th in [(.31,.027),(.23,.011)]:
    line('Distance / engraved reticle',[(2.05+rr*math.cos(i*math.tau/96),-.70+rr*math.sin(i*math.tau/96),.581) for i in range(96)],th,M['dark'],True)
line('Distance / exposed reticle lip',[(2.05+.30*math.cos(i*math.tau/96),-.70+.30*math.sin(i*math.tau/96),.608) for i in range(96)],.016,M['red_edge'],True)
line('Distance / diagonal spear shaft',[(1.76,-.98,.623),(2.28,-.46,.623)],.026,M['bronze'])
poly('Distance / spear tip',[(2.18,-.44),(2.43,-.30),(2.30,-.58)],.592,.650,M['edge'],.011)
# Top ring keystone, kept subordinate to the image and values.
poly('Outer ring / diamond keystone',[(0,3.065),(.19,2.84),(0,2.61),(-.19,2.84)],.16,.32,M['edge'],.021)
poly('Outer ring / inset dark diamond',[(0,2.994),(.114,2.84),(0,2.69),(-.114,2.84)],.302,.321,M['dark'],.010)


def fracture(body,name,pts,z,width=.008):
    """An actual shallow, tapered chisel seam; never a line drawn on the render."""
    cu=line('CUT '+name,[(x,y,z-.004) for x,y in pts],width,M['dark'])
    for i,p in enumerate(cu.data.splines[0].points):
        p.radius=.30+.70*math.sin(math.pi*(i+.45)/(len(pts)+.1))
    difference(body,cu,name)


# Art-directed fissures do not cross numerals or destroy the regular silhouette.
for body,name,z,paths in [
    (boot,'Boot cut',.563,[
        [(-2.20,2.05),(-2.14,1.97),(-2.17,1.88)],
        [(-2.43,1.48),(-2.35,1.42),(-2.32,1.32)],
        [(-1.52,1.28),(-1.62,1.25),(-1.66,1.17)]]),
    (shield,'Shield cut',.563,[
        [(2.43,1.95),(2.34,1.93),(2.32,1.82),(2.27,1.77)],
        [(1.42,1.76),(1.49,1.72),(1.46,1.65)],
        [(1.93,.91),(1.89,1.0),(1.84,1.04)]]),
    (atk,'Red attack cut',.563,[
        [(-2.53,-1.11),(-2.43,-1.13),(-2.41,-1.19)],
        [(-1.70,-1.48),(-1.77,-1.56),(-1.78,-1.63)],
        [(-2.12,-1.83),(-2.09,-1.76),(-2.15,-1.71)]]),
    (dist,'Red distance cut',.563,[
        [(2.56,-1.0),(2.48,-.96),(2.44,-1.03)],
        [(1.65,-1.42),(1.68,-1.50),(1.75,-1.55)],
        [(2.14,-1.75),(2.07,-1.80),(2.06,-1.86)]]),
    (speed,'Hourglass cut',.563,[
        [(-.64,-1.94),(-.55,-1.99),(-.57,-2.06)],
        [(.55,-2.65),(.46,-2.60),(.44,-2.54)]]),
    (plate,'Name slate cut',.28,[
        [(-1.60,-.65),(-1.54,-.78),(-1.41,-.81),(-1.39,-.98)],
        [(1.56,-.63),(1.46,-.72),(1.47,-.82),(1.35,-.86)],
        [(-1.50,-1.57),(-1.31,-1.61),(-1.25,-1.76),(-1.01,-1.86),(-.97,-1.98)],
        [(1.41,-1.72),(1.28,-1.73),(1.20,-1.86),(1.03,-1.90)],
        [(-.96,-1.86),(-.82,-1.84),(-.77,-1.73)],
        [(.24,-1.54),(.37,-1.63),(.33,-1.78)]])]:
    for i,pts in enumerate(paths):fracture(body,name+str(i),pts,z,.010 if body==plate else .007)

# Broken radial veins and small bevel scars in the red inset band.
for i in (0,2,5,7,9,11,14,16,19,22,25):
    ob=bpy.data.objects.get('Red mineral band segment %02d'%i)
    a=(i+.48)*math.tau/28
    pts=[]
    for j,r in enumerate((2.67,2.72,2.76,2.83)):
        aa=a+(.010 if j%2 else -.009)
        pts.append((r*math.cos(aa),r*math.sin(aa)))
    fracture(ob,'Mineral vein '+str(i),pts,max(v.co.z for v in ob.data.vertices),.011)

sys.path.insert(0,str(ROOT/'tools/art'))
from r2b01_boomerang_hero import build_icon
ICON=build_icon(MODEL,{k:M[k] for k in ('steel','edge','bronze','dark','rock','orange','orange_hot')})

# One root facilitates real pivot animation; all glyphs stay on their stone bodies.
root=bpy.data.objects.new('BUTTON / rotate this complete assembly',None)
MODEL.objects.link(root)
for ob in list(MODEL.objects):
    if ob != root and ob.parent is None:ob.parent=root

C=STUDIO
poly('Studio / matte charcoal ground',[(-100,-100),(100,-100),(100,100),(-100,100)],-.58,-.54,M['ground'])
cam=bpy.data.objects.new('Camera / front hero portrait',bpy.data.cameras.new('Front portrait'))
STUDIO.objects.link(cam)
cam.data.type='ORTHO';cam.data.ortho_scale=6.95
cam.location=(0,-1.15,22)
cam.rotation_euler=(Vector((0,0,.10))-cam.location).to_track_quat('-Z','Y').to_euler()
SC.camera=cam
for name,loc,power,size,col in [
    ('Large warm raking key',(-4.2,5.0,5.5),680,2.8,(1,.88,.72)),
    ('Cool restrained fill',(4.4,1.0,7),190,5.0,(.59,.74,1)),
    ('Silvery upper edge',(0,4,3.8),210,2.6,(1,.96,.9)),
    ('Soft front bounce',(-1,-4,7),65,4.5,(.72,.79,1))]:
    ld=bpy.data.lights.new(name,'AREA');ld.energy=power;ld.shape='DISK';ld.size=size;ld.color=col
    ob=bpy.data.objects.new(name,ld);STUDIO.objects.link(ob);ob.location=loc
    ob.rotation_euler=(Vector((0,0,.1))-ob.location).to_track_quat('-Z','Y').to_euler()
SC.world=bpy.data.worlds.new('Quiet studio world');SC.world.use_nodes=True
SC.world.node_tree.nodes['Background'].inputs[0].default_value=(.055,.072,.09,1)
SC.world.node_tree.nodes['Background'].inputs[1].default_value=.18
SC.render.engine='CYCLES';SC.cycles.device='CPU';SC.cycles.samples=96
SC.cycles.use_denoising=True
SC.cycles.max_bounces=6
SC.render.resolution_x=1280;SC.render.resolution_y=1280;SC.render.resolution_percentage=100
SC.render.image_settings.file_format='PNG'
SC.view_settings.view_transform='AgX'
SC.view_settings.look='AgX - Medium High Contrast'
SC.use_nodes=True
n=SC.node_tree.nodes;l=SC.node_tree.links;n.clear()
rl=n.new('CompositorNodeRLayers')
gl=n.new('CompositorNodeGlare');gl.glare_type='FOG_GLOW';gl.quality='HIGH';gl.threshold=1.1;gl.size=8;gl.mix=-.50
co=n.new('CompositorNodeComposite');l.new(rl.outputs['Image'],gl.inputs[0]);l.new(gl.outputs[0],co.inputs[0])
SC.render.fps=24;SC.frame_start=1;SC.frame_end=192
for frame,ang in [(1,0),(49,-.19),(97,0),(145,.19),(193,0)]:
    root.rotation_euler=(.018*math.sin(frame/193*math.tau),ang,0)
    root.keyframe_insert('rotation_euler',frame=frame)
SC.frame_set(1)
root.rotation_euler=(0,0,0)
notes=bpy.data.texts.new('READ ME / single specimen scope')
notes.write('Single red Boomerang button. Base values 4 move, 3 defend, 3 attack, 3 distance, 9 initiative. Actual editable stone meshes, incised numerals/name and 3D forged blade. Hidden green4/red2 attack faces are material previews, not a computed gameplay state. No Unity integration or texture baking. Source created by build_r2b01_hero_button.py and r2b01_boomerang_hero.py. Workbench.blend is not used. Generated icon is original geometry. Rotate BUTTON root or use timeline to inspect relief depth. All fonts converted to cuts; no external image assets.')
for area in bpy.context.screen.areas:
    if area.type=='VIEW_3D':
        area.spaces.active.region_3d.view_perspective='CAMERA'
        area.spaces.active.shading.type='MATERIAL'
SOURCE.parent.mkdir(parents=True,exist_ok=True)
bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE),compress=True)
report={'source':str(SOURCE.relative_to(ROOT)),'blender':bpy.app.version_string,'cuts':CUTS,
        'objects':len(MODEL.objects),'vertices':sum(len(o.data.vertices) for o in MODEL.objects if o.type=='MESH'),
        'icon_objects':len(ICON),'unity_integrated':False,'art_acceptance':'pending user review',
        'values':{'move':4,'defence':3,'attack':3,'attack_distance':3,'initiative':9},
        'external_images':[],'render':'CPU Cycles actual geometry'}
(OUT/'model-report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf8')
if not A.no_render:
    if A.preview:SC.render.resolution_percentage=60;SC.cycles.samples=24
    SC.render.filepath=str(OUT/'01-front.png')
    bpy.ops.render.render(write_still=True)
print('R2B01_SINGLE_BUTTON_DONE',str(SOURCE),flush=True)
