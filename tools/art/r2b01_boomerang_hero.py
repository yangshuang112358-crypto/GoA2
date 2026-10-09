"""Editable forged boomerang relief for the single R2B01 finished sample.

Only builds geometry in the supplied collection.  No scene, material, camera,
render, save, or process side effects.  All decorative strokes have volume.
The weapon's central ridge and outside bevel are part of its mesh, not a normal
map.  Its dark seam core, segmented plates, clasps and chiselled nicks remain
separately editable.
"""
import math
import random

import bpy
from mathutils import Vector


PREFIX = "R2B01_Boomerang_"


def _link(obj, collection):
    for old in list(obj.users_collection):
        old.objects.unlink(obj)
    collection.objects.link(obj)
    return obj


def _mesh(name, verts, faces, collection, materials, face_materials=None):
    mesh = bpy.data.meshes.new(PREFIX + name + "Mesh")
    mesh.from_pydata(verts, [], faces)
    mesh.update()
    ob = bpy.data.objects.new(PREFIX + name, mesh)
    collection.objects.link(ob)
    for mat in materials:
        ob.data.materials.append(mat)
    if face_materials:
        for p, i in zip(ob.data.polygons, face_materials):
            p.material_index = i
    # Recalculate once so boolean/weighted normal behaviour is reproducible.
    import bmesh
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(mesh)
    bm.free()
    return ob


def _bevel(ob, width=0.012, segments=3):
    bevel = ob.modifiers.new("Small polished edge catches", "BEVEL")
    bevel.width = width
    bevel.segments = segments
    bevel.limit_method = "ANGLE"
    bevel.angle_limit = 0.18
    bevel.harden_normals = True
    try:
        weighted = ob.modifiers.new("Weighted forged normals", "WEIGHTED_NORMAL")
        weighted.keep_sharp = True
        weighted.weight = 40
    except RuntimeError:
        pass


def _tube(name, points, radius, material, collection, radii=None):
    data = bpy.data.curves.new(PREFIX + name + "Curve", "CURVE")
    data.dimensions = "3D"
    data.resolution_u = 2
    data.bevel_depth = radius
    data.bevel_resolution = 3
    data.resolution_u = 2
    spline = data.splines.new("POLY")
    spline.points.add(len(points) - 1)
    for i, (p, co) in enumerate(zip(spline.points, points)):
        p.co = (*co, 1)
        if radii:
            p.radius = radii[i]
    data.use_fill_caps = True
    ob = bpy.data.objects.new(PREFIX + name, data)
    collection.objects.link(ob)
    ob.data.materials.append(material)
    return ob


def _curve_samples():
    # Carefully art-directed right-bending crescent. Upper/lower tips are left,
    # matching the approved concept instead of a generic chevron or two bars.
    return [
        (-1.18, 1.90, 0.026),
        (-0.97, 1.85, 0.110),
        (-0.68, 1.75, 0.195),
        (-0.34, 1.57, 0.245),
        (0.01, 1.33, 0.282),
        (0.29, 1.03, 0.312),
        (0.41, 0.77, 0.320),
        (0.20, 0.50, 0.282),
        (-0.13, 0.28, 0.242),
        (-0.51, 0.10, 0.176),
        (-0.86, -0.01, 0.102),
        (-1.09, -0.045, 0.023),
    ]


def _station(points, u):
    u = max(0.0, min(float(len(points) - 1), u))
    i = min(len(points) - 2, int(u))
    f = u - i
    a, b = points[i], points[i + 1]
    center = Vector((a[0] * (1 - f) + b[0] * f,
                     a[1] * (1 - f) + b[1] * f))
    width = a[2] * (1 - f) + b[2] * f
    def tangent(k):
        lo, hi = points[max(0, k - 1)], points[min(len(points) - 1, k + 1)]
        return Vector((hi[0] - lo[0], hi[1] - lo[1])).normalized()
    t = (tangent(i) * (1 - f) + tangent(i + 1) * f).normalized()
    return center, Vector((-t.y, t.x)), width, t


def _blade_segment(name, points, start, end, collection, mats,
                   width_scale=1, zbase=0.46, height=0.235):
    # Seven points per cross section form two edge bevels, two broad forged
    # planes and a narrow raised central spine. The ridge is deliberately off
    # centre to avoid the appearance of an extruded flat icon.
    profile = [(-1, 0.06), (-.82, .42), (-.31, .83),
               (-.10, 1), (.12, .98), (.75, .48), (1, .04)]
    stations = [start]
    stations += [float(i) for i in range(math.floor(start) + 1, math.ceil(end))
                 if start + .001 < i < end - .001]
    stations += [end]
    verts = []
    for u in stations:
        # Slight longitudinal crown makes it feel like a physical forged blade.
        crown = .034 * math.sin(math.pi * u / (len(points) - 1))
        for s, h in profile:
            # Staggered broken-joint construction. Every cross section is a
            # shallow dogleg, so neighbouring faces no longer read like evenly
            # sliced pieces of the same crescent. The same station function on
            # both sides leaves the polished outer silhouette continuous.
            fade = math.sin(math.pi * u / (len(points) - 1))
            shift = fade * (.17 * math.sin(u * 1.78 + s * 2.10)
                            + .045 * math.sin(s * 6.0 + u))
            p, n, w, _ = _station(points, u + shift)
            q = p + n * (w * width_scale * s)
            hammer_crown = .010 * math.sin(u * 1.93 + s * 2.8) * fade
            verts.append((q.x, q.y, zbase + height * h + crown + hammer_crown))
        for s in (-1, 1):
            p, n, w, _ = _station(points, u)
            q = p + n * (w * width_scale * s)
            verts.append((q.x, q.y, zbase - .075 + crown))
    faces, indices = [], []
    for j in range(len(stations) - 1):
        a, b = j * 9, (j + 1) * 9
        for k in range(6):
            faces.append((a + k, b + k, b + k + 1, a + k + 1))
            indices.append(1 if k in (0, 5) else 0)
        faces.extend([(a, a + 7, b + 7, b),
                      (a + 6, b + 6, b + 8, a + 8),
                      (a + 7, a + 8, b + 8, b + 7)])
        indices.extend((2, 2, 2))
    faces.append(tuple([0, 1, 2, 3, 4, 5, 6, 8, 7]))
    indices.append(2)
    base = (len(stations) - 1) * 9
    faces.append(tuple(base + i for i in (7, 8, 6, 5, 4, 3, 2, 1, 0)))
    indices.append(2)
    ob = _mesh(name, verts, faces, collection,
               [mats["steel"], mats["edge"], mats["dark"]], indices)
    _bevel(ob, .009, 3)
    return ob


def _catmull_points(controls, count=190):
    result = []
    for i in range(count):
        u = i / (count - 1) * (len(controls) - 1)
        k = min(len(controls) - 2, int(u))
        t = u - k
        p0 = Vector(controls[max(0, k - 1)])
        p1 = Vector(controls[k])
        p2 = Vector(controls[k + 1])
        p3 = Vector(controls[min(len(controls) - 1, k + 2)])
        p = .5 * ((2 * p1) + (-p0 + p2) * t
                  + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t*t
                  + (-p0 + 3*p1 - 3*p2 + p3) * t*t*t)
        result.append(tuple(p))
    return result


def _apply_cut(plate, cutter):
    # Apply only the boolean. Leave the actual bevel/normal stack editable.
    bpy.context.view_layer.objects.active = plate
    modifier = plate.modifiers.new("Hand chiselled shallow nick", "BOOLEAN")
    modifier.operation = "DIFFERENCE"
    modifier.solver = "EXACT"
    modifier.object = cutter
    bpy.ops.object.modifier_move_up(modifier=modifier.name)
    bpy.ops.object.modifier_move_up(modifier=modifier.name)
    bpy.ops.object.modifier_apply(modifier=modifier.name)
    bpy.data.objects.remove(cutter, do_unlink=True)


def _rock(name, location, size, collection, material, seed):
    rng = random.Random(seed)
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=1, radius=1, location=location)
    ob = _link(bpy.context.object, collection)
    ob.name = PREFIX + name
    for v in ob.data.vertices:
        jitter = rng.uniform(.77, 1.21)
        v.co.x *= size * jitter
        v.co.y *= size * rng.uniform(.7, 1.1)
        v.co.z *= size * rng.uniform(.55, .96)
    ob.rotation_euler = (rng.uniform(-.5, .5), rng.uniform(-.5, .5), rng.uniform(0, 6.28))
    ob.data.materials.append(material)
    _bevel(ob, .007, 2)
    return ob


def build_icon(collection, materials):
    """Return root + editable objects; icon fits x±1.68/y[-.35,2.25]/z[.18,.85]."""
    keys = ("steel", "edge", "bronze", "dark", "rock", "orange", "orange_hot")
    missing = [k for k in keys if k not in materials]
    if missing:
        raise KeyError("Missing icon materials: " + ", ".join(missing))
    objects = []
    root = bpy.data.objects.new(PREFIX + "EDITABLE_ROOT", None)
    collection.objects.link(root)
    root["role"] = "Original sculpted relief; not a generated-image plane"
    root["bounds"] = "x[-1.68,1.68] y[-.35,2.25] z[.18,.85]"
    points = _curve_samples()

    # A thin forged cutting layer is visible beneath the darker shell and warm
    # alloy seams. It is modelled separately, giving two catches at the edge.
    edge_mats = dict(materials, steel=materials["edge"], edge=materials["edge"])
    edge = _blade_segment("Continuous_thin_polished_cutting_layer", points, 0, 11,
                          collection, edge_mats, width_scale=1.018,
                          zbase=.414, height=.211)
    objects.append(edge)
    core_mats = dict(materials, steel=materials["bronze"], edge=materials["bronze"])
    core = _blade_segment("Continuous_seam_core", points, 0, 11, collection,
                          core_mats, width_scale=.975, zbase=.446, height=.221)
    objects.append(core)

    # Offset panel boundaries break mechanical repetition without changing the
    # clean outer silhouette. Two slender brass joints are deliberate accents.
    spans = [(0, 1.80), (1.85, 3.03), (3.065, 4.17), (4.205, 5.34),
             (5.385, 6.40), (6.435, 7.55), (7.585, 8.61), (8.655, 9.70), (9.735, 11)]
    plates = []
    for i, (a, b) in enumerate(spans):
        plate = _blade_segment("Forged_plate_%02d" % (i + 1), points, a, b,
                               collection, materials, width_scale=.985,
                               zbase=.468 + .006 * math.sin(i * 1.8),
                               height=.231 + .006 * math.cos(i * 2.1))
        plate["part"] = "Raised centre ridge, hand bevel and real segment seam"
        plates.append(plate)
        objects.append(plate)
    for i, (a, b) in enumerate(((1.802, 1.848), (5.343, 5.382), (8.613, 8.652))):
        clasp_mats = dict(materials, steel=materials["bronze"], edge=materials["bronze"])
        clasp = _blade_segment("Narrow_bronze_clasp_%02d" % i, points, a, b,
                               collection, clasp_mats, width_scale=1.035,
                               zbase=.467, height=.239)
        objects.append(clasp)

    # Real tiny wedge cuts at one face-edge. Their shallow, irregular ends look
    # forged/worn without turning the weapon into an eroded or misshapen rock.
    for j, (plate_index, u, s) in enumerate(((1, 2.40, -.42), (3, 4.82, .31),
                                            (4, 5.98, -.46), (6, 8.16, -.37),
                                            (7, 9.20, .33))):
        p, n, w, t = _station(points, u)
        q = p + n * w * s
        z = .46 + .235 * (.78 if s < 0 else .78) + .034 * math.sin(math.pi * u / 11)
        # A triangular chisel produces a tapered scratch in the geometry.
        direction = (t * .83 + n * .55).normalized()
        across = Vector((-direction.y, direction.x))
        p1 = q - direction * .060
        p2 = q + direction * .091
        p3 = q - direction * .027 + across * .015
        vs = [(v.x, v.y, z - .014) for v in (p1, p2, p3)]
        vs += [(v.x, v.y, z + .12) for v in (p1, p2, p3)]
        cutter = _mesh("Temporary_chisel", vs,
                       [(0, 2, 1), (3, 4, 5), (0, 1, 4, 3), (1, 2, 5, 4), (2, 0, 3, 5)],
                       collection, [])
        _apply_cut(plates[plate_index], cutter)

    # Art-directed crossing S sweep: the upper trail passes BEHIND the blade,
    # the short return turns toward the viewer below the lower cutting tip.
    # The three paths have different silhouettes and interrupted lengths;
    # they must not read as concentric neon hoops.
    paths = [
        (.047, [(-1.18, 2.04, .33), (-1.36, 1.91, .34),
                (-1.12, 1.73, .34), (-.48, 1.52, .35),
                (.28, 1.23, .35), (.84, .88, .48),
                (.79, .58, .77), (.29, .34, .795),
                (-.43, .095, .77), (-1.12, -.09, .73),
                (-1.35, .045, .70), (-1.16, .35, .60),
                (-.74, .67, .39)]),
        (.016, [(-1.24, 1.94, .29), (-.79, 2.045, .30),
                (-.04, 1.89, .31), (.64, 1.58, .31),
                (1.13, 1.18, .35), (1.22, .85, .39),
                (1.08, .59, .43)]),
        (.010, [(-1.32, .39, .40), (-1.39, .10, .48),
                (-1.17, -.16, .54), (-.67, -.20, .58),
                (.04, -.015, .62), (.64, .25, .57),
                (.95, .49, .41)]),
    ]
    for lane, (width, controls) in enumerate(paths):
        coords = _catmull_points(controls)
        radii = []
        for i in range(len(coords)):
            u = i / (len(coords) - 1)
            pulse = (.34 + .66 * math.exp(-((u - .60) / .21)**2)) if lane == 0 else 1
            radii.append(max(.015, math.sin(math.pi * u)**.70 * pulse))
        # Deliberate bright-break near the main sweep's return. Both exposed
        # ends taper separately; continuous circular wires are avoided.
        intervals = ((0, 130), (137, len(coords))) if lane == 0 else ((0, len(coords)),)
        for section, (first, last) in enumerate(intervals):
            local_radii = radii[first:last]
            for j in range(min(7, len(local_radii))):
                local_radii[j] *= (j + 1) / 7
                local_radii[-1-j] *= (j + 1) / 7
            objects.append(_tube("S_cast_trail_%02d_%02d" % (lane, section),
                                 coords[first:last], width, materials["orange"],
                                 collection, local_radii))
            if lane == 0:
                objects.append(_tube("White_hot_cast_core_%02d" % section,
                                     coords[first:last], width * .23,
                                     materials["orange_hot"], collection, local_radii))

    # Sparse crisply faceted particles; scale is subordinate to the weapon.
    debris = [(-1.30, .59, .30, .080), (-1.02, .96, .37, .052),
              (-.88, 1.39, .31, .077), (.75, 1.65, .35, .110),
              (.98, .31, .30, .089), (.04, 2.01, .32, .106),
              (-.50, .52, .33, .065), (.83, 1.18, .34, .037),
              (-1.27, 1.35, .29, .043)]
    for i, (x, y, z, size) in enumerate(debris):
        objects.append(_rock("Flying_stone_%02d" % i, (x, y, z), size,
                             collection, materials["rock"], 410 + i))
    rng = random.Random(841)
    for i in range(22):
        a = rng.uniform(-1.0, 5.0)
        x = -.045 + rng.uniform(1.16, 1.39) * math.cos(a)
        y = 1.025 + rng.uniform(.78, 1.05) * math.sin(a)
        z = rng.uniform(.29, .43)
        spark = _rock("Small_live_ember_%02d" % i, (x, y, z), rng.uniform(.005, .015),
                      collection, materials["orange_hot"], 909 + i)
        objects.append(spark)

    for ob in objects:
        ob.parent = root
    return [root] + objects
