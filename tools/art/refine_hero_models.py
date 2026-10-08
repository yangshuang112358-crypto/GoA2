"""Editable, original hero geometry refinement for Blender 4.5.

blender -b --factory-startup --python-exit-code 1 --python this.py -- ROOT
Optional: --only wasp --no-render / --samples 32 / --resolution 1200
Default writes a unique artifacts/hero-refinement/runs candidate only.
Use --publish to publish a validated whole batch; --replace-generated is also
required to replace an existing HeroRefinement*.blend source file.
No downloads, textures, external Python modules or old-generator execution.
Runtime FBX paths/material names/14 bone names remain stable. Rigid piece weights
are intentional: this does not claim a production soft-deforming character rig.
"""
import argparse
import hashlib
import json
import math
import os
import shutil
import sys
import uuid
from datetime import datetime, timezone
from pathlib import Path

import bpy
from mathutils import Vector


HEROES = ('wasp', 'shargatha', 'brogan', 'arien', 'tigerclaw', 'sabina')
PALETTE = {'WhiteArmor': 'E6E5DE', 'SkinYellow': 'D4A860', 'SkinGreen': '67965C',
           'Skin': 'D8AF8A', 'BrownCloth': '765035', 'Leather': '49382E',
           'TealArmor': '397D89', 'ShadowCloth': '252839', 'RedBeard': 'AA4E25',
           'Steel': '647F90', 'Bronze': 'B39250', 'Glow': '8CE6ED',
           'Shadow': '141924', 'Linen': 'C6C4B0'}
PARTS, MATERIALS = [], {}


def material_palette():
    for name, rgb in PALETTE.items():
        rgb = [int(rgb[i:i + 2], 16) / 255 for i in (0, 2, 4)]
        linear = [c / 12.92 if c <= .04045 else ((c + .055) / 1.055) ** 2.4 for c in rgb]
        m = bpy.data.materials.new(name)
        m.diffuse_color = (*linear, 1)
        m.use_nodes = True
        p = m.node_tree.nodes.get('Principled BSDF')
        p.inputs['Base Color'].default_value = m.diffuse_color
        p.inputs['Metallic'].default_value = .72 if name in ('Steel', 'Bronze', 'WhiteArmor', 'TealArmor') else .015
        p.inputs['Roughness'].default_value = .34 if name in ('Steel', 'Bronze') else .5
        if name == 'Glow':
            p.inputs['Emission Color'].default_value = (*linear, 1)
            p.inputs['Emission Strength'].default_value = .7
        MATERIALS[name] = m


def mesh(name, verts, faces, mat, bone='Spine', smooth=False, bevel=0):
    data = bpy.data.meshes.new(name)
    data.from_pydata(verts, [], faces)
    data.update()
    obj = bpy.data.objects.new(name, data)
    bpy.context.collection.objects.link(obj)
    data.materials.append(MATERIALS[mat])
    for p in data.polygons:
        p.use_smooth = smooth
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    if bevel:
        mod = obj.modifiers.new('Forged edge radius', 'BEVEL')
        mod.width, mod.segments = bevel, 2
        bpy.ops.object.modifier_apply(modifier=mod.name)
    group = obj.vertex_groups.new(name=bone)
    group.add(list(range(len(obj.data.vertices))), 1, 'REPLACE')
    obj['surface_intent'] = name
    PARTS.append(obj)
    return obj


def loft(name, sections, mat, bone='Spine', n=24, smooth=True, ridge=0, twist=0):
    """Sections (z, center x, center y, halfwidth, halfdepth), elliptical anatomy.
    Front is -Y. Optional front ridge makes forged breastplates, not cylinders.
    """
    verts, faces = [], []
    for i, (z, x, y, w, d) in enumerate(sections):
        for j in range(n):
            a = j * math.tau / n + twist
            xx, yy = math.cos(a), math.sin(a)
            verts.append((x + w * xx, y + d * yy - ridge * max(0, -yy) ** 6, z))
    for i in range(len(sections) - 1):
        for j in range(n):
            a, b = i * n + j, i * n + (j + 1) % n
            faces.append((a, b, b + n, a + n))
    faces += [tuple(reversed(range(n))), tuple(range((len(sections) - 1) * n, len(sections) * n))]
    return mesh(name, verts, faces, mat, bone, smooth)


def path_tube(name, points, radii, mat, bone='Spine', n=10, flatten=1, smooth=True):
    verts, faces = [], []
    for i, p in enumerate(points):
        direction = (Vector(points[min(i + 1, len(points) - 1)]) - Vector(points[max(0, i - 1)])).normalized()
        ref = Vector((0, 1, 0)) if abs(direction.y) < .9 else Vector((1, 0, 0))
        u = direction.cross(ref).normalized()
        v = direction.cross(u).normalized()
        for j in range(n):
            a = math.tau * j / n
            verts.append(Vector(p) + radii[i] * (u * math.cos(a) + v * math.sin(a) * flatten))
    for i in range(len(points) - 1):
        for j in range(n):
            a, b = i * n + j, i * n + (j + 1) % n
            faces.append((a, b, b + n, a + n))
    faces += [tuple(reversed(range(n))), tuple(range((len(points) - 1) * n, len(points) * n))]
    return mesh(name, verts, faces, mat, bone, smooth)


def smooth_chain(points, radii, steps=8):
    """Centrally continuous authored path; shared by flesh and fitted plates."""
    result, sizes = [], []
    for i in range(len(points) - 1):
        p0, p1, p2, p3 = [Vector(points[max(0, min(len(points) - 1, k))]) for k in (i - 1, i, i + 1, i + 2)]
        for j in range(steps):
            t = j / steps
            p = .5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t +
                       (-p0 + 3 * p1 - 3 * p2 + p3) * t ** 3)
            result.append(tuple(p))
            sizes.append(radii[i] * (1 - t) + radii[i + 1] * t)
    result.append(points[-1])
    sizes.append(radii[-1])
    return result, sizes


def thicken(obj, thickness=.010, bevel=0):
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    mod = obj.modifiers.new('Physical shell thickness', 'SOLIDIFY')
    mod.thickness = thickness
    mod.offset = 0
    bpy.ops.object.modifier_apply(modifier=mod.name)
    if bevel:
        mod = obj.modifiers.new('Rolled shell edge', 'BEVEL')
        mod.width, mod.segments = bevel, 2
        mod.limit_method = 'ANGLE'
        bpy.ops.object.modifier_apply(modifier=mod.name)
    return obj


def panel(name, outline, depth, mat, bone='Spine', bevel=.007):
    """Closed panel with spatially folded front outline, offset back."""
    n = len(outline)
    verts = list(outline) + [(x, y + depth, z) for x, y, z in outline]
    faces = [tuple(reversed(range(n))), tuple(range(n, n * 2))]
    faces += [(i, (i + 1) % n, (i + 1) % n + n, i + n) for i in range(n)]
    return mesh(name, verts, faces, mat, bone, False, bevel)


def ellipsoid(name, center, scale, mat, bone='Head', n=24, rings=12):
    x, y, z = center
    sx, sy, sz = scale
    sections = []
    for i in range(rings + 1):
        a = -.5 * math.pi + (i + .015) / (rings + .03) * math.pi
        sections.append((z + sz * math.sin(a), x, y, sx * math.cos(a), sy * math.cos(a)))
    return loft(name, sections, mat, bone, n)


def trim(name, path, mat='Bronze', r=.008, bone='Spine'):
    return path_tube(name, path, [r] * len(path), mat, bone, 8)


def face(skin, broad=1, masked=False, feminine=False):
    # Ring progression differentiates chin, cheekbone, temple and cranium.
    jaw = .85 if feminine else 1
    sections = [(1.355, 0, .022, .087, .079), (1.393, 0, .024, .077, .070),
        (1.437, 0, .024, .065, .061), (1.478, 0, .024, .061, .060),
        (1.509, 0, .014, .064 * broad, .069),
        (1.533, 0, -.005, .079 * broad * jaw, .087), (1.56, 0, -.003, .110 * broad * jaw, .100),
        (1.605, 0, .001, .140 * broad * (.94 if feminine else 1), .114),
        (1.648, 0, .005, .148 * broad, .126),
        (1.69, 0, .012, .146 * broad, .131), (1.75, 0, .014, .140 * broad, .119),
        (1.81, 0, .020, .119 * broad, .100), (1.846, 0, .023, .073 * broad, .063),
        (1.86, 0, .024, .012, .01)]
    loft('Continuous neck jaw cheek and cranium', sections, skin, 'Head', 40)
    if masked:
        return
    def surface(x, z, outset=0):
        """Anchor facial structures to the actual authored cheek, not guesses."""
        for a, b in zip(sections, sections[1:]):
            if a[0] <= z <= b[0]:
                t = (z - a[0]) / (b[0] - a[0])
                y, w, d = [a[k] * (1 - t) + b[k] * t for k in (2, 3, 4)]
                return y - d * math.sqrt(max(.03, 1 - (x / w) ** 2)) - outset
        raise ValueError('Face feature outside face sections')
    # Angular bridge/tip, nostrils and short cupid-bow keep face human at close view.
    nose_width = .018 if feminine else .021
    panel('Nasal bridge and tip', [(-nose_width * .6, -.113, 1.691), (nose_width * .6, -.113, 1.691),
        (nose_width, -.144, 1.615), (nose_width * .46, -.157, 1.607),
        (-nose_width * .46, -.157, 1.607), (-nose_width, -.144, 1.615)], .026, skin, 'Head', .008)
    # Two broad lip patches follow the cheek, with actual cupid-bow silhouette.
    # The seam is embedded between them, not hidden behind the front face shell.
    for upper in (True, False):
        vs, fs = [], []
        for row in range(5):
            t = row / 4
            for j in range(17):
                u = (j / 16 - .5) * 2
                x = u * .036
                seam_z = 1.581 + .0025 * math.cos(u * math.pi)
                edge = (.0075 + .003 * math.cos(u * math.tau)) if upper else -.011
                z = seam_z + edge * (1 - u * u) * t
                bulge = .004 + .006 * math.sin(t * math.pi) * (1 - u * u)
                vs.append((x, surface(x, z, bulge), z))
        for row in range(4):
            for j in range(16):
                a = row * 17 + j
                fs.append((a, a + 1, a + 18, a + 17))
        mesh('Sculpted upper lip' if upper else 'Sculpted lower lip', vs, fs, skin, 'Head', True)
    trim('Mouth separation', [(x, surface(x, 1.581, .0045), 1.581 + .0025 * math.cos(x / .036 * math.pi))
        for x in [-.033 + j / 12 * .066 for j in range(13)]], 'Leather', .0014, 'Head')
    for s in (-1, 1):
        # Eye sockets, whites, iris and separate eyelid, not black spheres on a ball.
        x = s * .059 * broad
        ellipsoid('Recessed eye white', (x, -.108, 1.676), (.022, .010, .010), 'Linen', n=20, rings=8)
        ellipsoid('Small inset iris', (x, -.1175, 1.676), (.0074, .0020, .0077), 'Shadow', n=16, rings=8)
        trim('Integrated upper eyelid', [(x - .024, -.104, 1.676), (x, -.118, 1.686),
            (x + .024, -.104, 1.676)], skin, .0055, 'Head')
        trim('Integrated lower eyelid', [(x - .023, -.104, 1.675), (x, -.116, 1.668),
            (x + .023, -.104, 1.675)], skin, .0030, 'Head')
        trim('Fine expression brow', [(x - .026, -.102, 1.700), (x, -.112, 1.708),
            (x + .028, -.101, 1.700)], 'Leather', .0040 if feminine else .0054, 'Head')
        ellipsoid('Ear cartilage', (s * .149 * broad, .013, 1.649), (.018, .023, .044), skin, n=20, rings=10)


def hand(side, s, wide, skin):
    x = s * .425 * wide
    bone = 'Hand.' + side
    loft('Palm ' + side, [(.745, x, -.145, .037, .031), (.765, x, -.140, .047, .038),
        (.815, x, -.132, .045, .036), (.852, x - s * .01, -.114, .029, .028)], skin, bone, 16)
    # Four separate curled fingers surround a Y-axis grip, plus opposed thumb.
    for i in range(4):
        z = .754 + i * .019
        path_tube('Curled finger ' + side + str(i), [(x + s * .026, -.139, z),
            (x + s * .045, -.170, z), (x + s * .028, -.197, z),
            (x - s * .004, -.189, z)], [.011, .011, .010, .008], skin, bone, 8)
    path_tube('Opposing thumb ' + side, [(x - s * .028, -.135, .813),
        (x - s * .046, -.162, .799), (x - s * .033, -.185, .778)], [.018, .016, .011], skin, bone, 10)


def body(skin, suit, wide=1, legs=True, female=False, armor=False):
    # Body and fitted garment use real section changes rather than a stack of balls.
    loft('Fitted torso silhouette', [(.83, 0, .0, .183 * wide, .126),
        (.90, 0, .0, .173 * wide, .122), (1.01, 0, .012, .153 * wide, .107),
        (1.12, 0, .009, .180 * wide, .143 if female else .134),
        (1.22, 0, .008, .217 * wide, .155), (1.31, 0, .012, .235 * wide, .145),
        (1.36, 0, .020, .203 * wide, .120), (1.40, 0, .011, .098, .081)],
        suit, 'Spine', 24 if not armor else 16, not armor, ridge=.021 if armor else 0)
    face(skin, 1.13 if wide > 1.2 else 1, masked=suit == 'ShadowCloth', feminine=female)
    if legs:
        for side, s in (('L', -1), ('R', 1)):
            x = s * .145 * wide
            exposed_leg = female and suit == 'BrownCloth'
            thigh_sections = ([(.636, s * .140 * wide, .015, .093 * wide, .103)] if exposed_leg else
                [(.45, x + s * .016, 0, .081 * wide, .080), (.52, x + s * .013, .010, .085 * wide, .092)])
            thigh_sections += [(.70, x, .015, .097 * wide, .107),
                (.79, s * .116 * wide, .015, .098 * wide, .102),
                (.89, s * .095 * wide, .015, .086 * wide, .096)]
            loft('Shaped thigh ' + side, thigh_sections,
                skin if female and suit == 'BrownCloth' else suit, 'UpperLeg.' + side, 20)
            lower_mat = 'ShadowCloth' if female and suit == 'BrownCloth' else 'Leather'
            loft('Anatomical calf ' + side, [(.11, x + s * .016, 0, .061, .060),
                (.20, x + s * .016, .025, .060, .058), (.32, x + s * .014, .037, .083, .088),
                (.43, x + s * .015, .012, .082, .086), (.51, x + s * .016, 0, .077, .078)],
                lower_mat, 'LowerLeg.' + side, 20)
            if female and suit == 'BrownCloth':
                loft('Continuous thigh stocking ' + side, [(.429, x + s * .014, .010, .086, .091),
                    (.51, x + s * .014, .009, .088, .094),
                    (.61, x + s * .004, .013, .097, .108),
                    (.681, x, .015, .102, .113), (.689, x, .015, .103, .114)], lower_mat, 'UpperLeg.' + side, 24)
            # Foot ends exactly at Z=0; coherent toe/instep/heel profile.
            loft('Lasted boot ' + side, [(0, x + s * .016, -.064, .086, .164),
                (.033, x + s * .016, -.064, .088, .166), (.075, x + s * .016, -.072, .081, .155),
                (.115, x + s * .016, -.025, .073, .102), (.16, x + s * .016, .003, .061, .068)], 'Leather', 'LowerLeg.' + side, 24)
            trim('Boot welt ' + side, [(x - .075, -.191, .039), (x, -.230, .039), (x + .075, -.191, .039)], 'Bronze', .004, 'LowerLeg.' + side)
    for side, s in (('L', -1), ('R', 1)):
        loft('Deltoid and upper arm ' + side, [(1.06, s * .350 * wide, -.022, .067 * wide, .071),
            (1.13, s * .332 * wide, -.005, .080 * wide, .082),
            (1.25, s * .293 * wide, .014, .091 * wide, .095),
            (1.33, s * .246 * wide, .015, .090 * wide, .091),
            (1.363, s * .231 * wide, .015, .065 * wide, .068),
            (1.376, s * .211 * wide, .015, .029 * wide, .031)], suit, 'UpperArm.' + side, 24)
        loft('Forearm muscles ' + side, [(.844, s * .416 * wide, -.119, .034 * wide, .035),
            (.918, s * .400 * wide, -.091, .048 * wide, .049),
            (1.01, s * .366 * wide, -.044, .065 * wide, .059),
            (1.075, s * .350 * wide, -.022, .062 * wide, .066)], skin, 'LowerArm.' + side, 20)
        hand(side, s, wide, 'Leather' if suit == 'ShadowCloth' else skin)
    loft('Continuous waist belt', [(.858, 0, 0, .189 * wide, .135), (.910, 0, 0, .183 * wide, .133)], 'Leather', 'Hips', 32)
    panel('Belt buckle frame', [(-.040, -.149, .870), (.040, -.149, .870), (.040, -.149, .909), (-.040, -.149, .909)], .021, 'Bronze', 'Hips', .004)


def shoulder(side, s, width, mat='WhiteArmor'):
    bone = 'UpperArm.' + side
    for i in range(3):
        z = 1.36 - i * .053
        x = s * (width + i * .030)
        panel('Overlapping shoulder lame ' + side + str(i), [(x - .117, -.095, z),
            (x - .088, -.165, z - .022), (x + .088, -.165, z - .022),
            (x + .117, -.095, z), (x + .09, .104, z + .025), (x - .09, .104, z + .025)], .018, mat, bone)
        trim('Shoulder engraved edge', [(x - .088, -.174, z - .018), (x, -.180, z - .025), (x + .088, -.174, z - .018)], 'Bronze', .006, bone)


def segmented_plate_armor(mat, wide=1):
    # A single fitted cuirass carries a subtle central keel. Curvature follows
    # the torso rather than two flat rectangles floating above the rib cage.
    vertices, faces = [], []
    columns, rows = 24, 8
    for row in range(rows):
        t = row / (rows - 1)
        width = (.188 + .042 * t) * wide
        depth = .149 + .010 * math.sin(t * math.pi)
        for j in range(columns + 1):
            a = (j / columns - .5) * 2 * 1.24
            x = width * math.sin(a)
            y = .007 - depth * math.cos(a) - .015 - .008 * max(0, math.cos(a)) ** 6
            z = 1.135 + .186 * t + (.012 * abs(math.sin(a))) * (1 - t)
            vertices.append((x, y, z))
    for row in range(rows - 1):
        for j in range(columns):
            a = row * (columns + 1) + j
            faces.append((a, a + 1, a + columns + 2, a + columns + 1))
    thicken(mesh('Continuous curved breastplate', vertices, faces, mat, smooth=True), .015, .003)
    trim('Breastplate lower rolled edge', vertices[:columns + 1], 'Bronze', .005)
    for edge in (0, columns):
        trim('Breastplate side rolled edge', [vertices[row * (columns + 1) + edge] for row in range(rows)], 'Bronze', .0045)
    for i in range(3):
        z = 1.102 - i * .052
        verts, fs = [], []
        for row in range(3):
            t = row / 2
            for j in range(21):
                a = (j / 20 - .5) * 2 * 1.19
                verts.append((.177 * wide * math.sin(a), -.007 - .129 * math.cos(a) - .017,
                              z - .056 * t - .010 * math.cos(a)))
        for row in range(2):
            for j in range(20):
                a = row * 21 + j
                fs.append((a, a + 1, a + 22, a + 21))
        thicken(mesh('Curved overlapping abdominal lame', verts, fs, mat, smooth=True), .014, .002)


def cloak(mat, length=.45, width=.27, bone='Spine', split=False):
    verts, faces = [], []
    rows, columns = 10, 16
    for i in range(rows):
        t = i / (rows - 1)
        for j in range(columns + 1):
            a = (j / columns - .5) * 2
            x = a * (width + t * .10)
            y = .143 + t * .068 + .030 * math.cos(a * math.pi * 3.5) * (t + .3)
            z = 1.38 - t * (1.38 - length) + .036 * abs(a) * t
            if split and abs(a) < .16 and t > .78:
                z += (t - .78) * .7
            verts.append((x, y, z))
    for i in range(rows - 1):
        for j in range(columns):
            a = i * (columns + 1) + j
            faces.append((a, a + 1, a + columns + 2, a + columns + 1))
    obj = mesh('Draped pleated cloak', verts, faces, mat, bone, True)
    mod = obj.modifiers.new('Cloth thickness', 'SOLIDIFY')
    mod.thickness = .008
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.modifier_apply(modifier=mod.name)
    trim('Weighted cloak hem', [verts[(rows - 1) * (columns + 1) + j] for j in range(columns + 1)], 'Bronze' if mat != 'ShadowCloth' else 'Leather', .008, bone)


def blade(x, short=False, mat='Steel', wide=1):
    bone = 'Hand.R' if x > 0 else 'Hand.L'
    y = -.178
    trim('Wrapped leather grip', [(x, y, .70), (x, y, .91)], 'Leather', .033, bone)
    for i in range(6):
        z = .724 + i * .028
        trim('Grip winding', [(x - .031, y, z), (x, y - .034, z + .011), (x + .031, y, z + .018)], 'Bronze', .004, bone)
    trim('Swept cross guard', [(x - .140, y, .918), (x - .093, y - .015, .956), (x, y, .944),
        (x + .093, y - .015, .956), (x + .140, y, .918)], 'Bronze', .017, bone)
    top = 1.27 if short else 1.69
    # Diamond cross-section gives light-bearing spine and sharp cutting edges.
    verts = [(x - .063 * wide, y, .969), (x, y - .025, .969), (x + .063 * wide, y, .969), (x, y + .025, .969),
             (x - .046 * wide, y, top - .16), (x, y - .019, top - .16), (x + .046 * wide, y, top - .16), (x, y + .019, top - .16), (x, y, top)]
    mesh('Diamond section blade', verts, [(0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7),
        (4, 5, 8), (5, 6, 8), (6, 7, 8), (7, 4, 8), (3, 2, 1, 0)], mat, bone)
    if not short:
        trim('Blade engraved inlay', [(x, y - .028, 1.04), (x - .016, y - .024, 1.22),
            (x + .013, y - .023, 1.40), (x, y - .021, 1.50)], 'Glow' if mat == 'TealArmor' else 'Bronze', .004, bone)


def hair_lock(name, path, color, width=.025, bone='Head'):
    return path_tube(name, path, [width, width * .9, width * .7, .004][:len(path)], color, bone, 10, .6)


def swept_hair_cap(color='Leather'):
    """Continuous scalp + broad swept clumps, deliberately no parallel tubes."""
    vertices, faces = [], []
    n, rows = 48, 12
    for i in range(rows):
        t = i / (rows - 1)
        for j in range(n):
            a = j / n * math.tau
            front = max(0, -math.sin(a))
            back = max(0, math.sin(a))
            hairline = 1.735 + .070 * front - .115 * back
            # Asymmetrical flowing crown rather than a flat mohawk silhouette.
            radius = math.cos(t * math.pi / 2) * (.99 + .02 * math.sin(a * 3))
            x = .150 * math.cos(a) * radius + .032 * math.sin(t * math.pi / 2)
            y = .016 + .140 * math.sin(a) * radius + .024 * t
            z = hairline + (1.903 - hairline) * math.sin(t * math.pi / 2)
            vertices.append((x, y, z))
    for i in range(rows - 1):
        for j in range(n):
            a, b = i * n + j, i * n + (j + 1) % n
            faces.append((a, b, b + n, a + n))
    cap = mesh('Continuous swept hair crown', vertices, faces, color, 'Head', True)
    mod = cap.modifiers.new('Hair crown volume', 'SOLIDIFY')
    mod.thickness = .010
    bpy.context.view_layer.objects.active = cap
    bpy.ops.object.modifier_apply(modifier=mod.name)
    # Five broad, overlapping convex clumps share the crown flow. Each is a
    # surface, not a cylinder, with an irregular tapered tip behind the ear.
    for index in range(5):
        offset = (index - 2) * .049
        verts, fs = [], []
        length, across = 13, 6
        for i in range(length):
            t = i / (length - 1)
            width = .047 * (1 - .82 * t ** 3)
            center_x = offset + .040 * math.sin(t * math.pi) - .020 * t
            center_y = -.100 + .292 * t
            center_z = 1.796 + .127 * math.sin(t * math.pi) - .068 * t
            # Outer locks hug side planes and stay lower than the crown crest.
            center_z -= abs(index - 2) * .017
            for j in range(across + 1):
                u = (j / across - .5) * 2
                verts.append((center_x + width * u, center_y,
                    center_z + .014 * (1 - u * u)))
        for i in range(length - 1):
            for j in range(across):
                a = i * (across + 1) + j
                fs.append((a, a + 1, a + across + 2, a + across + 1))
        obj = mesh('Broad swept hair plane ' + str(index), verts, fs, color, 'Head', True)
        mod = obj.modifiers.new('Tapered hair sheet thickness', 'SOLIDIFY')
        mod.thickness = .012
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.modifier_apply(modifier=mod.name)


def wasp_pauldron(side, s, mat='WhiteArmor', wide=1):
    bone = 'UpperArm.' + side
    # Two cupped forged shells wrap the actual deltoid; no horizontal paper fins.
    loft('Upper cupped pauldron', [(1.290, s * .291 * wide, .005, .105 * wide, .111),
        (1.314, s * .279 * wide, .005, .139 * wide, .143), (1.375, s * .259 * wide, .013, .130 * wide, .143),
        (1.419, s * .246 * wide, .018, .085 * wide, .111), (1.431, s * .241 * wide, .019, .038 * wide, .067)],
        mat, bone, 24, True)
    loft('Offset lower shoulder lame', [(1.223, s * .325 * wide, -.003, .071 * wide, .095),
        (1.245, s * .317 * wide, -.003, .101 * wide, .120), (1.285, s * .304 * wide, .002, .113 * wide, .126),
        (1.306, s * .290 * wide, .008, .101 * wide, .116)], mat, bone, 24, True)
    for x, y, z, width, depth in [(s * .279 * wide, .005, 1.314, .140 * wide, .144),
                                (s * .317 * wide, -.003, 1.245, .102 * wide, .121)]:
        points = [(x + width * math.cos(a), y + depth * math.sin(a), z - .008 * math.sin(a) ** 2)
            for a in [math.pi + i / 18 * math.pi for i in range(19)]]
        trim('Cupped shoulder rolled gold rim', points, 'Bronze', .0065, bone)


def fitted_greave(side, s):
    x = s * .161
    vertices, faces = [], []
    rows = [(.142, .061, .056, .001), (.206, .067, .066, .018),
            (.310, .080, .089, .030), (.409, .087, .091, .010),
            (.460, .093, .084, -.002), (.499, .081, .079, -.003),
            (.520, .048, .063, -.002)]
    n = 20
    for i, (z, width, depth, cy) in enumerate(rows):
        for j in range(n + 1):
            a = (j / n - .5) * 2 * 1.23
            # Rounded poleyn follows the front of the knee instead of a flat sign.
            extra = .012 * math.exp(-((z - .47) / .064) ** 2) * math.cos(a) ** 2
            vertices.append((x + width * math.sin(a), cy - depth * math.cos(a) - .016 - extra,
                             z - .012 * abs(math.sin(a)) * (1 if i > 3 else 0)))
    for row in range(len(rows) - 1):
        for j in range(n):
            a = row * (n + 1) + j
            faces.append((a, a + 1, a + n + 2, a + n + 1))
    bone = 'LowerLeg.' + side
    thicken(mesh('Fitted shin and rounded poleyn', vertices, faces, 'WhiteArmor', bone, True), .012, .003)
    trim('Poleyn rolled edge', vertices[-n - 1:], 'Bronze', .004, bone)


def build_wasp():
    body('SkinYellow', 'WhiteArmor', female=True, armor=True)
    segmented_plate_armor('WhiteArmor')
    for side, s in (('L', -1), ('R', 1)):
        wasp_pauldron(side, s)
        panel('Fitted wrist vambrace', [(s * .371, -.091, 1.04), (s * .439, -.133, .872),
            (s * .397, -.171, .868), (s * .331, -.129, 1.02)], .046, 'WhiteArmor', 'LowerArm.' + side)
        fitted_greave(side, s)
        trim('Cyan armor conduit', [(s * .040, -.174, 1.28), (s * .112, -.158, 1.235),
            (s * .034, -.168, 1.175)], 'Glow', .0045)
    # High fitted gorget blends neck into the chest instead of exposing a peg.
    loft('Fitted valkyrie gorget', [(1.348, 0, .010, .163, .116),
        (1.378, 0, .009, .125, .103), (1.420, 0, .010, .081, .079),
        (1.455, 0, .010, .078, .077)], 'WhiteArmor', 'Spine', 32, True)
    trim('Gorget rolled edge', [(math.cos(a) * .079, .010 + math.sin(a) * .078, 1.454)
        for a in [i / 32 * math.tau for i in range(33)]], 'Bronze', .005)
    swept_hair_cap()
    trim('White diadem', [(-.133, -.098, 1.774), (-.085, -.145, 1.805), (0, -.146, 1.771),
        (.085, -.145, 1.805), (.133, -.098, 1.774)], 'WhiteArmor', .015, 'Head')
    blade(.425, mat='Steel')
    # Static channels intentionally accompany, not replace, runtime electricity.


def build_shargatha():
    body('SkinGreen', 'SkinGreen', legs=False, female=True)
    segmented_plate_armor('TealArmor')
    points = [(0, 0, .86), (0, .005, .65), (.04, .053, .43), (.21, .11, .25),
        (.46, .055, .19), (.66, -.135, .145), (.60, -.37, .118), (.32, -.51, .085),
        (-.02, -.50, .06), (-.32, -.35, .041), (-.59, -.12, .022), (-.72, .10, .005)]
    radii = [.177, .217, .223, .221, .196, .167, .140, .113, .079, .047, .027, .004]
    smooth_points, smooth_radii = smooth_chain(points, radii, 10)
    path_tube('Continuous tapered serpent body', smooth_points, smooth_radii, 'SkinGreen', 'Hips', 32)
    def belly_point(index, a, outset=.010):
        index = max(0, min(len(smooth_points) - 2, index))
        i, t = int(index), index - int(index)
        center = Vector(smooth_points[i]).lerp(Vector(smooth_points[i + 1]), t)
        tangent = (Vector(smooth_points[min(len(smooth_points) - 1, i + 1)]) -
                   Vector(smooth_points[max(0, i - 1)])).normalized()
        right = tangent.cross(Vector((0, 1, 0))).normalized()
        front = tangent.cross(right).normalized()
        radius = smooth_radii[i] * (1 - t) + smooth_radii[i + 1] * t + outset
        return tuple(center + (right * math.sin(a) + front * math.cos(a)) * radius)
    # Every ventral plate uses the same curve and frame as the skin underneath.
    # Plates occupy a curved band, with a scalloped lower edge and real thickness.
    for i in range(13):
        vertices, faces = [], []
        for row in range(4):
            t = row / 3
            for j in range(17):
                u = (j / 16 - .5) * 2
                path_index = .35 + i * 1.34 + t * 1.42 + .14 * (1 - u * u) * t
                vertices.append(belly_point(path_index, u * .86, .012 + .003 * math.sin(t * math.pi)))
        for row in range(3):
            for j in range(16):
                a = row * 17 + j
                faces.append((a, a + 1, a + 18, a + 17))
        thicken(mesh('Fitted overlapping serpent ventral plate', vertices, faces, 'TealArmor', 'Hips', True), .008, .0015)
    # Three primary flowing crown bundles and two low side tendrils. The uneven
    # S-curves read as a deliberate reptilian crest, not parallel upright rods.
    crowns = [([(0, .0, 1.817), (-.031, .00, 1.951), (.018, .098, 2.058), (.047, .173, 2.013)], [.039, .044, .026, .003]),
        ([(-.079, .015, 1.791), (-.133, -.009, 1.944), (-.187, .071, 2.028), (-.164, .161, 1.946)], [.036, .041, .024, .003]),
        ([(.079, .015, 1.791), (.135, -.012, 1.931), (.190, .080, 2.010), (.161, .162, 1.927)], [.036, .041, .024, .003])]
    for index, (path, rr) in enumerate(crowns):
        pp, rr = smooth_chain(path, rr, 7)
        path_tube('Flowing primary crown bundle ' + str(index), pp, rr, 'SkinGreen', 'Head', 16, .68)
    for s in (-1, 1):
        pp, rr = smooth_chain([(s * .116, .015, 1.756), (s * .188, .057, 1.889),
            (s * .230, .151, 1.920), (s * .217, .210, 1.775)], [.029, .034, .020, .002], 7)
        path_tube('Swept side crown bundle', pp, rr, 'SkinGreen', 'Head', 14, .58)
    trim('Gold brow crest', [(-.146, -.090, 1.75), (-.09, -.136, 1.79), (0, -.144, 1.805),
        (.09, -.136, 1.79), (.146, -.090, 1.75)], 'Bronze', .017, 'Head')
    blade(.425, short=True)


def build_brogan():
    body('Skin', 'Steel', wide=1.38, armor=True)
    segmented_plate_armor('Steel', 1.38)
    cloak('BrownCloth', .30, .30, split=True)
    for side, s in (('L', -1), ('R', 1)):
        wasp_pauldron(side, s, 'Steel', 1.38)
    # Broad red moustache, substantial braided beard with actual strand rhythm.
    for s in (-1, 1):
        path_tube('Swept red moustache', [(0, -.154, 1.585), (s * .070, -.157, 1.577),
            (s * .135, -.121, 1.541)], [.018, .035, .010], 'RedBeard', 'Head', 12, .65)
    for i in range(9):
        x = (i - 4) * .030
        bottom = 1.245 + abs(i - 4) * .023
        for strand in range(2):
            pts = [(x + .008 * math.sin(j * 2 + strand * math.pi), -.121 - .040 * math.sin(j / 7 * math.pi),
                1.561 - j / 7 * (1.561 - bottom)) for j in range(8)]
            path_tube('Braided red beard strand', pts, [.020 - j * .0014 for j in range(8)], 'RedBeard', 'Head', 8)
        trim('Beard metal clasp', [(x - .015, -.151, bottom + .037), (x, -.169, bottom + .037),
            (x + .015, -.151, bottom + .037)], 'Bronze', .010, 'Head')
    loft('Riveted Viking helmet', [(1.748, 0, .014, .191, .156), (1.80, 0, .014, .189, .153),
        (1.864, 0, .018, .153, .125), (1.908, 0, .020, .072, .065), (1.914, 0, .021, .012, .012)], 'Steel', 'Head', 24, False)
    trim('Helmet nasal reinforcement', [(0, -.159, 1.838), (0, -.163, 1.761), (0, -.157, 1.68)], 'Bronze', .022, 'Head')
    # Weapon grip sits inside curled right hand; cross-section broadens into edge.
    x, y = .425 * 1.38, -.178
    trim('Ash axe haft', [(x, y, .50), (x, y, 1.52)], 'BrownCloth', .029, 'Hand.R')
    # Eye and spine are thick; the cutting edge is honed to a 3mm bevel instead
    # of extruding the entire silhouette as a rectangular metal block.
    profile = [(x - .035, 1.478, .032), (x + .136, 1.573, .032),
        (x + .265, 1.588, .009), (x + .320, 1.546, .002),
        (x + .338, 1.457, .002), (x + .334, 1.350, .002),
        (x + .290, 1.254, .002), (x + .222, 1.214, .006),
        (x + .169, 1.285, .019), (x + .100, 1.334, .030), (x - .035, 1.333, .032)]
    count = len(profile)
    verts = [(px, y - thickness, z) for px, z, thickness in profile] + [(px, y + thickness, z) for px, z, thickness in profile]
    fs = [tuple(reversed(range(count))), tuple(range(count, count * 2))]
    fs += [(i, (i + 1) % count, (i + 1) % count + count, i + count) for i in range(count)]
    mesh('Bearded axe with honed cutting bevel', verts, fs, 'Steel', 'Hand.R', False, .002)
    trim('Axe cutting edge', [(px, y - thickness - .001, z) for px, z, thickness in profile[2:8]], 'Linen', .0025, 'Hand.R')
    # Convex segmented wood shield, circular iron rim and ornamental boss.
    cx, cy, cz, radius = -.425 * 1.38, -.243, .94, .327
    n = 48
    verts = [(cx, cy - .068, cz)] + [(cx + radius * math.cos(j * math.tau / n), cy,
        cz + radius * math.sin(j * math.tau / n)) for j in range(n)]
    mesh('Convex plank shield face', verts, [(0, j + 1, (j + 1) % n + 1) for j in range(n)], 'BrownCloth', 'Hand.L')
    ring = [(cx + radius * math.cos(j * math.tau / n), cy - .014,
        cz + radius * math.sin(j * math.tau / n)) for j in range(n + 1)]
    trim('Forged round shield rim', ring, 'Steel', .020, 'Hand.L')
    for dx in (-.22, -.11, 0, .11, .22):
        extent = math.sqrt(radius ** 2 - dx ** 2) - .02
        trim('Shield plank seam', [(cx + dx, cy - .066, cz - extent), (cx + dx, cy - .068, cz + extent)], 'Leather', .004, 'Hand.L')
    ellipsoid('Shield boss', (cx, cy - .072, cz), (.079, .052, .079), 'Steel', 'Hand.L', 24, 12)
    for j in range(12):
        a = j * math.tau / 12
        ellipsoid('Shield rivet', (cx + .287 * math.cos(a), cy - .024, cz + .287 * math.sin(a)), (.009, .007, .009), 'Bronze', 'Hand.L', 8, 4)


def build_arien():
    body('Skin', 'TealArmor')
    cloak('TealArmor', .29, .20, split=True)
    for s in (-1, 1):
        panel('Folded ocean coat lapel', [(s * .196, -.112, 1.36), (s * .080, -.175, 1.302),
            (s * .045, -.153, 1.028), (s * .136, -.150, 1.24)], .010, 'Linen')
        trim('Lapel gold piping', [(s * .196, -.12, 1.36), (s * .080, -.181, 1.302), (s * .045, -.160, 1.028)], 'Bronze', .007)
        panel('Split sweeping coat skirt', [(s * .040, -.099, .87), (s * .170, -.082, .90),
            (s * .265, -.020, .31), (s * .074, -.032, .36)], .009, 'TealArmor', 'Hips')
        for j in range(4):
            z = .98 + j * .07
            ellipsoid('Coat fastener', (s * .042, -.139, z), (.010, .008, .010), 'Bronze', 'Spine', 8, 4)
    panel('Raised asymmetrical duellist collar', [(-.18, -.066, 1.365), (-.127, -.054, 1.50),
        (-.078, -.034, 1.49), (-.067, -.096, 1.35)], .030, 'TealArmor')
    swept_hair_cap('TealArmor')
    blade(.425, mat='TealArmor', wide=.75)
    # Runtime water remains a separate effect; boots do not include an opaque ring.


def build_tigerclaw():
    body('Shadow', 'ShadowCloth')
    cloak('ShadowCloth', .32, .255, split=True)
    # Open hood shell with actual aperture, pointed shoulder cowl, no cat anatomy.
    verts, faces = [], []
    rings, n = 11, 32
    for i in range(rings):
        t = i / (rings - 1)
        # Front rim is vertically elliptical; sweeps to the small back cap.
        scale = max(.015, math.cos(t * math.pi / 2))
        for j in range(n):
            a = math.tau * j / n
            verts.append((.203 * math.cos(a) * scale, -.148 + .369 * math.sin(t * math.pi / 2),
                          1.703 + .250 * math.sin(a) * scale + .009 * t))
    for i in range(rings - 1):
        for j in range(n):
            a, b = i * n + j, i * n + (j + 1) % n
            faces.append((a, b, b + n, a + n))
    faces.append(tuple(range((rings - 1) * n, rings * n)))
    mesh('Open tailored shadow hood', verts, faces, 'ShadowCloth', 'Head', True)
    trim('Stitched hood aperture', [verts[j] for j in range(n)] + [verts[0]], 'Leather', .011, 'Head')
    panel('Face wrap', [(-.124, -.149, 1.639), (0, -.179, 1.652), (.124, -.149, 1.639),
        (.094, -.131, 1.493), (0, -.130, 1.460), (-.094, -.131, 1.493)], .012, 'Shadow', 'Head')
    for s in (-1, 1):
        trim('Narrow shadow eye', [(s * .029, -.151, 1.680), (s * .062, -.156, 1.688),
            (s * .096, -.140, 1.682)], 'Glow', .005, 'Head')
        panel('Layered shoulder leather', [(s * .164, -.100, 1.365), (s * .316, -.091, 1.368),
            (s * .369, -.081, 1.235), (s * .215, -.143, 1.278)], .015, 'Leather', 'UpperArm.' + ('R' if s > 0 else 'L'))
        vertices, faces = [], []
        for i in range(13):
            t = i / 12
            z, x = 1.328 - .376 * t, s * (.164 - .304 * t)
            # Broad leather strip sinks its edges into the shirt contour; not X pipes.
            body_width = .224 if z > 1.20 else (.175 if z > 1.05 else .162)
            body_depth = .148 if z > 1.20 else (.133 if z > 1.05 else .114)
            for u in (-1, 1):
                px = x + u * .017
                y = .009 - body_depth * math.sqrt(max(.05, 1 - (px / body_width) ** 2)) - .009
                vertices.append((px, y, z))
        for i in range(12):
            faces.append((i * 2, i * 2 + 1, i * 2 + 3, i * 2 + 2))
        thicken(mesh('Fitted flat leather bandolier', vertices, faces, 'Leather', smooth=True), .009, .0015)
    blade(.425, True)
    blade(-.425, True)


def build_sabina():
    body('Skin', 'BrownCloth', female=True)
    # Leather jacket lapels and seam fold lines; no plated armor or metallic cuffs.
    for s in (-1, 1):
        panel('Leather jacket folded lapel', [(s * .142, -.109, 1.385), (s * .042, -.163, 1.308),
            (s * .078, -.172, 1.167), (s * .175, -.140, 1.290)], .009, 'Leather')
        seam = []
        for i in range(17):
            t = i / 16
            z, x = 1.294 - .349 * t, s * (.128 - .029 * t)
            # Keep seam inside the torso silhouette and on its actual curved front.
            width = .229 - .074 * t
            depth = .148 - .036 * t
            y = .009 - depth * math.sqrt(max(.05, 1 - (x / width) ** 2)) - .006
            seam.append((x, y, z))
        trim('Jacket seam topstitch', seam, 'Leather', .0025)
    # Genuine flared short skirt with tapered folds and leather hem.
    n = 48
    sections = [(.603, 0, -.005, .286, .218), (.626, 0, -.005, .284, .216),
                (.735, 0, 0, .257, .195), (.870, 0, 0, .202, .151)]
    skirt = loft('Short flared leather skirt', sections, 'BrownCloth', 'Hips', n, False)
    for v in skirt.data.vertices:
        a = math.atan2(v.co.y / .18, v.co.x / .24)
        v.co.x += .010 * math.cos(a * 8) * math.cos(a)
        v.co.y += .010 * math.cos(a * 8) * math.sin(a)
    trim('Skirt rolled hem', [(math.cos(j * math.tau / n) * .288, -.005 + math.sin(j * math.tau / n) * .220,
        .610) for j in range(n + 1)], 'Leather', .008, 'Hips')
    for i in range(12):
        a = .05 + i / 11 * math.pi
        x, y = math.cos(a) * .147, math.sin(a) * .112
        hair_lock('Long separated hair lock', [(x, y, 1.807), (x * 1.12, y + .023, 1.640),
            (x * 1.1, y + .044, 1.428), (x * .86, y + .055, 1.337)], 'Leather', .025)
    # Curved, pinched cowboy crown and turned-up brim built as tailored surfaces.
    loft('Pinched cowboy crown', [(1.826, 0, .005, .189, .156), (1.875, 0, .010, .185, .152),
        (1.986, 0, .014, .151, .135), (2.016, 0, .018, .140, .128),
        (2.025, 0, .018, .134, .122)], 'BrownCloth', 'Head', 32)
    verts, faces = [], []
    for radial in range(5):
        t = radial / 4
        for j in range(64):
            a = j * math.tau / 64
            x = (.169 + .191 * t) * math.cos(a)
            y = (.145 + .141 * t) * math.sin(a)
            z = 1.831 + .070 * t ** 2 * abs(math.cos(a)) ** 3 - .018 * t * max(0, -math.sin(a))
            verts.append((x, y, z))
    for i in range(4):
        for j in range(64):
            a, b = i * 64 + j, i * 64 + (j + 1) % 64
            faces.append((a, b, b + 64, a + 64))
    brim = mesh('Turned cowboy brim', verts, faces, 'BrownCloth', 'Head', True)
    mod = brim.modifiers.new('Leather brim thickness', 'SOLIDIFY')
    mod.thickness = .013
    bpy.context.view_layer.objects.active = brim
    bpy.ops.object.modifier_apply(modifier=mod.name)
    trim('Rolled hat brim piping', verts[-64:] + [verts[-64]], 'Leather', .008, 'Head')
    loft('Cowboy leather hat band', [(1.852, 0, .010, .192, .158), (1.895, 0, .013, .184, .153)], 'Leather', 'Head', 32)
    # Rifle: wooden stock follows grip, octagonal receiver/barrel, sights and muzzle.
    x, y = .425, -.178
    panel('Carved rifle stock', [(x - .049, y - .029, .489), (x + .065, y - .029, .489),
        (x + .047, y - .029, .747), (x + .026, y - .029, .854),
        (x - .033, y - .029, .849)], .067, 'BrownCloth', 'Hand.R', .010)
    path_tube('Octagonal rifle barrel', [(x, y, .848), (x, y, 1.537)], [.028, .023], 'Steel', 'Hand.R', 8, smooth=False)
    path_tube('Muzzle lip', [(x, y, 1.522), (x, y, 1.550)], [.030, .030], 'Steel', 'Hand.R', 16)
    path_tube('Muzzle bore inset', [(x, y, 1.5505), (x, y, 1.551)], [.017, .017], 'Shadow', 'Hand.R', 16)
    trim('Rifle brass spine', [(x, y - .030, .842), (x, y - .030, 1.004)], 'Bronze', .010, 'Hand.R')
    trim('Trigger guard', [(x - .016, y - .028, .771), (x - .059, y - .030, .742),
        (x - .061, y - .030, .811), (x - .016, y - .028, .820)], 'Bronze', .007, 'Hand.R')
    for z in (1.01, 1.40):
        trim('Barrel band', [(x - .03, y, z), (x, y - .033, z), (x + .03, y, z)], 'Bronze', .007, 'Hand.R')
    # Holstered sidearm is held at proper grip instead of intersecting the forearm.
    path_tube('Revolver grip', [(-.425, -.170, .716), (-.425, -.176, .831)], [.025, .027], 'Leather', 'Hand.L', 8, smooth=False)
    path_tube('Revolver barrel', [(-.425, -.178, .846), (-.425, -.376, .846)], [.024, .022], 'Steel', 'Hand.L', 8, smooth=False)
    path_tube('Revolver cylinder', [(-.425, -.177, .829), (-.425, -.245, .829)], [.037, .037], 'Steel', 'Hand.L', 12, smooth=False)


BUILDERS = {name: globals()['build_' + name] for name in HEROES}


def rig_export(kind, out):
    bpy.ops.object.select_all(action='DESELECT')
    for obj in PARTS:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = PARTS[0]
    bpy.ops.object.join()
    obj = bpy.context.object
    obj.name = kind + ' refined silhouette'
    # Shader-compatible material slots retained and unused slots removed.
    bpy.ops.object.material_slot_remove_unused()
    # Closed panels include n-gons. Triangulate before tangent export, preserving
    # the deliberate smooth organic / flat forged faces from the source pieces.
    triangulate = obj.modifiers.new('Stable realtime triangulation', 'TRIANGULATE')
    triangulate.keep_custom_normals = True
    bpy.ops.object.modifier_apply(modifier=triangulate.name)
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.mesh.normals_make_consistent(inside=False)
    bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=.008)
    bpy.ops.object.mode_set(mode='OBJECT')
    obj.data.uv_layers.active.name = 'HeroSurfaceUV'
    data = bpy.data.armatures.new(kind + ' semantic skeleton')
    rig = bpy.data.objects.new(kind + ' rig', data)
    bpy.context.collection.objects.link(rig)
    bpy.ops.object.select_all(action='DESELECT')
    rig.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode='EDIT')
    specs = [('Root', (0, 0, 0), (0, 0, .3), None), ('Hips', (0, 0, .7), (0, 0, .95), 'Root'),
        ('Spine', (0, 0, .95), (0, 0, 1.4), 'Hips'), ('Head', (0, 0, 1.4), (0, 0, 1.9), 'Spine')]
    for side, s in (('L', -1), ('R', 1)):
        specs += [('UpperArm.' + side, (s * .24, 0, 1.34), (s * .35, 0, 1.07), 'Spine'),
            ('LowerArm.' + side, (s * .35, 0, 1.07), (s * .42, -.13, .85), 'UpperArm.' + side),
            ('Hand.' + side, (s * .42, -.13, .85), (s * .43, -.14, .7), 'LowerArm.' + side),
            ('UpperLeg.' + side, (s * .14, 0, .85), (s * .16, 0, .47), 'Hips'),
            ('LowerLeg.' + side, (s * .16, 0, .47), (s * .16, 0, .1), 'UpperLeg.' + side)]
    for name, head, tail, parent in specs:
        bone = data.edit_bones.new(name)
        bone.head, bone.tail = head, tail
        if parent:
            bone.parent = data.edit_bones[parent]
    bpy.ops.object.mode_set(mode='OBJECT')
    mod = obj.modifiers.new('Rigid semantic piece rig', 'ARMATURE')
    mod.object, obj.parent = rig, rig
    obj.select_set(True)
    obj.data.calc_loop_triangles()
    coordinates = [v.co for v in obj.data.vertices]
    bounds_min = [min(v[k] for v in coordinates) for k in range(3)]
    bounds_max = [max(v[k] for v in coordinates) for k in range(3)]
    report = {'hero': kind, 'triangles': len(obj.data.loop_triangles), 'vertices': len(coordinates),
        'bones': [b.name for b in data.bones], 'materialSlots': [m.name for m in obj.data.materials],
        'boundsMinBlender': bounds_min, 'boundsMaxBlender': bounds_max,
        'heightMeters': bounds_max[2] - bounds_min[2],
        'uvLayer': obj.data.uv_layers.active.name, 'tangentExport': True,
        'stage': 'geometry refinement; rigid component weights; no dedicated attack animation',
        'runtimeSizing': 'existing HeroHeight normalization; Blender +Z height, -Y facing; FBX Y up'}
    assert len(data.bones) == 14, 'Semantic skeleton must remain 14 bones'
    assert all(m.name in PALETTE for m in obj.data.materials), 'Unsupported runtime material slot'
    assert 0 < report['triangles'] < 40000, 'Unexpected density; inspect generator before shipping'
    assert all(math.isfinite(component) for vertex in coordinates for component in vertex), 'Non-finite geometry'
    assert len(obj.data.uv_layers.active.data) == len(obj.data.loops), 'Incomplete export UV'
    # Only a private run directory is accepted here. Production paths are handled
    # after all selected characters, renders and the editable source succeed.
    bpy.ops.export_scene.fbx(filepath=str(out / (kind + '.fbx')), use_selection=True,
        object_types={'ARMATURE', 'MESH'}, add_leaf_bones=False, bake_anim=False,
        axis_forward='-Z', axis_up='Y', mesh_smooth_type='FACE', use_custom_props=True,
        use_tspace=True)
    return rig, obj, report


def studio():
    scene = bpy.context.scene
    scene.render.engine = 'CYCLES'
    scene.cycles.device = 'CPU'
    scene.cycles.use_denoising = True
    scene.world = bpy.data.worlds.new('Hero neutral studio')
    scene.world.use_nodes = True
    scene.world.node_tree.nodes['Background'].inputs[0].default_value = (.17, .18, .22, 1)
    scene.world.node_tree.nodes['Background'].inputs[1].default_value = .6
    for name, loc, power, color, size in [('Key', (-3, -4, 5), 700, (1, .87, .74), 3.0),
        ('Fill', (3, -2, 3.5), 450, (.67, .81, 1), 3.0), ('Rim', (1, 3, 4), 900, (.65, .83, 1), 2.5)]:
        d = bpy.data.lights.new(name, 'AREA')
        d.energy, d.color, d.size = power, color, size
        o = bpy.data.objects.new(name, d)
        scene.collection.objects.link(o)
        o.location = loc
        o.rotation_euler = (Vector((0, 0, 1)) - o.location).to_track_quat('-Z', 'Y').to_euler()
    bpy.ops.mesh.primitive_plane_add(size=200)
    floor = bpy.context.object
    floor.name = 'Studio floor - never exported'
    floor.location.z = -.008
    floor.data.materials.append(MATERIALS['Shadow'])
    d = bpy.data.cameras.new('Inspection camera')
    camera = bpy.data.objects.new('Inspection camera', d)
    scene.collection.objects.link(camera)
    d.type, d.ortho_scale = 'ORTHO', 2.55
    scene.camera = camera
    scene.view_settings.view_transform = 'AgX'
    scene.render.image_settings.file_format = 'PNG'
    return scene, camera, floor


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def publish_batch(root, run, files, replace_generated):
    """Stage all output, then replace known targets; roll back on any I/O error.

    Per-file replace is atomic. Filesystem multi-file publication cannot hide the
    short replacement interval from an external Unity auto-import, so callers
    must close/serialize Unity during --publish (same as all asset generators).
    """
    token = uuid.uuid4().hex[:10]
    prepared, replaced, result = [], [], []
    previous = run / 'previous-publish'
    previous.mkdir()
    for candidate, target in files:
        resolved = target.resolve()
        if not resolved.is_relative_to(root) or target.name == 'Workbench.blend':
            raise ValueError('Publication target outside generated asset contract')
        if target.suffix == '.blend' and target.exists() and not replace_generated:
            raise FileExistsError('Generated source already exists; use --replace-generated after review: ' + str(target.relative_to(root)))
        if not candidate.is_file() or candidate.stat().st_size == 0:
            raise ValueError('Missing or empty candidate: ' + str(candidate.relative_to(root)))
    try:
        for index, (candidate, target) in enumerate(files):
            target.parent.mkdir(parents=True, exist_ok=True)
            backup = previous / (str(index) + '-' + target.name) if target.exists() else None
            if backup:
                shutil.copy2(target, backup)
            pending = target.with_name('.' + target.name + '.' + token + '.pending')
            shutil.copy2(candidate, pending)
            prepared.append((pending, target, backup))
            if digest(pending) != digest(candidate):
                raise IOError('Copy integrity check failed')
            result.append({'path': target.relative_to(root).as_posix(), 'sha256': digest(candidate),
                'previousBackup': backup.relative_to(root).as_posix() if backup else None})
        for pending, target, backup in prepared:
            os.replace(pending, target)
            replaced.append((target, backup))
    except Exception:
        for target, backup in reversed(replaced):
            if backup:
                restore = target.with_name('.' + target.name + '.' + token + '.restore')
                shutil.copy2(backup, restore)
                os.replace(restore, target)
            else:
                target.unlink(missing_ok=True)
        raise
    finally:
        for pending, _, _ in prepared:
            pending.unlink(missing_ok=True)
    (run / 'publication.json').write_text(json.dumps({'published': result,
        'transaction': 'all candidates validated before replacement; per-file atomic replace with rollback',
        'unityRequirement': 'Unity import/build must be serialized during publication'}, indent=2), encoding='utf-8')
    return result


def main():
    args = argparse.ArgumentParser(description=__doc__)
    args.add_argument('root', type=Path)
    args.add_argument('--only', choices=HEROES)
    args.add_argument('--no-render', action='store_true')
    args.add_argument('--samples', type=int, default=24)
    args.add_argument('--resolution', type=int, default=1000)
    args.add_argument('--publish', action='store_true', help='Publish all selected validated staged assets')
    args.add_argument('--replace-generated', action='store_true', help='Allow replacing an existing generated refinement .blend')
    opts = args.parse_args(sys.argv[sys.argv.index('--') + 1:])
    root = opts.root.resolve()
    blend_name = 'HeroRefinement' + ('-' + opts.only if opts.only else '') + '.blend'
    if opts.publish and (root / 'art/production/assets' / blend_name).exists() and not opts.replace_generated:
        raise FileExistsError('Review existing generated source and explicitly pass --replace-generated before publication')
    run_id = datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%SZ') + '-' + uuid.uuid4().hex[:8]
    run = root / 'artifacts/hero-refinement/runs' / run_id
    out, source, preview = run / 'fbx', run / 'source', run / 'previews'
    for path in (out, source, preview):
        path.mkdir(parents=True, exist_ok=True)
    # Capture once before geometry/render work: another task may edit the source
    # while this Blender process renders. Never re-read it at manifest time.
    generator_bytes = Path(__file__).read_bytes()
    generator_sha256 = hashlib.sha256(generator_bytes).hexdigest()
    generator_snapshot = source / 'refine_hero_models.py'
    generator_snapshot.write_bytes(generator_bytes)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.preferences.filepaths.save_version = 0
    bpy.context.scene['generated_by'] = 'tools/art/refine_hero_models.py'
    bpy.context.scene['generation_run'] = run.relative_to(root).as_posix()
    material_palette()
    models, reports = [], []
    selected = (opts.only,) if opts.only else HEROES
    for kind in selected:
        PARTS.clear()
        BUILDERS[kind]()
        rig, obj, report = rig_export(kind, out)
        models.append((kind, rig, obj))
        reports.append(report)
        print('REFINED_HERO', kind, report['triangles'], 'triangles', flush=True)
    scene, camera, floor = studio()
    scene.cycles.samples = opts.samples
    scene.render.resolution_x = opts.resolution
    scene.render.resolution_y = opts.resolution
    scene.render.resolution_percentage = 100
    views = [('front', (0, -6, 1.48)), ('three-quarter', (3.8, -6, 2.6)), ('back', (0, 6, 1.5))]
    if not opts.no_render:
        portraits = run / 'portraits'
        portraits.mkdir(parents=True, exist_ok=True)
        for kind, rig, obj in models:
            for _, other_rig, other_obj in models:
                other_obj.hide_render = other_obj != obj
            for name, location in views:
                camera.location = location
                camera.rotation_euler = (Vector((0, 0, 1.02)) - camera.location).to_track_quat('-Z', 'Y').to_euler()
                scene.render.filepath = str(preview / (kind + '-' + name + '.png'))
                bpy.ops.render.render(write_still=True)
            # A real transparent studio portrait, not a crop of lineup pixels.
            # Shoulder/header framing leaves room for hats, crest and beard.
            floor.hide_render = True
            scene.render.film_transparent = True
            scene.render.image_settings.color_mode = 'RGBA'
            scene.render.resolution_x = scene.render.resolution_y = 256
            camera.data.ortho_scale = 1.22
            camera.location = (1.65, -6, 2.04)
            camera.rotation_euler = (Vector((0, 0, 1.53)) - camera.location).to_track_quat('-Z', 'Y').to_euler()
            scene.render.filepath = str(portraits / (kind + '.png'))
            bpy.ops.render.render(write_still=True)
            floor.hide_render = False
            scene.render.film_transparent = False
            scene.render.resolution_x = scene.render.resolution_y = opts.resolution
            camera.data.ortho_scale = 2.55
    # Store a readable spaced collection, not six overlapping characters.
    for index, (_, rig, obj) in enumerate(models):
        obj.hide_render = False
        rig.location.x = (index - (len(models) - 1) / 2) * 1.95
    camera.location = (1.5, -14, 5)
    camera.rotation_euler = (Vector((0, 0, .95)) - camera.location).to_track_quat('-Z', 'Y').to_euler()
    camera.data.ortho_scale = max(3.0, len(models) * 1.95)
    scene.render.resolution_x = max(1200, len(models) * 400)
    scene.render.resolution_y = 900
    # An --only run gets its own .blend, preserving any reviewed full collection.
    bpy.ops.wm.save_as_mainfile(filepath=str(source / blend_name), compress=True)
    report_path = run / ('refinement-manifest' + ('-' + opts.only if opts.only else '') + '.json')
    report_path.parent.mkdir(parents=True, exist_ok=True)
    report_path.write_text(json.dumps({'schemaVersion': 1, 'generator': 'tools/art/refine_hero_models.py',
        'generatorSha256': generator_sha256,
        'generatorSnapshot': generator_snapshot.relative_to(root).as_posix(),
        'run': run.relative_to(root).as_posix(),
        'blend': (source / blend_name).relative_to(root).as_posix(), 'assets': reports,
        'limitations': ['Rigid piece weights; no soft deformation validation',
            'No hero-specific attack clips, LOD, sculpted normal maps or facial animation',
            'Original stylized forms; not official character designs',
            'Runtime replacement materials differ from Blender studio materials; inspect Unity separately']},
        ensure_ascii=False, indent=2), encoding='utf-8')
    if not opts.no_render:
        scene.render.filepath = str(preview / ('lineup' + ('-' + opts.only if opts.only else '') + '.png'))
        bpy.ops.render.render(write_still=True)
    publication = [(out / (kind + '.fbx'), root / 'unity/Assets/Resources/UI3D/Heroes' / (kind + '.fbx')) for kind in selected]
    publication += [(source / blend_name, root / 'art/production/assets' / blend_name),
                    (report_path, root / 'art/heroes' / report_path.name)]
    if not opts.no_render:
        publication += [(run / 'portraits' / (kind + '.png'), root / 'artifacts/art-refinement/heroes/portraits' / (kind + '.png')) for kind in selected]
        publication += [(path, root / 'artifacts/hero-refinement' / path.name) for path in sorted(preview.glob('*.png'))]
    (run / 'candidate-files.json').write_text(json.dumps({'publishRequested': opts.publish,
        'files': [{'candidate': candidate.relative_to(root).as_posix(), 'target': target.relative_to(root).as_posix(),
                   'sha256': digest(candidate)} for candidate, target in publication]}, indent=2), encoding='utf-8')
    if opts.publish:
        publish_batch(root, run, publication, opts.replace_generated)
    print('GOA_HERO_REFINEMENT_' + ('PUBLISHED' if opts.publish else 'STAGED'), report_path.relative_to(root).as_posix(), flush=True)


if __name__ == '__main__':
    main()
