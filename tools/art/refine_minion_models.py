"""Refine the three existing semantic rigs without executing the legacy generator.

Blender 4.5:
  blender -b --factory-startup --python-exit-code 1 --python tools/art/refine_minion_models.py -- --root <repo>

Default: staged FBX, a new editable .blend, manifest and three studio views.
--publish replaces only the three existing FBX payloads after every contract passes;
their .meta files and the previous editable collection are never written.
"""
from __future__ import annotations

import argparse
import ast
import hashlib
import json
import math
import shutil
import sys
from pathlib import Path

import bpy
from mathutils import Vector


def arguments():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', required=True)
    parser.add_argument('--output', default='artifacts/minion-refinement')
    parser.add_argument('--publish', action='store_true')
    parser.add_argument('--skip-render', action='store_true')
    parser.add_argument('--replace-generated', action='store_true',
                        help='Replace this generator\'s previous MinionRefinement.blend, never a hand-edited source.')
    parser.add_argument('--samples', type=int, default=48)
    return parser.parse_args(sys.argv[sys.argv.index('--') + 1:])


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest() if path.exists() else None


def legacy_library(path):
    """Only pure function definitions and the palette; no argument parsing/build/save/render."""
    tree = ast.parse(path.read_text(encoding='utf-8-sig'), filename=str(path))
    selected = []
    for node in tree.body:
        if isinstance(node, ast.FunctionDef):
            if node.name == 'rig_and_export':
                # Retain exact semantic rig and weapon transforms, defer file export
                # until every model passes our skeleton/bounds/weight contract.
                node.body = [statement for statement in node.body
                             if not (isinstance(statement, ast.Expr)
                                     and isinstance(statement.value, ast.Call)
                                     and ast.unparse(statement.value.func) == 'bpy.ops.export_scene.fbx')]
            selected.append(node)
        elif isinstance(node, ast.Assign) and any(isinstance(t, ast.Name) and t.id == 'PALETTE' for t in node.targets):
            selected.append(node)
    required = {'melee', 'ranged', 'heavy', 'rig_and_export', 'finish', 'lathe', 'prism', 'rod', 'sphere', 'box'}
    if not required.issubset({n.name for n in selected if isinstance(n, ast.FunctionDef)}):
        raise RuntimeError('Legacy source contract changed; review its new geometry API first.')
    namespace = {'bpy': bpy, 'math': math, 'Vector': Vector, 'parts': [], 'collection': None}
    exec(compile(ast.fix_missing_locations(ast.Module(body=selected, type_ignores=[])), str(path), 'exec'), namespace)
    return namespace


def palette(lib):
    materials = {}
    for name, (code, metallic, roughness) in lib['PALETTE'].items():
        material = bpy.data.materials.get(name) or bpy.data.materials.new(name)
        material.diffuse_color = (*lib['rgb'](code), 1)
        material.use_nodes = True
        shader = material.node_tree.nodes.get('Principled BSDF')
        shader.inputs['Base Color'].default_value = material.diffuse_color
        shader.inputs['Metallic'].default_value = metallic
        shader.inputs['Roughness'].default_value = roughness
        if name == 'Glow':
            shader.inputs['Emission Color'].default_value = material.diffuse_color
            shader.inputs['Emission Strength'].default_value = .45
        materials[name] = material
    lib['materials'] = materials


def set_active(obj):
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj


def smooth_round(obj):
    for polygon in obj.data.polygons:
        # Ngon end caps stay planar; barrel/cloth sides have continuous normals.
        polygon.use_smooth = len(polygon.vertices) <= 4
    return obj


def mesh_part(lib, name, vertices, faces, material, bone='Spine', smooth=True):
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    lib['collection'].objects.link(obj)
    set_active(obj)
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.mesh.normals_make_consistent(inside=False)
    bpy.ops.object.mode_set(mode='OBJECT')
    lib['finish'](obj, name, material, bone)
    return smooth_round(obj) if smooth else obj


def curve_points(points, resolution=5):
    """Centripetal-looking bounded Hermite path; exact endpoints for weapon grips/tips."""
    points = [Vector(p) for p in points]
    if len(points) < 3:
        return points
    result = []
    for i in range(len(points) - 1):
        p0 = points[max(0, i - 1)]
        p1, p2 = points[i], points[i + 1]
        p3 = points[min(len(points) - 1, i + 2)]
        for step in range(resolution):
            t = step / resolution
            value = .5 * ((2 * p1) + (-p0 + p2) * t
                          + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t
                          + (-p0 + 3 * p1 - 3 * p2 + p3) * t * t * t)
            # Prevent overshoot changing the silhouette/grip envelope.
            result.append(Vector(tuple(max(min(p1[k], p2[k]), min(max(p1[k], p2[k]), value[k])) for k in range(3))))
    result.append(points[-1])
    return result


def tube(lib, name, points, radius, material, bone='Spine', sides=8, resolution=4):
    path = curve_points(points, resolution)
    vertices, faces = [], []
    for i, point in enumerate(path):
        tangent = (path[min(i + 1, len(path) - 1)] - path[max(0, i - 1)]).normalized()
        axis = Vector((1, 0, 0)) if abs(tangent.x) < .8 else Vector((0, 1, 0))
        u, v = tangent.cross(axis).normalized(), None
        v = tangent.cross(u).normalized()
        for j in range(sides):
            angle = math.tau * j / sides
            vertices.append(tuple(point + (u * math.cos(angle) + v * math.sin(angle)) * radius))
    for i in range(len(path) - 1):
        for j in range(sides):
            a = i * sides + j
            b = i * sides + (j + 1) % sides
            faces.append((a, b, b + sides, a + sides))
    faces.extend([tuple(reversed(range(sides))), tuple((len(path) - 1) * sides + j for j in range(sides))])
    return mesh_part(lib, name, vertices, faces, material, bone)


def install_refinement(lib):
    base_sphere, base_cone = lib['sphere'], lib['cone']
    base_box, base_prism = lib['box'], lib['prism']

    def sphere(name, at, size, material, bone='Spine', segments=16, rings=8):
        detail = any(word in name.lower() for word in ('helmet', 'hood volume', 'helm', 'cuirass', 'torso'))
        if detail:
            segments, rings = max(segments, 28), max(rings, 14)
        elif 'rivet' not in name.lower():
            segments, rings = max(segments, 20), max(rings, 10)
        return smooth_round(base_sphere(name, at, size, material, bone, segments, rings))

    def cone(name, at, r1, r2, depth, material, bone='Spine', vertices=12):
        return smooth_round(base_cone(name, at, r1, r2, depth, material, bone, max(vertices, 16)))

    def box(name, at, size, material, bone='Spine', bevel=.025, rotation=(0, 0, 0)):
        obj = base_box(name, at, size, material, bone, bevel, rotation)
        # Weighted normals preserve the forged broad planes. Curved silhouettes
        # come from a geometric bevel, not globally smoothing every hard edge.
        return obj

    def prism(name, outline, front, back, material, bone='Spine', bevel=.015):
        return base_prism(name, outline, front, back, material, bone, bevel)

    def stripe(name, points, radius, material, bone='Spine'):
        return tube(lib, name, points, radius, material, bone,
                    sides=10 if 'bow' in name.lower() else 8,
                    resolution=7 if 'bow' in name.lower() else 3)

    def lathe(name, profiles, material, bone='Spine', segments=16, folds=0):
        # Smooth restrained cloth pleats, retaining the original vertical envelope.
        count = 40 if folds else 32
        rows = []
        for i, (z, radius) in enumerate(profiles[:-1]):
            nz, nr = profiles[i + 1]
            for j in range(4):
                t = j / 4
                ease = t * t * (3 - 2 * t)
                rows.append((z + (nz - z) * t, radius + (nr - radius) * ease))
        rows.append(profiles[-1])
        vertices, faces = [], []
        for z, radius in rows:
            for j in range(count):
                a = math.tau * j / count
                corrugation = folds * (.80 * math.cos(a * 8 + .35 * z) + .20 * math.cos(a * 16 + .18 * z))
                rr = radius * (1 + corrugation)
                vertices.append((rr * math.cos(a), rr * math.sin(a) * .76, z))
        for row in range(len(rows) - 1):
            for j in range(count):
                a = row * count + j
                b = row * count + (j + 1) % count
                faces.append((a, b, b + count, a + count))
        faces.extend([tuple(reversed(range(count))), tuple((len(rows) - 1) * count + j for j in range(count))])
        return mesh_part(lib, name, vertices, faces, material, bone)

    lib.update(sphere=sphere, cone=cone, box=box, prism=prism, stripe=stripe, lathe=lathe)


def glove_details(lib, kind):
    box, sphere = lib['box'], lib['sphere']
    for side, sign in [('L', -1), ('R', 1)]:
        center = Vector((sign * .59, -.085, .83) if kind == 'Heavy'
                        else (sign * .425, -.17, .75) if kind == 'Ranged'
                        else (sign * .43, -.13, .70))
        bone = 'Hand.' + side
        scale = 1.15 if kind == 'Heavy' else .8 if kind == 'Ranged' else 1
        # Split fingers curl around the unchanged vertical grip. Keeping all
        # fingers on Hand preserves the existing rigid runtime IK contract.
        for finger in range(4):
            z = center.z + (finger - 1.5) * .038 * scale
            path = [(center.x + sign * .070 * scale, center.y + .012, z),
                    (center.x + sign * .050 * scale, center.y - .079 * scale, z),
                    (center.x - sign * .014 * scale, center.y - .090 * scale, z),
                    (center.x - sign * .049 * scale, center.y - .046 * scale, z)]
            tube(lib, 'Curled articulated glove finger ' + side + str(finger), path,
                 .017 * scale, 'Leather', bone, sides=6, resolution=2)
            sphere('Raised knuckle ' + side + str(finger),
                   (center.x + sign * .029 * scale, center.y - .079 * scale, z),
                   (.025 * scale, .016 * scale, .016 * scale),
                   'Steel' if kind != 'Ranged' else 'Leather', bone, 8, 4)
        tube(lib, 'Opposed gripping thumb ' + side,
             [tuple(center + Vector((sign * .061, .008, .044)) * scale),
              tuple(center + Vector((sign * .077, -.061, .081)) * scale),
              tuple(center + Vector((sign * .009, -.102, .061)) * scale)],
             .025 * scale, 'Leather', bone, sides=8, resolution=3)
        box('Layered wrist cuff ' + side, tuple(center + Vector((0, .029, .075)) * scale),
            (.155 * scale, .099 * scale, .055 * scale),
            'Bronze', bone, .009)


def stitched_strap(lib, name, points, bone='Spine', width=.018):
    tube(lib, name, points, width, 'Leather', bone, sides=8, resolution=4)
    path = curve_points(points, 4)
    for index, p in enumerate(path[::3]):
        # Raised short stitches on the outward surface, not a noisy whole-body lattice.
        tube(lib, name + ' stitch ' + str(index),
             [tuple(p + Vector((-.008, -.012, 0))), tuple(p + Vector((.008, -.012, 0)))],
             .0034, 'Parchment', bone, sides=4, resolution=1)


def add_model_details(lib, kind):
    glove_details(lib, kind)
    sphere, box, rod = lib['sphere'], lib['box'], lib['rod']
    if kind == 'Melee':
        # Continuous helmet bands and layered segmented spaulders.
        for s, side in [(-1, 'L'), (1, 'R')]:
            tube(lib, 'Helmet forged side seam ' + side,
                 [(s * .16, .22, 1.47), (s * .27, .12, 1.48), (s * .29, -.09, 1.47), (s * .23, -.25, 1.42)],
                 .013, 'SteelLight', 'Head', sides=6)
            for layer in range(2):
                sphere('Overlapping shoulder lames ' + side + str(layer),
                       (s * (.38 + layer * .025), -.008, 1.00 - layer * .054),
                       (.157, .19, .052), 'Steel', 'UpperArm.' + side, 16, 8)
            stitched_strap(lib, 'Cross boot strap ' + side,
                           [(s * .165 - .092, -.161, .18), (s * .165, -.177, .20), (s * .165 + .092, -.161, .18)],
                           'Foot.' + side, .016)
        # Fine raised inlay keeps broad readable shield planes instead of flat cubes.
        for s in (-1, 1):
            tube(lib, 'Shield engraved chevron ' + str(s),
                 [(-.53, -.405, .54), (-.53 + s * .13, -.405, .67), (-.53 + s * .17, -.401, .85)],
                 .009, 'Bronze', 'Weapon.L', sides=6)
        box('Sword pommel socket', (.49, -.13, .469), (.092, .087, .075), 'Bronze', 'Weapon.R', .018)
        sphere('Sword polished pommel', (.49, -.13, .447), (.045, .043, .031), 'SteelLight', 'Weapon.R', 12, 6)
        stitched_strap(lib, 'Cuirass shoulder strap', [(-.17, -.10, 1.10), (-.22, -.18, 1.06), (-.21, -.251, .95)], width=.021)
    elif kind == 'Ranged':
        # Back-mounted quiver remains below the existing arrow-tip envelope.
        for z in (.78, 1.16):
            t = (z - .63) / .68
            cx, cy = .16 + .11 * t, .22 + .06 * t
            points = [(cx + .122 * math.cos(math.tau * i / 16), cy + .122 * math.sin(math.tau * i / 16), z) for i in range(17)]
            tube(lib, 'Quiver stitched retaining band ' + str(z), points, .013, 'Bronze', sides=6, resolution=1)
        stitched_strap(lib, 'Diagonal quiver carrying strap',
                       [(-.21, -.14, 1.035), (-.09, -.228, .94), (.06, -.225, .80), (.21, -.11, .71)], width=.025)
        box('Quiver strap buckle', (.04, -.254, .821), (.083, .025, .080), 'Bronze', bevel=.009)
        # Discrete leather wraps follow the same middle grip, no change to tips.
        for j in range(5):
            z = .689 + j * .030
            points = [(-.425 + .055 * math.cos(math.tau * i / 12), -.17 + .055 * math.sin(math.tau * i / 12), z + i * .001) for i in range(13)]
            tube(lib, 'Bow wrapped grip ' + str(j), points, .006, 'Bronze', 'Weapon.L', sides=5, resolution=1)
        for s in (-1, 1):
            tube(lib, 'Hood sewn seam ' + str(s),
                 [(s * .06, .17, 1.68), (s * .13, .16, 1.64), (s * .23, .07, 1.53), (s * .29, -.06, 1.35), (s * .21, -.22, 1.15)],
                 .008, 'TeamInset', 'Head', sides=6)
    else:
        # Leg couplings add a mechanical language without changing eight leg bones.
        for i in range(8):
            angle = math.tau * i / 8
            bone = 'SpiderLeg.' + str(i)
            a = Vector((.35 * math.cos(angle), .35 * math.sin(angle), .30))
            b = Vector((.65 * math.cos(angle), .65 * math.sin(angle), .38))
            c = Vector((.82 * math.cos(angle), .82 * math.sin(angle), .035))
            rod('Hydraulic leg sleeve ' + str(i), tuple(a.lerp(b, .18)), tuple(a.lerp(b, .66)), .063, 'Steel', bone, 12)
            rod('Polished leg piston ' + str(i), tuple(b.lerp(c, .18)), tuple(b.lerp(c, .55)), .047, 'SteelLight', bone, 10)
        for j in range(12):
            angle = math.tau * j / 12
            # These are shield-bound before legacy scale, and remain on the rim.
            sphere('Shield chased rim boss ' + str(j),
                   (-.64 + .337 * math.cos(angle), -.557, 1.01 + .337 * math.sin(angle)),
                   (.018, .012, .018), 'SteelLight', 'Weapon.L', 8, 4)
        for s in (-1, 1):
            tube(lib, 'Heavy shield radial inlay ' + str(s),
                 [(-.64 + s * .065, -.577, .945), (-.64 + s * .205, -.577, .805), (-.64 + s * .245, -.577, .89)],
                 .012, 'Bronze', 'Weapon.L', sides=6)
        box('Gem sword pommel', (.64, -.25, .589), (.12, .105, .08), 'Bronze', 'Weapon.R', .021)
        sphere('Sword pommel cabochon', (.64, -.305, .589), (.033, .024, .030), 'TeamInset', 'Weapon.R', 12, 6)


def contract(rig, mesh):
    bpy.context.view_layer.update()
    return {
        'bones': {b.name: {'head': list(b.head_local), 'tail': list(b.tail_local),
                           'parent': b.parent.name if b.parent else None,
                           'matrix': [v for row in b.matrix_local for v in row]}
                  for b in rig.data.bones},
        'bounds': {'min': [min((mesh.matrix_world @ v.co)[i] for v in mesh.data.vertices) for i in range(3)],
                   'max': [max((mesh.matrix_world @ v.co)[i] for v in mesh.data.vertices) for i in range(3)]},
        'dimensions': list(mesh.dimensions),
    }


def verify_contract(kind, baseline, rig, mesh):
    current = contract(rig, mesh)
    if current['bones'] != baseline['bones']:
        raise RuntimeError(kind + ': rest skeleton or marker changed; refuse export.')
    for i, (old, new) in enumerate(zip(baseline['dimensions'], current['dimensions'])):
        if abs(old - new) > max(.003, old * .035):
            raise RuntimeError(f'{kind}: axis {i} envelope drift {old} -> {new} exceeds 3.5%.')
    if abs(current['bounds']['min'][2] - baseline['bounds']['min'][2]) > .005:
        raise RuntimeError(kind + ': foot/base elevation changed.')
    triangles = sum(len(p.vertices) - 2 for p in mesh.data.polygons)
    if triangles > 30000:
        raise RuntimeError(f'{kind}: triangle budget exceeded ({triangles} > 30000).')
    bone_names = set(current['bones'])
    if any(group.name not in bone_names for group in mesh.vertex_groups):
        raise RuntimeError(kind + ': unknown weight group.')
    if any(len(v.groups) != 1 or abs(sum(g.weight for g in v.groups) - 1) > .00001 for v in mesh.data.vertices):
        raise RuntimeError(kind + ': weights no longer have exact legacy rigid-piece semantics.')
    if rig.animation_data is not None or mesh.animation_data is not None:
        raise RuntimeError(kind + ': unexpected animation/root motion.')
    return current, triangles


def generate(lib, kind, refined=False):
    collection = bpy.data.collections.new(kind + (' refined editable assembly' if refined else ' baseline audit'))
    bpy.context.scene.collection.children.link(collection)
    lib['collection'], lib['parts'] = collection, []
    lib[kind.lower()]()
    if refined:
        add_model_details(lib, kind)
    rig, mesh, info = lib['rig_and_export'](kind)
    return rig, mesh, info, collection


def remove_collection(collection):
    for obj in list(collection.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    bpy.data.collections.remove(collection)


def unwrap(mesh):
    set_active(mesh)
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=.008,
                             area_weight=.5, correct_aspect=True, scale_to_bounds=True)
    bpy.ops.object.mode_set(mode='OBJECT')
    mesh.data.uv_layers.active.name = 'UV0_PaintReady'


def export(rig, mesh, path):
    bpy.ops.object.select_all(action='DESELECT')
    rig.select_set(True)
    mesh.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.export_scene.fbx(filepath=str(path), use_selection=True,
                             object_types={'ARMATURE', 'MESH'}, use_mesh_modifiers=True,
                             add_leaf_bones=False, bake_anim=False,
                             axis_forward='-Z', axis_up='Y', apply_unit_scale=True,
                             mesh_smooth_type='FACE', path_mode='AUTO')


def studio(models, output, samples, skip_render):
    scene = bpy.context.scene
    scene.render.engine = 'CYCLES'
    scene.cycles.device = 'CPU'
    scene.cycles.samples = samples
    scene.cycles.use_denoising = True
    scene.world.use_nodes = True
    scene.world.node_tree.nodes['Background'].inputs[0].default_value = (.075, .10, .14, 1)
    scene.world.node_tree.nodes['Background'].inputs[1].default_value = .4
    for i, (rig, _) in enumerate(models):
        rig.location.x = (i - 1) * 2.15
    for name, location, power, color, size in [
        ('Warm broad key', (-4, -4, 6), 850, (1, .84, .67), 5),
        ('Neutral soft fill', (3, -2, 4), 400, (.76, .86, 1), 4),
        ('Cool silhouette rim', (0, 3, 5), 1100, (.55, .77, 1), 4),
    ]:
        data = bpy.data.lights.new(name, 'AREA')
        data.energy, data.color, data.shape, data.size = power, color, 'DISK', size
        obj = bpy.data.objects.new(name, data)
        scene.collection.objects.link(obj)
        obj.location = location
        obj.rotation_euler = (Vector((0, 0, 1)) - obj.location).to_track_quat('-Z', 'Y').to_euler()
    bpy.ops.mesh.primitive_plane_add(size=200, location=(0, 0, -.018))
    ground = bpy.context.object
    ground.name = 'STUDIO floor - excluded from FBX'
    material = bpy.data.materials.new('STUDIO neutral slate')
    material.diffuse_color = (.027, .035, .046, 1)
    material.use_nodes = True
    material.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = material.diffuse_color
    material.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value = .8
    ground.data.materials.append(material)
    camera_data = bpy.data.cameras.new('STUDIO fixed presentation')
    camera = bpy.data.objects.new('STUDIO fixed presentation', camera_data)
    scene.collection.objects.link(camera)
    camera_data.type, camera_data.ortho_scale = 'ORTHO', 7.1
    scene.camera = camera
    scene.render.resolution_x, scene.render.resolution_y, scene.render.resolution_percentage = 1800, 1100, 100
    scene.render.image_settings.file_format = 'PNG'
    scene.view_settings.view_transform = 'AgX'
    shots = [('front', (0, -11, 3.0)), ('three-quarter', (3.4, -10, 4.5)), ('rear', (2.8, 10, 4.1))]
    for name, position in shots:
        camera.location = position
        camera.rotation_euler = (Vector((0, 0, 1.0)) - camera.location).to_track_quat('-Z', 'Y').to_euler()
        scene.render.filepath = str(output / ('minions-' + name + '.png'))
        if not skip_render:
            bpy.ops.render.render(write_still=True)
    camera.location = shots[1][1]
    camera.rotation_euler = (Vector((0, 0, 1)) - camera.location).to_track_quat('-Z', 'Y').to_euler()


def main():
    args = arguments()
    root = Path(args.root).resolve()
    output = (root / args.output).resolve()
    source = root / 'art/production/assets/MinionRefinement.blend'
    manifest_path = root / 'art/minions/refinement-manifest.json'
    runtime = root / 'unity/Assets/Scripts/UI3D/Resources/UI3D/Minions'
    legacy = root / 'tools/art/build_minions.py'
    protected = [root / 'art/minions/Goa2_Minion_Collection.blend',
                 root / 'art/production/assets/Workbench.blend']
    protected += [runtime / (kind + '.fbx.meta') for kind in ('Melee', 'Ranged', 'Heavy')]
    before = {str(p.relative_to(root)): digest(p) for p in protected}
    if source.exists() and not args.replace_generated:
        raise RuntimeError('MinionRefinement.blend already exists. Preserve manual work; explicit --replace-generated is required.')
    output.mkdir(parents=True, exist_ok=True)
    source.parent.mkdir(parents=True, exist_ok=True)
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete(use_global=False)
    lib = legacy_library(legacy)
    palette(lib)
    # Audit the unchanged builder in memory. Its top-level save/export/render is
    # never executed, and its rig function's FBX write is explicitly stripped.
    baselines = {}
    for kind in ('Melee', 'Ranged', 'Heavy'):
        rig, mesh, _, collection = generate(lib, kind)
        baselines[kind] = contract(rig, mesh)
        remove_collection(collection)
    install_refinement(lib)
    models, records = [], []
    for kind in ('Melee', 'Ranged', 'Heavy'):
        rig, mesh, info, _ = generate(lib, kind, refined=True)
        measured, triangles = verify_contract(kind, baselines[kind], rig, mesh)
        unwrap(mesh)
        path = output / (kind + '.fbx')
        export(rig, mesh, path)
        info.update(triangles=triangles, vertices=len(mesh.data.vertices),
                    baseline_dimensions=baselines[kind]['dimensions'],
                    dimensions=measured['dimensions'], skeleton_unchanged=True,
                    uv_layer=mesh.data.uv_layers.active.name, fbx_sha256=digest(path),
                    contract=measured)
        rig['asset_notes'] = 'Original refined Goa2 minion; unchanged semantic rest rig and rigid weights; no root motion. Runtime collision/pose review required.'
        models.append((rig, mesh))
        records.append(info)
    studio(models, output, args.samples, args.skip_render)
    bpy.context.scene['goa_generator'] = 'tools/art/refine_minion_models.py'
    bpy.context.scene['geometry_version'] = 'refinement-1'
    bpy.ops.wm.save_as_mainfile(filepath=str(source))
    if args.publish:
        backup = output / 'previous-runtime'
        backup.mkdir(exist_ok=True)
        for kind in ('Melee', 'Ranged', 'Heavy'):
            target = runtime / (kind + '.fbx')
            if target.exists() and not (backup / target.name).exists():
                shutil.copy2(target, backup / target.name)
            shutil.copy2(output / target.name, target)
    after = {str(p.relative_to(root)): digest(p) for p in protected}
    if before != after:
        raise RuntimeError('Protected .blend or Unity .meta changed during generation.')
    manifest = {'generator': 'tools/art/refine_minion_models.py', 'version': 1,
                'blender': bpy.app.version_string, 'legacy_source_sha256': digest(legacy),
                'source': str(source.relative_to(root)), 'published_runtime': args.publish,
                'source_sha256': digest(source), 'studio_rendered': not args.skip_render,
                'external_assets': [], 'protected_files_unchanged': True,
                'protected_sha256': after, 'models': records,
                'verification_boundary': 'Geometry and bind-pose contract only. Studio rendering is not runtime animation/collision acceptance.'}
    manifest_path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    (output / 'manifest.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print('GOA_MINION_REFINEMENT_DONE ' + json.dumps({'published': args.publish, 'triangles': {r['kind']: r['triangles'] for r in records}, 'output': str(output)}))


if __name__ == '__main__':
    main()
