"""Blender 4.5: restrained fractured Atlantis rock masses, not stacked hex pillars.

Run: blender -b --factory-startup --python tools/art/build_refined_rocks.py -- <repo>
The canonical exterior ground corners and connected roofs are preserved. Every
shape field is even about the board centre, giving exact 180-degree symmetry.
This is a new production source; the earlier rock .blend and generator stay intact.
"""
import json
import math
import sys
from collections import Counter
from pathlib import Path

import bpy
from mathutils import Vector


ROOT = Path(sys.argv[sys.argv.index('--') + 1]).resolve()
OUT = ROOT / 'unity/Assets/Resources/UI3D/Terrain'
SOURCE = ROOT / 'art/production/assets/RefinedRocks.blend'
OUT.mkdir(parents=True, exist_ok=True)
SOURCE.parent.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.preferences.filepaths.save_version = 0
EXPORT = bpy.data.collections.new('EXPORT')
bpy.context.scene.collection.children.link(EXPORT)


def world(x, y):
    return Vector((math.sqrt(3) * (x + y * .5), -1.5 * y, 0))


CELLS = [c for c in json.loads((ROOT / 'content/canonical/map.json').read_text(encoding='utf8'))['cells'] if c['obstacle']]
CENTRE = world(0, .5)
CENTRES = [world(c['x'], c['y']) for c in CELLS]
CENTRAL = {(0, 0), (0, 1)}


def field(point, scale=1):
    """Broad stone variation, exactly invariant under p -> 2*CENTRE-p."""
    d = point - CENTRE
    return (.57 * math.cos((d.x * 1.71 + d.y * .63) * scale)
            + .28 * math.cos((d.x * .49 - d.y * 1.91) * scale)
            + .15 * math.cos((d.x * 2.73 + d.y * 1.27) * scale))


def inset(point, amount, height):
    # Identical corner/edge input on neighboring hexes produces identical output.
    # At an internal edge the average is the point itself. At the exterior it
    # moves toward the stone mass, never out into a walkable hex.
    adjacent = [c for c in CENTRES if (point - c).length < 1.001]
    assert adjacent, 'Surface point must belong to an obstacle'
    mean = sum(adjacent, Vector()) / len(adjacent)
    p = point.lerp(mean, amount)
    p.z = height
    return p


class Builder:
    def __init__(self):
        self.vertices = []
        self.faces = []
        self.indices = {}

    def vertex(self, point):
        key = tuple(round(float(v), 7) for v in point)
        if key not in self.indices:
            self.indices[key] = len(self.vertices)
            self.vertices.append(key)
        return self.indices[key]

    def tri(self, a, b, c):
        ids = tuple(self.vertex(v) for v in (a, b, c))
        assert len(set(ids)) == 3, 'Degenerate stone triangle'
        self.faces.append(ids)

    def object(self, name):
        mesh = bpy.data.meshes.new(name)
        mesh.from_pydata(self.vertices, [], self.faces)
        mesh.update()
        obj = bpy.data.objects.new(name, mesh)
        EXPORT.objects.link(obj)
        mesh.materials.append(stone_material())
        # Flat shading preserves deliberately cut planes. Broad bevel strips,
        # rather than global smoothing, provide readable light-catching edges.
        for poly in mesh.polygons:
            poly.use_smooth = False
        return obj


def stone_material():
    material = bpy.data.materials.get('Atlantis hewn stone')
    if material:
        return material
    material = bpy.data.materials.new('Atlantis hewn stone')
    material.diffuse_color = (.30, .32, .29, 1)
    material.use_nodes = True
    nodes = material.node_tree.nodes
    shader = nodes.get('Principled BSDF')
    shader.inputs['Roughness'].default_value = .86
    shader.inputs['Specular IOR Level'].default_value = .23
    tex = nodes.new('ShaderNodeTexNoise')
    tex.inputs['Scale'].default_value = 2.0
    tex.inputs['Detail'].default_value = 1.4
    ramp = nodes.new('ShaderNodeValToRGB')
    ramp.color_ramp.elements[0].position = .2
    ramp.color_ramp.elements[0].color = (.18, .205, .19, 1)
    ramp.color_ramp.elements[1].position = .8
    ramp.color_ramp.elements[1].color = (.38, .385, .33, 1)
    material.node_tree.links.new(tex.outputs['Fac'], ramp.inputs['Fac'])
    material.node_tree.links.new(ramp.outputs['Color'], shader.inputs['Base Color'])
    # Runtime uses the dedicated Terrain shader, not these Blender nodes.
    return material


rocks = Builder()
roof_points = []
expected_floor = set()
for cell in CELLS:
    at = world(cell['x'], cell['y'])
    central = (cell['x'], cell['y']) in CENTRAL
    # Derive corners from the integer half-hex lattice, not centre+trigonometry.
    # mathutils is float32: evaluating the same shared corner through two centre
    # additions can otherwise differ by micrometres and break welded topology.
    corner_offsets = ((1, 1), (0, 2), (-1, 1), (-1, -1), (0, -2), (1, -1))
    corners = [Vector((math.sqrt(3) * .5 * (2 * cell['x'] + cell['y'] + dx),
                       .5 * (-3 * cell['y'] + dy), 0)) for dx, dy in corner_offsets]
    outline = []
    for i in range(6):
        outline += [corners[i], (corners[i] + corners[(i + 1) % 6]) * .5]
    outer = []
    for p in outline:
        # Sparse chiselled losses in the top arris: an inward nick and a small
        # height loss, not rubble sticking into neighboring playable hexes.
        chip = max(0, (field(p, 3.1) - .28) / .72)
        height = .34 if central else max(1.045, 1.065 + field(p) * .018 - chip * .012)
        outer.append(inset(p, .105 + field(p, 1.2) * .070 + chip * .055, height))
    inner = []
    for p in outline:
        q = at.lerp(p, .43)
        q.z = .34 if central else 1.106 + field(q) * .020
        inner.append(q)
    hub = at.copy()
    hub.z = .34 if central else 1.117 + field(at) * .017
    if not central:
        roof_points += outer + inner + [hub]
    for i in range(12):
        j = (i + 1) % 12
        rocks.tri(hub, inner[i], inner[j])
        rocks.tri(inner[i], outer[i], outer[j])
        rocks.tri(inner[i], outer[j], inner[j])

    # Only two broad fracture paths. Their heights vary by 25--30 cm around
    # each mass instead of forming horizontal, repeated plinth rings. Inset
    # changes independently at each path, creating leaning, broken stone faces.
    # A shared point still uses the same fields on both neighboring side faces.
    profiles = [(.29, .135, 1.35, .055, .042, 2.1),
                (.70, .140, .84, .083, .052, 1.63)]
    for i in range(6):
        a, b = corners[i], corners[(i + 1) % 6]
        mid = (a + b) * .5
        other = at + (mid - at) * 2
        if any((other - c).length < .001 for c in CENTRES if (c - at).length > .2):
            continue
        expected_floor.update(tuple(round(float(v), 7) for v in p) for p in (a, b))
        bands = []
        for height, relief, frequency, amount, erosion, cut_frequency in profiles:
            band = []
            for p in (a, mid, b):
                z = (height + relief * field(p, frequency)) * (.29 if central else 1)
                band.append(inset(p, amount + erosion * field(p, cut_frequency), z))
            bands.append(band)
        bands.append([outer[i * 2], outer[i * 2 + 1], outer[(i * 2 + 2) % 12]])
        # Bottom contains only original canonical corners, no added mid-edge
        # feet: compatible with the exact footprint audit and existing terrain.
        u, m, v = bands[0]
        rocks.tri(u, a, b)
        rocks.tri(u, b, m)
        rocks.tri(m, b, v)
        for low, high in zip(bands, bands[1:]):
            for j in range(2):
                rocks.tri(high[j], low[j], low[j + 1])
                rocks.tri(high[j], low[j + 1], high[j + 1])


def validate_rocks(builder):
    points = builder.vertices
    # Tolerant spatial lookup avoids false failures when the reflected point is
    # at a decimal rounding boundary. The geometry tolerance is ten micrometres,
    # substantially tighter than the independent Unity one-millimetre audit.
    bins = {}
    for p in points:
        key = tuple(math.floor(v * 10000) for v in p)
        bins.setdefault(key, []).append(p)
    def has_mirror(p):
        q = (2 * CENTRE.x - p[0], 2 * CENTRE.y - p[1], p[2])
        key = tuple(math.floor(v * 10000) for v in q)
        return any(sum((a - b) ** 2 for a, b in zip(q, candidate)) < .00001 ** 2
                   for dx in (-1, 0, 1) for dy in (-1, 0, 1) for dz in (-1, 0, 1)
                   for candidate in bins.get((key[0] + dx, key[1] + dy, key[2] + dz), []))
    assert all(has_mirror(p) for p in points), 'Rock geometry lost central symmetry'
    floor = {p for p in points if abs(p[2]) < .00001}
    assert floor == expected_floor, 'Canonical exterior ground corners changed'
    edges = Counter(tuple(sorted((face[j], face[(j + 1) % 3]))) for face in builder.faces for j in range(3))
    assert all(count == 2 or count == 1 and abs(points[a][2]) < .00001 and abs(points[b][2]) < .00001
               for (a, b), count in edges.items()), 'Open seam above ground or non-manifold stone edge'
    heights = [p.z for p in roof_points]
    assert max(heights) - min(heights) <= .12, 'Roof relief exceeded 12 cm'
    assert 1.04 <= min(heights) <= max(heights) <= 1.15, 'Roof moved outside the existing height range'
    return {'vertices': len(points), 'triangles': len(builder.faces), 'ground_corners': len(floor),
            'ordinary_roof_min': min(heights), 'ordinary_roof_max': max(heights),
            'symmetry': '180 degrees; canonical exterior corner set; closed above ground'}


report = validate_rocks(rocks)
rock = rocks.object('ConnectedRocks')


def export(obj, name):
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.export_scene.fbx(filepath=str(OUT / (name + '.fbx')), use_selection=True,
                            object_types={'MESH'}, bake_anim=False, axis_forward='-Z',
                            axis_up='Y', mesh_smooth_type='FACE', add_leaf_bones=False)


export(rock, 'ConnectedRocks')
layout = {'width': max(v[0] for v in rocks.vertices) - min(v[0] for v in rocks.vertices),
          'height': max(v[2] for v in rocks.vertices), 'centerX': CENTRE.x, 'centerZ': CENTRE.y}
(OUT / 'rock-layout.json').write_text(json.dumps(layout), encoding='utf8')

# The altar remains static. Preserve the flat 1.14 surface, inner radius 1.045,
# outer radius 1.12 and 1.28 upright lip. Its underside has sculpted strata only.
altar = Builder()
segments = 48
rings = []
profiles = [(.38, .02, 100), (.455, .18, 84), (.445, .22, 80),
            (.53, .34, 66), (.57, .49, 49), (.56, .53, 45),
            (.73, .70, 29), (.81, .79, 23), (.91, .91, 12),
            (1.085, 1.055, 0), (1.12, 1.085, 0), (1.12, 1.14, 0)]
for radius, height, twist in profiles:
    ring = []
    for i in range(segments + 1):
        angle = -math.pi / 2 + i * math.pi / segments + math.radians(twist)
        # Low frequency angular cuts meet symmetrically in the rotated second
        # half. Upper disk and upright lip remain perfectly circular and level.
        cut = 0 if height >= 1.055 else .013 * math.sin(i * math.pi / segments) * math.cos(i * math.pi / 8)
        ring.append(Vector((math.cos(angle) * (radius + cut), math.sin(angle) * (radius + cut), height)))
    rings.append(ring)
for low, high in zip(rings, rings[1:]):
    for i in range(segments):
        altar.tri(low[i], low[i + 1], high[i + 1])
        altar.tri(low[i], high[i + 1], high[i])
    for end in (0, segments):
        a, b = low[end], high[end]
        lower, upper = Vector((0, 0, a.z)), Vector((0, 0, b.z))
        if end == 0:
            altar.tri(lower, b, a)
            altar.tri(lower, upper, b)
        else:
            altar.tri(lower, a, b)
            altar.tri(lower, b, upper)
for i in range(segments):
    angle = -math.pi / 2 + i * math.pi / segments
    next_angle = angle + math.pi / segments

    def p(theta, radius, height):
        return Vector((math.cos(theta) * radius, math.sin(theta) * radius, height))

    altar.tri(Vector((0, 0, 1.14)), p(angle, 1.045, 1.14), p(next_angle, 1.045, 1.14))
    for radius, reverse in ((1.12, False), (1.045, True)):
        u, v = p(angle, radius, 1.14), p(next_angle, radius, 1.14)
        w, x = p(angle, radius, 1.28), p(next_angle, radius, 1.28)
        if reverse:
            altar.tri(u, w, x)
            altar.tri(u, x, v)
        else:
            altar.tri(u, x, w)
            altar.tri(u, v, x)
    altar.tri(p(angle, 1.045, 1.28), p(angle, 1.12, 1.28), p(next_angle, 1.12, 1.28))
    altar.tri(p(angle, 1.045, 1.28), p(next_angle, 1.12, 1.28), p(next_angle, 1.045, 1.28))
# End walls close the upright semicircular lip. Second instance rotates 180°.
for angle, reverse in ((-math.pi / 2, False), (math.pi / 2, True)):
    a, b, c, d = (Vector((math.cos(angle) * r, math.sin(angle) * r, h))
                  for r, h in ((1.045, 1.14), (1.12, 1.14), (1.12, 1.28), (1.045, 1.28)))
    if reverse:
        altar.tri(a, c, b)
        altar.tri(a, d, c)
    else:
        altar.tri(a, b, c)
        altar.tri(a, c, d)
half = altar.object('CentralSpiralHalf')
half['static_sculpture'] = True
half['flat_coin_surface'] = 1.14
export(half, 'CentralSpiralHalf')
half.location = CENTRE
other = half.copy()
other.data = half.data
other.name = 'CentralSpiralHalf opposite'
other.rotation_euler.z = math.pi
EXPORT.objects.link(other)
scene = bpy.context.scene
scene.unit_settings.system = 'METRIC'
scene.unit_settings.scale_length = 1
scene['rock_validation'] = json.dumps(report)
scene['source_note'] = 'Original refined Goa2 stone; static terrain; canonical foot corners unchanged'
bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE), compress=True)
print('GOA_REFINED_ROCKS_READY ' + json.dumps(report))
