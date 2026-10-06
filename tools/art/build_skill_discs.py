"""Sculpted skill frames and inset stat stones, baked from editable Blender geometry.

Visual reference: Blizzard's Mercenaries ability rims and sculpted speed feather.
All meshes/materials below are authored here; reference images are not packaged.
Numbers, card names and card colours remain live Unity data, never baked text.
"""
import bpy
import math
import sys
from pathlib import Path

root = Path(sys.argv[sys.argv.index('--') + 1]).resolve()
out = root / 'unity/Assets/Resources/UI3D/SkillDiscs'
out.mkdir(parents=True, exist_ok=True)
source = root / 'art/ui/SkillDiscs.blend'
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.preferences.filepaths.save_version = 0


def material(name, color, metal=0, rough=.5, grain=0):
    m = bpy.data.materials.new(name)
    m.diffuse_color = (*color, 1)
    m.use_nodes = True
    shader = m.node_tree.nodes.get('Principled BSDF')
    shader.inputs['Base Color'].default_value = (*color, 1)
    shader.inputs['Metallic'].default_value = metal
    shader.inputs['Roughness'].default_value = rough
    if grain:
        noise = m.node_tree.nodes.new('ShaderNodeTexNoise')
        noise.inputs['Scale'].default_value = 38
        noise.inputs['Detail'].default_value = 3
        bump = m.node_tree.nodes.new('ShaderNodeBump')
        bump.inputs['Strength'].default_value = grain
        bump.inputs['Distance'].default_value = .032
        m.node_tree.links.new(noise.outputs['Fac'], bump.inputs['Height'])
        m.node_tree.links.new(bump.outputs['Normal'], shader.inputs['Normal'])
    return m


bronze = material('Warm forged bronze', (.32, .20, .085), .78, .31, .15)
gold = material('Worn gold bevel', (.62, .43, .18), .68, .3, .1)
shadow = material('Deep seam', (.018, .026, .03), .28, .6)
face = material('Midnight blue stone', (.035, .066, .080), .25, .53, .27)
stone = material('Warm grey speed stone', (.31, .32, .28), .27, .46, .32)
edge = material('Cut silver stone bevel', (.53, .53, .45), .5, .36, .18)
etch = material('Carved symbols', (.042, .055, .058), .2, .52)
steel = material('Discarded brushed steel', (.19, .24, .26), .72, .4, .25)
variants = {}
active = None


def keep(obj, mat):
    obj.data.materials.append(mat)
    for col in list(obj.users_collection):
        col.objects.unlink(obj)
    active.objects.link(obj)
    return obj


def variant(name):
    global active
    active = bpy.data.collections.new(name)
    bpy.context.scene.collection.children.link(active)
    variants[name] = active


def poly(name, points, z, depth, mat, bevel=.03):
    n = len(points)
    verts = [(x, y, z + d) for d in (-depth / 2, depth / 2) for x, y in points]
    faces = [tuple(reversed(range(n))), tuple(range(n, 2 * n))]
    faces += [(i, (i + 1) % n, (i + 1) % n + n, i + n) for i in range(n)]
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(verts, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    active.objects.link(obj)
    mesh.materials.append(mat)
    if bevel:
        mod = obj.modifiers.new('Sculpted chamfer', 'BEVEL')
        mod.width = bevel
        mod.segments = 3
        obj.modifiers.new('Weighted surface normals', 'WEIGHTED_NORMAL')
    return obj


def circle(name, radius, z, depth, mat, segments=80):
    return poly(name, [(math.cos(i * math.tau / segments) * radius,
                       math.sin(i * math.tau / segments) * radius)
                      for i in range(segments)], z, depth, mat, .025)


def ring(name, outer, inner, z, mat):
    n = 96
    verts = [(math.cos(i * math.tau / n) * r, math.sin(i * math.tau / n) * r, h)
             for r, h in [(outer, z), (outer - .026, z + .065),
                          (inner + .022, z + .065), (inner, z)] for i in range(n)]
    faces = []
    for row in range(3):
        for i in range(n):
            faces.append((row * n + i, row * n + (i + 1) % n,
                          (row + 1) * n + (i + 1) % n, (row + 1) * n + i))
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(verts, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    active.objects.link(obj)
    mesh.materials.append(mat)
    return obj


variant('front')
circle('Heavy outer casing', 1.40, 0, .17, shadow)
ring('Forged outer lip', 1.38, 1.25, .045, bronze)
ring('Light-catching gilded bevel', 1.31, 1.19, .12, gold)
ring('Recessed inlay channel', 1.23, 1.15, .11, shadow)
circle('Inset skill face', 1.15, .09, .13, face)
ring('Inner hairline bevel', 1.16, 1.12, .165, bronze)
for i in range(8):
    a = i * math.tau / 8
    r = 1.33
    bpy.ops.mesh.primitive_uv_sphere_add(segments=12, ring_count=6, radius=.048,
                                       location=(math.cos(a)*r, math.sin(a)*r, .13))
    o = keep(bpy.context.object, gold)
    o.name = 'Hammered rim pin'
    o.scale.z = .4

variant('back')
circle('Reverse casing', 1.4, 0, .17, shadow)
ring('Reverse bronze lip', 1.38, 1.20, .06, bronze)
circle('Steel reverse', 1.20, .09, .14, steel)
ring('Reverse engraving border', 1.10, 1.08, .17, shadow)
cross = [(-.61, -.47), (-.47, -.61), (0, -.14), (.47, -.61), (.61, -.47),
         (.14, 0), (.61, .47), (.47, .61), (0, .14), (-.47, .61), (-.61, .47), (-.14, 0)]
poly('Sunken discard X', cross, .169, .008, shadow, .016)

glyphs = {
    'boot': [(-.16,.16),(.1,.16),(.065,-.06),(.22,-.12),(.22,-.23),(-.19,-.23),(-.19,-.11),(-.13,-.06)],
    'shield': [(-.22,.18),(0,.26),(.22,.18),(.18,-.09),(0,-.26),(-.18,-.09)],
    'sword': [(-.19,-.26),(-.26,-.19),(-.07,0),(-.18,.11),(-.11,.18),(0,.07),(.19,.29),(.28,.30),(.28,.20),(.08,0),(.18,-.11),(.11,-.18),(0,-.07)],
    'spark': [(0,.27),(.07,.08),(.25,0),(.07,-.07),(0,-.25),(-.07,-.07),(-.25,0),(-.07,.08)],
    'arrow': [(-.21,-.26),(-.28,-.19),(.07,.16),(-.06,.19),(.29,.30),(.19,-.04),(.16,.08)],
    'range': [(0,.27),(.27,0),(0,-.27),(-.27,0)],
}
outline = [(-.38,-.46),(-.49,-.28),(-.46,.32),(-.25,.48),(.24,.46),(.45,.29),(.47,-.30),(.26,-.48)]
for kind, glyph in glyphs.items():
    variant(kind)
    poly('Dark socket', [(x*1.075,y*1.075) for x,y in outline], -.045, .14, shadow, .04)
    poly('Stone outer bevel', outline, .025, .17, edge, .055)
    poly('Recessed grey tablet', [(x*.87,y*.88) for x,y in outline], .103, .065, stone, .025)
    # Top-centred carved symbol; lower two thirds stay clear for the live number.
    points = [(x*.53,y*.53+.275) for x,y in glyph]
    poly('Engraved '+kind, points, .139, .006, etch, .005)
    if kind == 'range':
        circle('Range centre dot', .036, .144, .006, edge, 20).location.y=.275
    if kind == 'shield':
        poly('Shield highlight ridge', [(-.009,.16),(.009,.16),(.009,.35),(-.009,.35)], .147, .006, edge, .003)

variant('speed')
# Feathered shoulders are connected to the stone, never floating outside the button.
for side in (-1, 1):
    for j in range(3):
        y = -.24 + j*.13
        wing = [(side*.16,y-.10),(side*.59,y-.065),(side*.76,y+.045),
                (side*.79,y+.17),(side*.63,y+.105),(side*.31,y+.10)]
        if side < 0:
            wing.reverse()
        poly('Carved feather '+str(side)+' '+str(j), wing, .015+j*.022, .09, edge, .038)
        poly('Feather inset', [(x*.96, yy-.018) for x,yy in wing], .074+j*.022, .026, stone, .021)
speed = [(-.27,-.41),(-.41,-.24),(-.34,.27),(-.14,.36),(.16,.34),(.37,.20),(.39,-.24),(.20,-.41)]
poly('Speed central worn bevel', speed, .08, .17, edge, .048)
poly('Speed number face', [(x*.83,y*.85) for x,y in speed], .167, .027, stone, .024)

scene = bpy.context.scene
scene.render.engine = 'CYCLES'
scene.cycles.device = 'CPU'
scene.cycles.samples = 32
scene.cycles.use_denoising = True
scene.render.film_transparent = True
scene.render.image_settings.file_format = 'PNG'
scene.render.image_settings.color_mode = 'RGBA'
scene.render.resolution_percentage = 100
scene.view_settings.view_transform = 'AgX'
scene.world = bpy.data.worlds.new('Soft environment')
scene.world.use_nodes = True
scene.world.node_tree.nodes.get('Background').inputs[0].default_value = (.3,.34,.4,1)
scene.world.node_tree.nodes.get('Background').inputs[1].default_value = .55
camera = bpy.data.objects.new('Front orthographic bake', bpy.data.cameras.new('Front orthographic bake'))
scene.collection.objects.link(camera)
camera.location = (0,0,9)
camera.rotation_euler = (0,0,0)
camera.data.type = 'ORTHO'
scene.camera = camera
for name, pos, energy, color, size in [('Warm key',(-3,4,6),480,(1,.92,.77),4),('Cool fill',(4,-1,5),210,(.73,.83,1),5)]:
    data = bpy.data.lights.new(name,'AREA')
    data.energy = energy
    data.color = color
    data.size = size
    obj = bpy.data.objects.new(name,data)
    scene.collection.objects.link(obj)
    obj.location = pos
    obj.rotation_euler = (-obj.location).to_track_quat('-Z','Y').to_euler()
for name in variants:
    for key,col in variants.items():
        col.hide_render = key != name
        col.hide_viewport = key != name
    if name in ('front','back'):
        camera.data.ortho_scale = 3.1
        scene.render.resolution_x = scene.render.resolution_y = 512
    elif name == 'speed':
        camera.data.ortho_scale = 1.72
        scene.render.resolution_x,scene.render.resolution_y = 448,288
    else:
        camera.data.ortho_scale = 1.05
        scene.render.resolution_x,scene.render.resolution_y = 256,288
    scene.render.filepath = str(out/(name+'.png'))
    bpy.ops.render.render(write_still=True)
for key,col in variants.items():
    col.hide_render = col.hide_viewport = key != 'front'
camera.data.ortho_scale = 3.1
scene.render.resolution_x = scene.render.resolution_y = 512
scene['README'] = 'Show one named collection to edit a variant. Unity supplies live white/green/red numerals and colour inlay. No downloaded artwork included.'
bpy.ops.wm.save_as_mainfile(filepath=str(source),compress=True)
print('GOA_SKILL_DISCS_READY')
