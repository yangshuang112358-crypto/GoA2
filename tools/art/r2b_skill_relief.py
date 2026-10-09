"""Editable R2B skill-centre reliefs; no scene setup, exports or side effects.

Usage from a Blender generator:
    objects = build_relief('boomerang', materials, radius=1.0)

materials contains Blender materials under bronze/dark_metal/stone/rune/silver.
The returned mesh/curve objects are centred at the origin, face +Z and fit
X [-.65,.65], Y [-.12,.72], Z [.18,.50] multiplied by radius. The lower area is
reserved for the skill name. These are original geometry, not image planes.
"""
import math

import bpy
import bmesh
from mathutils import Vector


def _mesh(name, vertices, faces, material, radius, objects, smooth=False, bevel=0):
    data = bpy.data.meshes.new('R2B_' + name + '_Mesh')
    data.from_pydata([tuple(radius * c for c in p) for p in vertices], [], faces)
    data.update()
    # All mesh parts are closed relief solids; normalize outward winding without
    # changing selection, active object or edit mode in the caller's scene.
    bm = bmesh.new()
    bm.from_mesh(data)
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bm.to_mesh(data)
    bm.free()
    data.update()
    obj = bpy.data.objects.new('R2B_' + name, data)
    bpy.context.collection.objects.link(obj)
    data.materials.append(material)
    for polygon in data.polygons:
        polygon.use_smooth = smooth
    if bevel:
        modifier = obj.modifiers.new('Editable forged edge', 'BEVEL')
        modifier.width, modifier.segments = bevel * radius, 2
        modifier.limit_method = 'ANGLE'
    obj['R2B_relief'] = True
    objects.append(obj)
    return obj


def _line(name, coordinates, width, material, radius, objects, smooth=False):
    data = bpy.data.curves.new('R2B_' + name + '_Curve', 'CURVE')
    data.dimensions, data.resolution_u = '3D', 16
    data.bevel_depth, data.bevel_resolution = width * radius, 3
    data.use_fill_caps = True
    spline = data.splines.new('BEZIER' if smooth else 'POLY')
    if smooth:
        spline.bezier_points.add(len(coordinates) - 1)
        for point, coordinate in zip(spline.bezier_points, coordinates):
            point.co = tuple(c * radius for c in coordinate)
            point.handle_left_type = point.handle_right_type = 'AUTO'
    else:
        spline.points.add(len(coordinates) - 1)
        for point, coordinate in zip(spline.points, coordinates):
            point.co = (*[c * radius for c in coordinate], 1)
    obj = bpy.data.objects.new('R2B_' + name, data)
    bpy.context.collection.objects.link(obj)
    data.materials.append(material)
    obj['R2B_relief'] = True
    objects.append(obj)
    return obj


def _blade_point(t, across, height):
    angle = math.pi * t
    # The lower tip projects toward the viewer while the upper arm recedes. This
    # is real relief depth, so the bend stays readable without painted perspective.
    center = Vector((-.310 + .713 * math.sin(angle), .635 - .607 * t, height + (t - .5) * .092))
    tangent = Vector((.713 * math.pi * math.cos(angle), -.607, 0)).normalized()
    lateral = Vector((-tangent.y, tangent.x, 0))
    width = .0105 + .117 * math.sin(angle) ** .72
    return tuple(center + lateral * across * width)


def _blade_section(name, start, stop, material, radius, objects, top=.376, inset=0):
    # A closed diamond/shoulder cross-section: cutting edge, bevel, raised keel,
    # bevel, cutting edge, then a shallow solid back. Faces remain editable.
    cross = [(-1 + inset, .291), (-.83 + inset, .352), (-.42 + inset, top - .013),
             (.09, top), (.66 - inset, top - .026), (1 - inset, .303),
             (.93 - inset, .236), (-.93 + inset, .236)]
    rows, vertices, faces = 9, [], []
    for i in range(rows):
        t = start + (stop - start) * i / (rows - 1)
        for across, height in cross:
            vertices.append(_blade_point(t, across, height))
    n = len(cross)
    for i in range(rows - 1):
        for j in range(n):
            a, b = i * n + j, i * n + (j + 1) % n
            faces.append((a, b, b + n, a + n))
    faces += [tuple(reversed(range(n))), tuple(range((rows - 1) * n, rows * n))]
    return _mesh(name, vertices, faces, material, radius, objects, bevel=.0018)


def _boomerang(materials, radius, objects):
    # Dark continuous forged substrate is visible only between fitted plates.
    _blade_section('Boomerang_ContinuousForging', 0, 1, materials['dark_metal'], radius, objects, .363)
    divisions = (0, .13, .255, .375, .50, .625, .745, .87, 1)
    for index, (a, b) in enumerate(zip(divisions, divisions[1:])):
        gap = .004 if 0 < index < 7 else .002
        _blade_section('Boomerang_ForgedPlate_%02d' % index, a + gap, b - gap,
                       materials['silver'], radius, objects, .433, .025)
    for index, t in enumerate((.285, .715)):
        # Bronze keyed inlays are slim surface fittings, not oversized bands.
        _line('Boomerang_BronzeKey_%02d' % index,
              [_blade_point(t + a * .010, a, .435 - abs(a) * .024)
               for a in (-.60, -.34, 0, .34, .60)], .007,
              materials['bronze'], radius, objects)
    for side in (-1, 1):
        _line('Boomerang_HonedEdge_%s' % ('Inner' if side > 0 else 'Outer'),
              [_blade_point(.013 + i / 52 * .974, side * .986, .304 if side > 0 else .292) for i in range(53)],
              .0023, materials['silver'], radius, objects)
    _line('Boomerang_ArcUpper', [(-.444, .522, .405), (-.486, .591, .416),
        (-.402, .657, .423), (-.260, .682, .425), (-.132, .652, .418),
        (-.053, .618, .413)], .0055, materials['rune'], radius, objects)
    _line('Boomerang_ArcSweep', [(.457, .548, .410), (.515, .482, .423),
        (.548, .395, .431), (.523, .321, .435), (.568, .277, .424),
        (.508, .169, .416), (.370, .077, .408), (.193, .024, .402)],
        .006, materials['rune'], radius, objects)
    _line('Boomerang_ArcLower', [(.192, .024, .402), (.051, -.022, .395),
        (-.130, -.038, .390), (-.326, -.008, .387)], .0032,
        materials['rune'], radius, objects)


def _palm(materials, radius, objects):
    # An open, cupped glove form. The asymmetric thenar edge connects to the thumb;
    # the wide knuckle line connects to the four fingers above it.
    outline = [(-.090, -.059), (.078, -.059), (.101, .014), (.160, .079),
               (.174, .180), (.144, .257), (.077, .292), (-.014, .305),
               (-.108, .269), (-.158, .220), (-.174, .153), (-.149, .081)]
    outline = [(x * 1.23, y * 1.13) for x, y in outline]
    center = Vector((-.005, .159, 0))
    vertices, faces = [], []
    scales = (.10, .36, .70, 1.0)
    n = len(outline)
    for t in scales:
        for x, y in outline:
            p = center.lerp(Vector((x, y, 0)), t)
            # Sculpted hollow in the palm, with a higher heel/knuckle rim.
            z = .315 + .061 * t ** 1.3 + .014 * max(0, (p.y - .19) / .15)
            z -= .034 * max(0, (.045 - p.y) / .115)
            vertices.append((p.x, p.y, z))
    for row in range(len(scales) - 1):
        for j in range(n):
            a, b = row * n + j, row * n + (j + 1) % n
            faces.append((a, b, b + n, a + n))
    faces.append(tuple(reversed(range(n))))
    lower = len(vertices)
    vertices += [(x, y, .218) for x, y in outline]
    for j in range(n):
        faces.append(((len(scales) - 1) * n + j, (len(scales) - 1) * n + (j + 1) % n,
                      lower + (j + 1) % n, lower + j))
    faces.append(tuple(range(lower, lower + n)))
    _mesh('Telekinesis_CuppedPalm', vertices, faces, materials['silver'], radius, objects, smooth=True)
    # Cuff is a shallow, shaped casting and leaves the name zone below -.12 empty.
    cuff_outline = [(-.102, -.081, .246), (.091, -.081, .246), (.101, -.015, .288),
                    (.080, .013, .326), (-.099, .013, .326), (-.115, -.015, .288)]
    vertices = cuff_outline + [(x, y, .198) for x, y, _ in cuff_outline]
    faces = [tuple(range(6)), tuple(reversed(range(6, 12)))]
    faces += [(i, (i + 1) % 6, (i + 1) % 6 + 6, i + 6) for i in range(6)]
    _mesh('Telekinesis_Cuff', vertices, faces, materials['dark_metal'], radius, objects, bevel=.006)
    _line('Telekinesis_CuffLip', [(-.104, -.018, .300), (-.081, -.030, .327),
        (.066, -.030, .327), (.092, -.018, .300)], .006,
        materials['bronze'], radius, objects, smooth=True)
    # Fine surface creases make the palm readable without hiding it in ornaments.
    _line('Telekinesis_ThenarCrease', [(-.133, .064, .365), (-.156, .124, .365),
        (-.127, .189, .343), (-.075, .222, .345)], .0025,
        materials['dark_metal'], radius, objects, smooth=True)
    _line('Telekinesis_KnuckleCrease', [(-.094, .270, .365), (.003, .288, .367),
        (.118, .271, .375)], .0025, materials['dark_metal'], radius, objects, smooth=True)


def _bezier(points, t):
    a, b, c, d = [Vector(p) for p in points]
    return (1 - t) ** 3 * a + 3 * (1 - t) ** 2 * t * b + 3 * (1 - t) * t * t * c + t ** 3 * d


def _finger(name, path, width, materials, radius, objects):
    vertices, faces, frames = [], [], []
    n, rows = 12, 25
    for i in range(rows):
        t = i / (rows - 1)
        center = _bezier(path, t)
        tangent = (_bezier(path, min(1, t + .006)) - _bezier(path, max(0, t - .006))).normalized()
        # Flat relief depth is retained even when the fingertip curls toward stone.
        lateral = Vector((-tangent.y, tangent.x, 0)).normalized()
        knuckle = .12 * math.exp(-((t - .34) / .085) ** 2) + .09 * math.exp(-((t - .68) / .075) ** 2)
        taper = (.94 - .44 * t + knuckle) * math.sqrt(max(.015, 1 - t ** 10))
        half_width = width * taper
        depth = half_width * .66
        frames.append((center, lateral, half_width, depth))
        for j in range(n):
            a = j * math.tau / n
            p = center + lateral * math.cos(a) * half_width + Vector((0, 0, math.sin(a) * depth))
            vertices.append(tuple(p))
    for i in range(rows - 1):
        for j in range(n):
            a, b = i * n + j, i * n + (j + 1) % n
            faces.append((a, b, b + n, a + n))
    faces += [tuple(reversed(range(n))), tuple(range((rows - 1) * n, rows * n))]
    _mesh('Telekinesis_' + name, vertices, faces, materials['silver'], radius, objects, smooth=True)
    for index in (8, 16):
        center, lateral, w, d = frames[index]
        points = [tuple(center + lateral * math.cos(a) * w + Vector((0, 0, math.sin(a) * d + .002)))
                  for a in [j / 8 * math.pi for j in range(9)]]
        _line('Telekinesis_' + name + '_Joint_%02d' % index, points, .0021,
              materials['dark_metal'], radius, objects)


def _rock(name, center, size, materials, radius, objects, phase=0):
    cx, cy, cz = center
    sx, sy, sz = size
    n, vertices, faces = 7, [], []
    # Deliberate mineral fracture planes rather than a regular octahedron.
    for row, (height, spread) in enumerate(((-1, .35), (-.44, .89), (.18, 1), (.76, .59))):
        for j in range(n):
            a = j * math.tau / n + phase + (row % 2) * .17
            irregular = 1 + .12 * math.sin(j * 2.81 + row * 1.14)
            vertices.append((cx + math.cos(a) * sx * spread * irregular,
                             cy + math.sin(a) * sy * spread * irregular,
                             cz + height * sz))
    for i in range(3):
        for j in range(n):
            a, b = i * n + j, i * n + (j + 1) % n
            # Alternating fractures prevent a smooth faceted cylinder silhouette.
            if (i + j) % 2:
                faces += [(a, b, a + n), (b, b + n, a + n)]
            else:
                faces.append((a, b, b + n, a + n))
    faces += [tuple(reversed(range(n))), tuple(range(3 * n, 4 * n))]
    _mesh('Telekinesis_' + name, vertices, faces, materials['stone'], radius, objects, bevel=.001)


def _telekinesis(materials, radius, objects):
    _palm(materials, radius, objects)
    # Shorter fingers cup inward toward the stone, with a distinct low opposing
    # thumb. Their lengths are subordinate to the enlarged palm, not antler rays.
    fingers = [('Thumb', [(-.166, .104, .341), (-.270, .163, .358), (-.249, .235, .394), (-.170, .273, .416)], .050),
        ('Index', [(-.128, .275, .359), (-.176, .337, .372), (-.143, .401, .405), (-.087, .434, .426)], .038),
        ('Middle', [(-.033, .320, .365), (-.059, .386, .382), (-.046, .441, .413), (-.008, .463, .434)], .043),
        ('Ring', [(.079, .307, .361), (.132, .362, .381), (.135, .416, .408), (.098, .449, .430)], .037),
        ('Little', [(.171, .259, .352), (.238, .296, .374), (.235, .354, .397), (.180, .392, .418)], .031)]
    for name, points, width in fingers:
        _finger(name, points, width, materials, radius, objects)
    _rock('SuspendedStone', (.025, .580, .409), (.150, .105, .079), materials, radius, objects, .16)
    _rock('SatelliteShardLeft', (-.287, .528, .373), (.031, .030, .031), materials, radius, objects, .84)
    _rock('SatelliteShardRight', (.283, .578, .392), (.026, .024, .031), materials, radius, objects, .30)
    _line('Telekinesis_RisingArc', [(.248, .190, .348), (.326, .304, .386),
        (.294, .445, .424), (.165, .522, .450), (.035, .544, .462)],
        .0046, materials['rune'], radius, objects, smooth=True)
    _line('Telekinesis_StoneOrbit', [(-.145, .605, .429), (-.025, .652, .412),
        (.162, .618, .430), (.200, .572, .451), (.099, .542, .464)], .0035,
        materials['rune'], radius, objects, smooth=True)


def build_relief(kind, materials, radius=1.0):
    """Return independent editable objects; caller owns parenting and transforms."""
    if kind not in ('boomerang', 'telekinesis'):
        raise ValueError('Supported R2B reliefs: boomerang, telekinesis')
    if not math.isfinite(radius) or radius <= 0:
        raise ValueError('radius must be a finite positive number')
    required = {'bronze', 'dark_metal', 'stone', 'rune', 'silver'}
    missing = required.difference(materials)
    if missing:
        raise ValueError('Missing relief materials: ' + ', '.join(sorted(missing)))
    objects = []
    (_boomerang if kind == 'boomerang' else _telekinesis)(materials, radius, objects)
    for obj in objects:
        obj['R2B_skill_kind'] = kind
        obj['R2B_authoring_radius'] = radius
    return objects
