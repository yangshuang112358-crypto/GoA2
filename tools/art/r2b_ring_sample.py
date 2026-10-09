"""Editable, isolated R2 ornament for the deep-stone skill-disc sample.

Import this module from Blender and call ``build_ring(materials)``.  Nothing is
built on import.  The caller owns the camera, lights, skill discs, render frame
range/fps and save destination.  No existing scene objects or assets are changed.

``z`` is the FRONT LIMIT of the ornament, not the centre of the spinning hoops.
The complete animated module stays behind that plane at every frame.  Skill
discs can therefore stay stationary in front of it.  All distances scale with
``radius / 4.2``; the ring remains in the XY plane with +Z toward the viewer.
"""

import math

import bpy


_TAU = math.tau


def _object(collection, name, data=None, parent=None):
    obj = bpy.data.objects.new("R2_" + name, data)
    collection.objects.link(obj)
    if parent is not None:
        obj.parent = parent
    return obj


def _arc(collection, parent, name, radius, width, start, end, z, thickness,
         material, bevel=0.005):
    """Closed radial strip; angles in radians, editable bevel left unapplied."""
    count = max(5, math.ceil(abs(end - start) * radius / 0.035))
    vertices = []
    for i in range(count + 1):
        a = start + (end - start) * i / count
        c, s = math.cos(a), math.sin(a)
        for r, h in ((radius - width / 2, z + thickness / 2),
                     (radius + width / 2, z + thickness / 2),
                     (radius - width / 2, z - thickness / 2),
                     (radius + width / 2, z - thickness / 2)):
            vertices.append((r * c, r * s, h))
    faces = []
    for i in range(count):
        a, b = i * 4, (i + 1) * 4
        faces.extend(((a, a + 1, b + 1, b),
                      (a + 2, b + 2, b + 3, a + 3),
                      (a + 1, a + 3, b + 3, b + 1),
                      (a, b, b + 2, a + 2)))
    last = count * 4
    faces.extend(((0, 2, 3, 1), (last, last + 1, last + 3, last + 2)))
    mesh = bpy.data.meshes.new("R2_" + name + "_Mesh")
    mesh.from_pydata(vertices, [], faces)
    mesh.materials.append(material)
    mesh.update()
    obj = _object(collection, name, mesh, parent)
    if bevel > 0:
        mod = obj.modifiers.new("R2_SoftMachinedEdges", "BEVEL")
        mod.width = min(bevel, width * 0.25, thickness * 0.25)
        mod.segments = 3
        mod.limit_method = "ANGLE"
    return obj


def _paths(collection, parent, name, paths, radius, material, cyclic=False):
    """Multiple deterministic strokes in one editable curve object."""
    curve = bpy.data.curves.new("R2_" + name + "_Curve", "CURVE")
    curve.dimensions = "3D"
    curve.resolution_u = 1
    curve.bevel_depth = radius
    curve.bevel_resolution = 3
    curve.use_fill_caps = True
    for points in paths:
        spline = curve.splines.new("POLY")
        spline.points.add(len(points) - 1)
        for point, xyz in zip(spline.points, points):
            point.co = (*xyz, 1)
        spline.use_cyclic_u = cyclic
    curve.materials.append(material)
    return _object(collection, name, curve, parent)


def _frame_curves(action):
    # Blender 4.4+ uses layered actions.  Older/legacy actions expose fcurves.
    seen = set()
    try:
        for curve in action.fcurves:
            pointer = curve.as_pointer()
            if pointer not in seen:
                seen.add(pointer)
                yield curve
    except (AttributeError, RuntimeError):
        pass
    for layer in getattr(action, "layers", ()):
        for strip in layer.strips:
            for bag in getattr(strip, "channelbags", ()):
                for curve in bag.fcurves:
                    pointer = curve.as_pointer()
                    if pointer not in seen:
                        seen.add(pointer)
                        yield curve


def _spin(obj, axis, turns, frame_end):
    obj.rotation_mode = "XYZ"
    obj.rotation_euler[axis] = 0
    obj.keyframe_insert(data_path="rotation_euler", index=axis, frame=0)
    obj.rotation_euler[axis] = turns * _TAU
    obj.keyframe_insert(data_path="rotation_euler", index=axis, frame=frame_end)
    action = obj.animation_data.action
    action.name = obj.name + "_Loop"
    for curve in _frame_curves(action):
        for key in curve.keyframe_points:
            key.interpolation = "LINEAR"
        cycle = curve.modifiers.new("CYCLES")
        cycle.mode_before = "REPEAT_OFFSET"
        cycle.mode_after = "REPEAT_OFFSET"
    obj.rotation_euler[axis] = 0
    obj["R2_loop_frames"] = frame_end
    obj["R2_turns_per_loop"] = turns


def _radial_xy(angle, tangent, radial, ring_radius, height):
    c, s = math.cos(angle), math.sin(angle)
    return ((ring_radius + radial) * c - tangent * s,
            (ring_radius + radial) * s + tangent * c, height)


# Deliberately repeated geometric glyphs, independent of font installations.
# Coordinates are tangent/radial and are fitted inside the inner rune lane.
_GLYPHS = (
    (((-0.055, -0.065), (0, 0.065), (0.055, -0.065)),
     ((-0.028, -0.006), (0.028, -0.006))),
    (((0, -0.068), (-0.052, 0), (0, 0.068), (0.052, 0), (0, -0.068)),
     ((0, -0.032), (0, 0.032))),
    (((-0.048, -0.055), (0.035, -0.055), (0.035, 0.055), (-0.048, 0.055)),
     ((-0.008, -0.021), (0.063, -0.021))),
    (((0, -0.071), (0, 0.071)),
     ((-0.052, 0.025), (0, -0.018), (0.052, 0.025))),
    (((-0.051, -0.06), (-0.051, 0.06), (0.045, 0.06)),
     ((-0.015, -0.027), (0.045, -0.027), (0.045, 0.019))),
)


def build_ring(materials, radius=4.2, z=-0.3, frame_end=192, fps=24):
    """Create R2_RING and return assembly objects and animation metadata.

    Required materials: bronze, dark_metal, rune (the caller supplies emission).
    A duplicate collection is rejected instead of deleting or replacing work.
    Animation is sampled at 0 and frame_end; for a video loop render frames
    0..frame_end-1, omitting the duplicate final pose.  Linear Euler curves keep
    outer clockwise / inner counterclockwise movement continuous.
    """
    if not math.isfinite(radius) or radius <= 0:
        raise ValueError("radius must be positive and finite")
    if not math.isfinite(z):
        raise ValueError("z must be finite")
    if not isinstance(frame_end, int) or frame_end <= 0 or fps <= 0:
        raise ValueError("frame_end must be a positive integer and fps positive")
    for key in ("bronze", "dark_metal", "rune"):
        if key not in materials or not isinstance(materials[key], bpy.types.Material):
            raise ValueError("materials must contain a Blender material: " + key)
    if bpy.data.collections.get("R2_RING") is not None:
        raise ValueError("R2_RING already exists; this builder never overwrites it")

    scale = radius / 4.2
    bronze, dark, rune = (materials[k] for k in ("bronze", "dark_metal", "rune"))
    collection = bpy.data.collections.new("R2_RING")
    bpy.context.scene.collection.children.link(collection)
    root = _object(collection, "Assembly")
    root.empty_display_type = "CIRCLE"
    root.empty_display_size = radius
    # Hoop extent is at most .32*scale; leave .10*scale in front of that envelope.
    # No animated part can reach the supplied front limit even at a quarter turn.
    root.location.z = z - 0.42 * scale
    root["R2_front_limit_z"] = z
    root["R2_fixed_skill_angles_degrees"] = [90, 18, -54, -126, 162]
    root["R2_role"] = "Decoration only; fixed skill discs are separate siblings"

    outer = _object(collection, "OuterOrbit", parent=root)
    inner = _object(collection, "RuneOrbit", parent=root)
    outer.empty_display_type = inner.empty_display_type = "PLAIN_AXES"
    outer.empty_display_size = inner.empty_display_size = 0.3 * scale
    collar_axes = []

    # A very fine guide is visible through the intentional gaps between bronze
    # strips, providing a coherent physical track without a solid backdrop.
    circle = [(radius * math.cos(i * _TAU / 360),
               radius * math.sin(i * _TAU / 360), -0.011 * scale)
              for i in range(360)]
    _paths(collection, outer, "FineTrack", [circle], 0.011 * scale, bronze, True)

    # The initial bays lie between stationary skills.  Ten individual sections
    # leave both five large gaps and five small expansion joints.
    engraving = []
    for bay in range(5):
        center = math.radians(54 - bay * 72)
        for half, (start_deg, end_deg) in enumerate(((-24, -1.2), (1.2, 24))):
            start, end = center + math.radians(start_deg), center + math.radians(end_deg)
            stem = "Bay%02d_%s" % (bay + 1, "A" if half == 0 else "B")
            # The dark bed is geometrically lower than the bronze lips: the
            # repeated inlaid chevrons sit inside this channel, not atop a plate.
            _arc(collection, outer, stem + "_Channel", radius, 0.144 * scale,
                 start, end, -0.031 * scale, 0.042 * scale, dark, 0.006 * scale)
            for side in (-1, 1):
                _arc(collection, outer, stem + ("_InnerLip" if side < 0 else "_OuterLip"),
                     radius + side * 0.073 * scale, 0.022 * scale,
                     start, end, 0.002 * scale, 0.044 * scale, bronze, 0.005 * scale)
            for end_index, angle in enumerate((start, end)):
                # Radial end caps make the channel visibly recessed at each join.
                cap_half = 0.013 * scale / radius
                _arc(collection, outer, stem + "_End%02d" % end_index,
                     radius, 0.158 * scale, angle - cap_half, angle + cap_half,
                     -0.001 * scale, 0.036 * scale, bronze, 0.004 * scale)
            for index in range(5):
                angle = start + (end - start) * (index + 0.5) / 5
                chevron = ((-0.035, -0.03), (0, 0.03), (0.035, -0.03))
                engraving.append([_radial_xy(angle, x * scale, y * scale,
                                            radius, -0.003 * scale)
                                  for x, y in chevron])
    _paths(collection, outer, "RecessedChevronInlays", engraving, 0.0045 * scale, bronze)

    inner_radius = radius - 0.37 * scale
    # Hairline rails and widely spaced runes leave the whole centre empty.
    for side in (-1, 1):
        inner_circle = [((inner_radius + side * 0.105 * scale) * math.cos(i * _TAU / 320),
                         (inner_radius + side * 0.105 * scale) * math.sin(i * _TAU / 320),
                         -0.043 * scale) for i in range(320)]
        _paths(collection, inner, "RuneLane" + ("Inner" if side < 0 else "Outer"),
               [inner_circle], 0.006 * scale, dark, True)
    glyph_strokes = []
    for i in range(30):
        angle = math.radians(90 + i * 12)
        for stroke in _GLYPHS[i % len(_GLYPHS)]:
            glyph_strokes.append([_radial_xy(angle, x * scale, y * scale,
                                            inner_radius, -0.033 * scale)
                                  for x, y in stroke])
    _paths(collection, inner, "GeometricRunes", glyph_strokes, 0.008 * scale, rune)

    # The hoop circle lies in local XZ.  Tilt its carrier around local X before
    # the orbit-facing Z rotation: viewed from +Z it becomes a readable ellipse,
    # while the copper track still passes through its hollow centre.  Local Y
    # spin moves the asymmetric markings inside that same fixed hoop plane.
    # Tilting cannot exceed the conservative spherical depth envelope above.
    collar_tilts = (35, 45, 55)
    for index, degrees in enumerate((54, -90, 198)):
        angle = math.radians(degrees)
        carrier = _object(collection, "Collar%02d_Carrier" % (index + 1), parent=outer)
        carrier.location = (radius * math.cos(angle), radius * math.sin(angle), 0)
        carrier.rotation_euler.x = math.radians(collar_tilts[index])
        carrier.rotation_euler.z = angle
        carrier["R2_readable_hoop_tilt_degrees"] = collar_tilts[index]
        spin = _object(collection, "Collar%02d_LocalSpin" % (index + 1), parent=carrier)
        collar_axes.append(spin)
        hoop_radius = (0.265 + index * 0.014) * scale
        hoop = [(hoop_radius * math.cos(i * _TAU / 128), 0,
                 hoop_radius * math.sin(i * _TAU / 128)) for i in range(128)]
        _paths(collection, spin, "Collar%02d_DarkCore" % (index + 1),
               [hoop], 0.024 * scale, dark, True)
        for rim_side in (-1, 1):
            rim = [(p[0], rim_side * 0.018 * scale, p[2]) for p in hoop]
            _paths(collection, spin, "Collar%02d_Rim%s" % (index + 1, "A" if rim_side < 0 else "B"),
                   [rim], 0.010 * scale, bronze, True)
        inlays = []
        for start_degrees, sweep_degrees in ((8, 55), (105, 34), (194, 76), (310, 20)):
            points = []
            for i in range(25):
                a = math.radians(start_degrees + sweep_degrees * i / 24)
                points.append(((hoop_radius + 0.024 * scale) * math.cos(a), 0,
                               (hoop_radius + 0.024 * scale) * math.sin(a)))
            inlays.append(points)
        _paths(collection, spin, "Collar%02d_AsymmetricCopperInlay" % (index + 1),
               inlays, 0.007 * scale, bronze)
        tick = [(hoop_radius * math.cos(math.radians(a)), -0.026 * scale,
                 hoop_radius * math.sin(math.radians(a))) for a in range(16, 37)]
        _paths(collection, spin, "Collar%02d_RuneIndex" % (index + 1),
               [tick], 0.006 * scale, rune)
        _spin(spin, 1, 2 if index != 1 else -2, frame_end)

    _spin(outer, 2, -1, frame_end)
    _spin(inner, 2, 1, frame_end)
    metadata = {
        "frame_start": 0,
        "frame_end": frame_end,
        "video_last_frame": frame_end - 1,
        "fps": fps,
        "duration_seconds": frame_end / fps,
        "front_limit_z": z,
        "assembly_plane_z": root.location.z,
        "conservative_mesh_max_z": root.location.z + 0.326 * scale,
        "outer_turns": -1,
        "inner_turns": 1,
        "local_axis": "Each collar's local Y after the carrier X tilt; fixed tilted hoop plane",
        "collar_tilt_degrees": list(collar_tilts),
        "fixed_skill_angles_degrees": [90, 18, -54, -126, 162],
    }
    root["R2_loop_fps"] = fps
    root["R2_loop_seconds"] = frame_end / fps
    return {
        "collection": collection,
        "root": root,
        "controls": {"outer": outer, "inner": inner, "collars": collar_axes},
        "objects": list(collection.objects),
        "animation_metadata": metadata,
    }
