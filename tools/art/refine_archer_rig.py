"""Ranged-only, non-destructive Blender motion-socket/arrow patch.

Run AFTER refine_minion_models.py, which regenerates the original three FBXs.
blender -b --factory-startup --python-exit-code 1 --python tools/art/refine_archer_rig.py -- --root <repo> --publish

The previous MinionRefinement.blend remains read-only. Output source is the new
ArcherMotionRig.blend. Ranged.fbx is published; Arrow.fbx is only created if absent.
Existing Arrow.fbx and all .meta files are untouched by subsequent rig revisions.
"""
import argparse
import hashlib
import json
import math
import shutil
import sys
from pathlib import Path

import bpy
from mathutils import Matrix, Vector


SOCKETS = {
    'BowRest': ((-.490, -.155, .880), 'Weapon.L'),
    'QuiverGrab': ((.240, .280, 1.310), 'Spine'),
    'QuiverExit': ((.240, .280, 1.650), 'Spine'),
}
UPPER_ARM_FACTOR = 1.15
LOWER_ARM_FACTOR = 1.60
BOW_FLEX_DEGREES = 22.8


def hash_file(path):
    return hashlib.sha256(path.read_bytes()).hexdigest() if path.exists() else None


def select(obj):
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj


def snapshot(rig, mesh):
    bpy.context.view_layer.update()
    positions = [mesh.matrix_world @ v.co for v in mesh.data.vertices]
    group_names = {g.index: g.name for g in mesh.vertex_groups}
    return {
        'bones': {b.name: {'head': list(b.head_local), 'tail': list(b.tail_local),
                           'parent': b.parent.name if b.parent else None,
                           'matrix': [v for row in b.matrix_local for v in row]}
                  for b in rig.data.bones},
        'vertices': [list(v.co) for v in mesh.data.vertices],
        'weights': [[(group_names[g.group], g.weight) for g in v.groups] for v in mesh.data.vertices],
        'min': [min(v[i] for v in positions) for i in range(3)],
        'max': [max(v[i] for v in positions) for i in range(3)],
    }


def lengthen_arms(rig, runtime, collection):
    """Change rest lengths and matching geometry; never scale a posed bone/weapon."""
    rest = {b.name: (b.head_local.copy(), b.tail_local.copy()) for b in rig.data.bones}
    bone_changes, geometry_frames, wrist_deltas = {}, {}, {}
    records = {}
    for side in ('L', 'R'):
        shoulder = rest['UpperArm.'+side][0]
        elbow = rest['LowerArm.'+side][0]
        wrist = rest['Hand.'+side][0]
        upper_axis, lower_axis = (elbow-shoulder).normalized(), (wrist-elbow).normalized()
        new_elbow = shoulder + (elbow-shoulder)*UPPER_ARM_FACTOR
        new_wrist = new_elbow + (wrist-elbow)*LOWER_ARM_FACTOR
        delta = new_wrist-wrist
        wrist_deltas[side] = delta
        bone_changes['UpperArm.'+side] = (shoulder, new_elbow)
        bone_changes['LowerArm.'+side] = (new_elbow, new_wrist)
        geometry_frames['UpperArm.'+side] = (shoulder, shoulder, upper_axis, UPPER_ARM_FACTOR)
        geometry_frames['LowerArm.'+side] = (elbow, new_elbow, lower_axis, LOWER_ARM_FACTOR)
        for name in ('Hand.'+side, 'Weapon.'+side):
            head, tail = rest[name]
            bone_changes[name] = (head+delta, tail+delta)
            geometry_frames[name] = (wrist, new_wrist, Vector((0, 0, 1)), 1.0)
        records[side] = {'old_shoulder': list(shoulder), 'old_elbow': list(elbow),
                         'old_wrist': list(wrist), 'new_elbow': list(new_elbow),
                         'new_wrist': list(new_wrist), 'wrist_translation': list(delta),
                         'upper_length': (new_elbow-shoulder).length,
                         'lower_length': (new_wrist-new_elbow).length}
    for name in ('BowGrip', 'BowTip.Top', 'BowTip.Bottom'):
        head, tail = rest[name]
        bone_changes[name] = (head+wrist_deltas['L'], tail+wrist_deltas['L'])

    # Named assembly parts and the runtime skin are edited together. Object
    # transforms differ, so all deformations are evaluated in rig/source space.
    for obj in list(collection.objects):
        if obj.type != 'MESH':
            continue
        data = obj.data
        matrix = obj.matrix_world.copy()
        inverse = matrix.inverted()
        normal_world = matrix.to_3x3().inverted().transposed()
        normal_local = matrix.to_3x3().transposed()
        group_names = {g.index: g.name for g in obj.vertex_groups}
        vertex_bones = [group_names[v.groups[0].group] if v.groups else '' for v in data.vertices]
        # Transform existing split normals by inverse stretch, preserving both
        # smooth sleeves and the intentional hard edges of rigid armor pieces.
        normals = [datum.vector.copy() for datum in data.corner_normals]
        for vertex, name in zip(data.vertices, vertex_bones):
            if name not in geometry_frames:
                continue
            origin, target, axis, factor = geometry_frames[name]
            p = matrix @ vertex.co
            local = p-origin
            p = target + local + axis*local.dot(axis)*(factor-1)
            vertex.co = inverse @ p
        new_normals = []
        for loop, normal in zip(data.loops, normals):
            name = vertex_bones[loop.vertex_index]
            if name in geometry_frames:
                _, _, axis, factor = geometry_frames[name]
                n = normal_world @ normal
                n -= axis*n.dot(axis)*(1-1/factor)
                normal = (normal_local @ n).normalized()
            new_normals.append(normal)
        data.update()
        if new_normals:
            data.normals_split_custom_set(new_normals)

    select(rig)
    bpy.ops.object.mode_set(mode='EDIT')
    for name, (head, tail) in bone_changes.items():
        rig.data.edit_bones[name].head = head
        rig.data.edit_bones[name].tail = tail
    bpy.ops.object.mode_set(mode='OBJECT')
    sockets = {name: (tuple(Vector(point) + (wrist_deltas['L'] if parent == 'Weapon.L' else Vector((0,0,0)))), parent)
               for name, (point, parent) in SOCKETS.items()}
    return records, set(bone_changes), sockets, wrist_deltas['L']


def build_mesh(name, verts, faces, material):
    data = bpy.data.meshes.new(name)
    data.from_pydata(verts, [], faces)
    data.update()
    obj = bpy.data.objects.new(name, data)
    bpy.context.scene.collection.objects.link(obj)
    data.materials.append(bpy.data.materials[material])
    select(obj)
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.mesh.normals_make_consistent(inside=False)
    bpy.ops.object.mode_set(mode='OBJECT')
    return obj


def box(name, at, size, material, bevel=.002):
    bpy.ops.mesh.primitive_cube_add(size=1, location=at)
    obj = bpy.context.object
    obj.name = name
    obj.scale = size
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    obj.data.materials.append(bpy.data.materials[material])
    if bevel:
        modifier = obj.modifiers.new('Small forged edge', 'BEVEL')
        modifier.width, modifier.segments = bevel, 2
        bpy.ops.object.modifier_apply(modifier=modifier.name)
    return obj


def tube(name, a, b, radius, material, vertices=12):
    start, end = Vector(a), Vector(b)
    bpy.ops.mesh.primitive_cylinder_add(vertices=vertices, radius=radius,
                                      depth=(end-start).length, location=(start+end)*.5)
    obj = bpy.context.object
    obj.name = name
    obj.rotation_euler = (end-start).to_track_quat('Z', 'Y').to_euler()
    obj.data.materials.append(bpy.data.materials[material])
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    for poly in obj.data.polygons:
        poly.use_smooth = len(poly.vertices) <= 4
    return obj


def unwrap(obj):
    select(obj)
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(65), island_margin=.015)
    bpy.ops.object.mode_set(mode='OBJECT')


def add_shelf(rig, runtime, translation):
    # Wood riser x=-.425: shelf protrudes to the archer's left. Its top is
    # z=.868, leaving a 12mm shaft radius/contact allowance at BowRest z=.880.
    # Long side follows -Y (arrow forward), with no raised wall across the shaft.
    shelf = box('Bow arrow-rest shelf', Vector((-.4725, -.155, .861))+translation, (.071, .106, .014), 'Bronze', .003)
    support = box('Arrow-rest leather contact', Vector((-.490, -.155, .868))+translation, (.030, .065, .004), 'Leather', .001)
    for obj in (shelf, support):
        weights = obj.vertex_groups.new(name='Weapon.L')
        weights.add(list(range(len(obj.data.vertices))), 1, 'REPLACE')
        unwrap(obj)
    # Retain editable unmerged shelf pieces, like the previous assembly source.
    originals = [shelf, support]
    for obj in originals:
        copy = obj.copy()
        copy.data = obj.data.copy()
        bpy.context.scene.collection.objects.link(copy)
        copy.name += ' editable source'
        copy.hide_render = True
        copy.hide_set(True)
    select(runtime)
    for obj in originals:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = runtime
    bpy.ops.object.join()
    runtime.name = 'Ranged Runtime Mesh'
    # Joining an uncolored shelf must not turn it black in the actor shader.
    color = runtime.data.color_attributes.get('Color')
    if color is not None:
        for loop in runtime.data.loops:
            if color.data[loop.index].color[3] < .01:
                color.data[loop.index].color = (.97, .97, .97, 1)
    return runtime


def add_markers(rig, sockets):
    select(rig)
    bpy.ops.object.mode_set(mode='EDIT')
    for name, (point, parent) in sockets.items():
        if name in rig.data.edit_bones:
            raise RuntimeError('Patch source unexpectedly already contains ' + name)
        marker = rig.data.edit_bones.new(name)
        marker.head = point
        marker.tail = Vector(point) + Vector((0, 0, .025))
        marker.parent = rig.data.edit_bones[parent]
        marker.use_deform = False
        marker.use_connect = False
    bpy.ops.object.mode_set(mode='OBJECT')


def add_bow_flex(rig, runtime, collection):
    """Two graded skin controls bend the existing bow, never the rigid riser.

    World/source axis is anatomical +X. Top rotates negative; Bottom positive.
    The tip markers are fully attached to the corresponding control. A smooth
    transition from Weapon.L to that control produces a continuous bend rather
    than a hinged, detached upper/lower half. This is a stylized skin deformation,
    not a material/elastic solver; the caller chooses angle from the draw length.
    """
    grip = rig.data.bones['BowGrip'].head_local.copy()
    saved_tips = {side: (rig.data.bones['BowTip.'+side].head_local.copy(),
                         rig.data.bones['BowTip.'+side].tail_local.copy())
                  for side in ('Top', 'Bottom')}
    select(rig)
    bpy.ops.object.mode_set(mode='EDIT')
    for side, sign in (('Top', 1), ('Bottom', -1)):
        control = rig.data.edit_bones.new('BowLimb.'+side)
        control.head = grip + Vector((0, .020, sign*.110))
        control.tail = saved_tips[side][0]
        control.parent = rig.data.edit_bones['Weapon.L']
        control.use_deform = True
        control.use_connect = False
        tip = rig.data.edit_bones['BowTip.'+side]
        tip.parent = control
        tip.use_connect = False
        tip.head, tip.tail = saved_tips[side]
    bpy.ops.object.mode_set(mode='OBJECT')

    changed = []
    for obj in list(collection.objects):
        if obj.type != 'MESH' or obj.vertex_groups.get('Weapon.L') is None:
            continue
        weapon = obj.vertex_groups['Weapon.L']
        limb_groups = {side: obj.vertex_groups.get('BowLimb.'+side) or
                       obj.vertex_groups.new(name='BowLimb.'+side)
                       for side in ('Top', 'Bottom')}
        to_rig = rig.matrix_world.inverted() @ obj.matrix_world
        for vertex in obj.data.vertices:
            original = next((g.weight for g in vertex.groups if g.group == weapon.index), 0)
            if original <= 0:
                continue
            offset = to_rig @ vertex.co - grip
            # All solid grip wraps and the shelf lie within 0.120m of grip Z.
            # Outside this band Weapon.L consists only of the two curved limbs
            # and their inlay. Keep side-mounted shelf/hand pieces rigid.
            if abs(offset.x) > .090 or abs(offset.z) <= .125:
                continue
            t = min(1, max(0, (abs(offset.z)-.125)/(.400-.125)))
            weight = t*t*t*(t*(t*6-15)+10)
            if weight <= 0:
                continue
            side = 'Top' if offset.z > 0 else 'Bottom'
            if weight >= 1:
                weapon.remove([vertex.index])
            else:
                weapon.add([vertex.index], original*(1-weight), 'REPLACE')
            limb_groups[side].add([vertex.index], original*weight, 'REPLACE')
            if obj == runtime:
                changed.append(vertex.index)
    assert changed, 'No bow limb vertices received flex weights'
    return {'changed_runtime_vertices': changed,
            'bone_names': ['BowLimb.Top', 'BowLimb.Bottom'],
            'parents': {'BowLimb.Top': 'Weapon.L', 'BowLimb.Bottom': 'Weapon.L',
                        'BowTip.Top': 'BowLimb.Top', 'BowTip.Bottom': 'BowLimb.Bottom'},
            'pivots_from_grip_RUF_source_m': {'Top': [0, .110, -.020], 'Bottom': [0, -.110, -.020]},
            'world_rotation_axis': 'Cross(bowUp, bowForward), independent of FBX local bone axes',
            'top_angle_sign': -1, 'bottom_angle_sign': 1,
            'recommended_full_draw_degrees': BOW_FLEX_DEGREES,
            'weight_transition_from_grip_abs_z_source_m': [.125, .400],
            'weight_curve': 'quintic smoothstep; Weapon.L remainder; tip-side region is fully limb weighted',
            'limit': 'Stylized graded skin; not a physical bow solver. Keep full draw within 0..25 degrees and verify the rendered string clearance.'}


def set_bow_flex(rig, degrees):
    # Source +X is the world right vector of the resting bow frame. Using matrix
    # space here mirrors the Unity world-axis contract without bone-roll guesses.
    for side, sign in (('Top', -1), ('Bottom', 1)):
        bone = rig.data.bones['BowLimb.'+side]
        pivot = bone.head_local
        rotation = Matrix.Rotation(math.radians(sign*degrees), 4, 'X')
        rig.pose.bones[bone.name].matrix = Matrix.Translation(pivot) @ rotation @ Matrix.Translation(-pivot) @ bone.matrix_local
    bpy.context.view_layer.update()


def validate_bow_flex(rig, mesh, flex):
    """Measure posed markers and skin, then restore the exact rest pose."""
    before = snapshot(rig, mesh)
    # A 0.33H draw and the shelf height mirror the current runtime relationship.
    # Coordinates stay in source meters, so the ratio is scale-independent.
    grip = rig.data.bones['BowGrip'].head_local
    nock = grip + Vector((-.065, .33*1.845+.015, .130))
    original_length = (rig.data.bones['BowTip.Top'].head_local-rig.data.bones['BowTip.Bottom'].head_local).length
    set_bow_flex(rig, BOW_FLEX_DEGREES)
    tips = {side: rig.pose.bones['BowTip.'+side].head.copy() for side in ('Top', 'Bottom')}
    length = sum((tips[side]-nock).length for side in tips)
    assert abs(length/original_length-1) < .02, 'Full-draw string changed length by over 2%'
    depsgraph = bpy.context.evaluated_depsgraph_get()
    evaluated = mesh.evaluated_get(depsgraph)
    geometry = evaluated.to_mesh()
    changed = set(flex['changed_runtime_vertices'])
    unchanged_error = max((geometry.vertices[v.index].co-v.co).length for v in mesh.data.vertices if v.index not in changed)
    assert unchanged_error < .00001, ('Flex deformed a hand, grip, shelf, or body vertex', unchanged_error)
    max_displacement = max((geometry.vertices[index].co-mesh.data.vertices[index].co).length for index in changed)
    evaluated.to_mesh_clear()
    set_bow_flex(rig, 0)
    after = snapshot(rig, mesh)
    assert before == after, 'Flex validation changed rest geometry'
    flex['validation'] = {'rest_string_source_m': original_length, 'full_draw_string_source_m': length,
                          'string_length_ratio': length/original_length,
                          'full_draw_tip_positions_blender': {side: list(p) for side,p in tips.items()},
                          'non_bow_vertex_max_error_source_m': unchanged_error,
                          'limb_max_vertex_displacement_source_m': max_displacement}


def arrow():
    # Blender -Y becomes Unity local +Z using the static baked FBX export below.
    # Ranged assembly contains no steel slot; create the same semantic steel
    # palette only for the new standalone forged arrowhead.
    if bpy.data.materials.get('SteelLight') is None:
        material = bpy.data.materials.new('SteelLight')
        material.use_nodes = True
        material.diffuse_color = (.3564, .4452, .4969, 1)
        shader = material.node_tree.nodes.get('Principled BSDF')
        shader.inputs['Base Color'].default_value = material.diffuse_color
        shader.inputs['Metallic'].default_value = .65
        shader.inputs['Roughness'].default_value = .35
    pieces = [tube('Ash arrow shaft', (0, -.025, 0), (0, -.912, 0), .008, 'Parchment')]
    pieces += [tube('Tail binding', (0, -.017, 0), (0, -.041, 0), .0105, 'Leather'),
               tube('Head socket', (0, -.876, 0), (0, -.922, 0), .011, 'Bronze')]
    # A faceted broadhead, maximum arrow width 55mm, tip exactly one meter ahead.
    verts = [(0, -1.0, 0), (-.0275, -.911, 0), (.0275, -.911, 0),
             (0, -.898, .009), (0, -.898, -.009)]
    pieces.append(build_mesh('Forged arrowhead', verts,
                             [(0, 1, 3), (0, 3, 2), (0, 2, 4), (0, 4, 1), (1, 4, 2, 3)], 'SteelLight'))
    for index in range(3):
        angle = math.tau * index / 3
        direction = Vector((math.cos(angle), 0, math.sin(angle)))
        # Three low-profile fletchings. Tail at y=0 remains the attachment origin.
        profiles = [(0, -.039), (.027, -.065), (.018, -.186), (0, -.199)]
        v = [tuple(direction*radius + Vector((0, y, 0))) for radius, y in profiles]
        normal = Vector((-math.sin(angle), 0, math.cos(angle))) * .0012
        vertices = [tuple(Vector(p)+normal*s) for s in (-1, 1) for p in v]
        faces = [(0, 3, 2, 1), (4, 5, 6, 7)] + [(i, (i+1)%4, (i+1)%4+4, i+4) for i in range(4)]
        pieces.append(build_mesh('Flight feather ' + str(index), vertices, faces, 'TeamCloth'))
    # Nock forks stop at the origin plane; string attaches at local (0,0,0).
    pieces += [box('Nock fork left', (-.006, -.010, 0), (.005, .020, .010), 'Bronze', .001),
               box('Nock fork right', (.006, -.010, 0), (.005, .020, .010), 'Bronze', .001)]
    select(pieces[0])
    for obj in pieces[1:]:
        obj.select_set(True)
    bpy.ops.object.join()
    obj = pieces[0]
    obj.name = 'Arrow'
    bpy.context.scene.cursor.location = (0, 0, 0)
    bpy.ops.object.origin_set(type='ORIGIN_CURSOR')
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    unwrap(obj)
    colors = obj.data.color_attributes.new(name='Color', type='BYTE_COLOR', domain='CORNER')
    for datum in colors.data:
        datum.color = (.98, .98, .98, 1)
    return obj


def export_model(path, objects, static_baked=False):
    bpy.ops.object.select_all(action='DESELECT')
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.export_scene.fbx(filepath=str(path), use_selection=True,
                            object_types={'MESH'} if static_baked else {'ARMATURE', 'MESH'},
                            use_mesh_modifiers=True, add_leaf_bones=False,
                            use_armature_deform_only=False, bake_anim=False,
                            axis_forward='-Z', axis_up='Y', apply_unit_scale=True,
                            bake_space_transform=static_baked,
                            mesh_smooth_type='FACE', path_mode='AUTO')


def inspect_arrow_fbx(path):
    # Inspect the serialized mesh coordinates, not the original Blender axis.
    # With axis baking, the exported mesh must itself point along positive Z.
    from io_scene_fbx import parse_fbx
    root, version = parse_fbx.parse(str(path))
    objects = next(child for child in root.elems if child.id == b'Objects')
    geometries = [child for child in objects.elems if child.id == b'Geometry']
    geometry = next(child for child in geometries if child.props[-1] == b'Mesh')
    raw = next(child for child in geometry.elems if child.id == b'Vertices').props[0]
    settings = next(child for child in root.elems if child.id == b'GlobalSettings')
    properties = next(child for child in settings.elems if child.id == b'Properties70')
    unit_cm = next(child.props[-1] for child in properties.elems
                   if child.id == b'P' and child.props[0] == b'UnitScaleFactor')
    meters_per_unit = unit_cm / 100
    positions = [[v * meters_per_unit for v in raw[i:i+3]] for i in range(0, len(raw), 3)]
    bounds = {'min': [min(v[i] for v in positions) for i in range(3)],
              'max': [max(v[i] for v in positions) for i in range(3)]}
    # Read the explicit FBX centimeter unit scale, rather than assuming whether
    # this Blender export stores raw coordinates in centimeters or meters.
    assert abs(bounds['min'][2]) < .00001 and abs(bounds['max'][2]-1) < .00001, bounds
    assert max(abs(bounds['min'][0]), abs(bounds['max'][0]), abs(bounds['min'][1]), abs(bounds['max'][1])) < .05
    return {'fbx_version': version, 'serialized_unit_centimeters': unit_cm,
            'serialized_mesh_bounds_meters': bounds,
            'forward_local': [0, 0, 1], 'tail_local': [0, 0, 0], 'tip_local': [0, 0, 1]}


def studio(stage, arrow_model, rig, samples):
    scene = bpy.context.scene
    scene.render.engine = 'CYCLES'
    scene.cycles.device = 'CPU'
    scene.cycles.samples = samples
    scene.cycles.use_denoising = True
    scene.world = bpy.data.worlds.new('Archer review world')
    scene.world.use_nodes = True
    scene.world.node_tree.nodes['Background'].inputs[0].default_value = (.075,.095,.13,1)
    scene.world.node_tree.nodes['Background'].inputs[1].default_value = .45
    for name, at, power, color, size in [
        ('Review key', (-3,-4,5), 650, (1,.88,.72), 4),
        ('Review fill', (3,-1,3), 400, (.65,.8,1), 3),
        ('Review rim', (0,3,4), 700, (.55,.8,1), 3),
    ]:
        data = bpy.data.lights.new(name, 'AREA')
        data.energy, data.color, data.size = power, color, size
        obj = bpy.data.objects.new(name, data)
        scene.collection.objects.link(obj)
        obj.location = at
        obj.rotation_euler = (Vector((0,0,1))-obj.location).to_track_quat('-Z','Y').to_euler()
    floor = box('STUDIO floor - not exported', (0,0,-.025), (200,200,.04), 'Shadow', 0)
    camera_data = bpy.data.cameras.new('Archer review camera')
    camera = bpy.data.objects.new('Archer review camera', camera_data)
    scene.collection.objects.link(camera)
    camera_data.type = 'ORTHO'
    camera_data.ortho_scale = 2.32
    scene.camera = camera
    scene.render.resolution_x, scene.render.resolution_y = 1200, 1100
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = 'PNG'
    scene.view_settings.view_transform = 'AgX'
    arrow_model.hide_render = True
    for name, at in [('three-quarter', (-3.4,-6,2.8)), ('side', (-6,0,1.6))]:
        camera.location = at
        camera.rotation_euler = (Vector((0,0,.94))-camera.location).to_track_quat('-Z','Y').to_euler()
        scene.render.filepath = str(stage/('archer-'+name+'.png'))
        bpy.ops.render.render(write_still=True)
    # Close side review of the new flexible upper/lower limbs. The actor is in
    # bind pose; this image proves deformation shape, not runtime hand clearance.
    set_bow_flex(rig, BOW_FLEX_DEGREES)
    camera.location = (-6, 0, 1.25)
    camera.rotation_euler = (Vector((-.46, -.12, .73))-camera.location).to_track_quat('-Z','Y').to_euler()
    camera_data.ortho_scale = 1.46
    scene.render.filepath = str(stage/'archer-bow-flex-side.png')
    bpy.ops.render.render(write_still=True)
    set_bow_flex(rig, 0)
    camera_data.ortho_scale = 2.32
    camera.location = (-3.4,-6,2.8)
    camera.rotation_euler = (Vector((0,0,.94))-camera.location).to_track_quat('-Z','Y').to_euler()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--root', required=True)
    parser.add_argument('--publish', action='store_true')
    parser.add_argument('--replace-generated', action='store_true')
    parser.add_argument('--samples', type=int, default=32)
    args = parser.parse_args(sys.argv[sys.argv.index('--')+1:])
    root = Path(args.root).resolve()
    source = root/'art/production/assets/MinionRefinement.blend'
    new_source = root/'art/production/assets/ArcherMotionRig.blend'
    runtime = root/'unity/Assets/Scripts/UI3D/Resources/UI3D/Minions'
    stage = root/'artifacts/archer-rig-v3-bow-flex'
    stage.mkdir(parents=True, exist_ok=True)
    if new_source.exists() and not args.replace_generated:
        raise RuntimeError('ArcherMotionRig.blend exists. Preserve hand edits; explicitly pass --replace-generated to regenerate.')
    protected = [source, root/'art/production/assets/Workbench.blend']
    protected += [runtime/(kind+'.fbx') for kind in ('Melee', 'Heavy')]
    if (runtime/'Arrow.fbx').exists():
        protected.append(runtime/'Arrow.fbx')
    protected += [runtime/(kind+'.fbx.meta') for kind in ('Melee', 'Ranged', 'Heavy', 'Arrow')]
    hashes = {str(p.relative_to(root)): hash_file(p) for p in protected}
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.preferences.filepaths.save_version = 0
    with bpy.data.libraries.load(str(source), link=False) as (data_from, data_to):
        data_to.collections = ['Ranged refined editable assembly']
    collection = data_to.collections[0]
    bpy.context.scene.collection.children.link(collection)
    rig = next(o for o in collection.objects if o.type == 'ARMATURE')
    runtime_mesh = next(o for o in collection.objects if o.name == 'Ranged Runtime Mesh')
    rig.location = (0, 0, 0)
    runtime_mesh.hide_set(False)
    runtime_mesh.hide_render = False
    baseline = snapshot(rig, runtime_mesh)
    arm_records, intentional_bones, sockets, left_translation = lengthen_arms(rig, runtime_mesh, collection)
    add_markers(rig, sockets)
    runtime_mesh = add_shelf(rig, runtime_mesh, left_translation)
    bow_flex = add_bow_flex(rig, runtime_mesh, collection)
    validate_bow_flex(rig, runtime_mesh, bow_flex)
    current = snapshot(rig, runtime_mesh)
    for name, contract in baseline['bones'].items():
        if name not in intentional_bones:
            assert current['bones'][name] == contract, 'Unrelated bone changed: ' + name
        expected_parent = bow_flex['parents'].get(name, contract['parent'])
        assert current['bones'][name]['parent'] == expected_parent, 'Unexpected semantic parenting change'
    count = len(baseline['vertices'])
    flex_indices = set(bow_flex['changed_runtime_vertices'])
    for index in range(count):
        if index not in flex_indices:
            assert current['weights'][index] == baseline['weights'][index], 'Non-bow skin weights changed'
        else:
            assert baseline['weights'][index] == [('Weapon.L', 1.0)], 'Flex changed a non-bow group'
            assert abs(sum(w for _,w in current['weights'][index])-1) < .000001, 'Flex weights not normalized'
    moved_groups = {'UpperArm.L','UpperArm.R','LowerArm.L','LowerArm.R','Hand.L','Hand.R','Weapon.L','Weapon.R'}
    for index, vertex in enumerate(baseline['vertices']):
        if baseline['weights'][index][0][0] not in moved_groups:
            assert current['vertices'][index] == vertex, 'Unrelated body vertex moved'
    for end in ('min', 'max'):
        assert abs(baseline[end][2]-current[end][2]) < .000001, 'Overall height/ground anchor changed'
    for name in ('BowTip.Top','BowTip.Bottom'):
        old_relative = Vector(baseline['bones'][name]['head'])-Vector(baseline['bones']['BowGrip']['head'])
        new_relative = Vector(current['bones'][name]['head'])-Vector(current['bones']['BowGrip']['head'])
        assert (old_relative-new_relative).length < .000001, 'Bow dimensions changed'
    assert current['bones']['BowTip.Bottom']['head'][2] > .01, 'Bow tip touches ground'
    source_height = current['max'][2]-current['min'][2]
    for record in arm_records.values():
        record['runtime_reach_at_height_2_016'] = (record['upper_length']+record['lower_length'])*2.016/source_height
        assert .57 <= record['runtime_reach_at_height_2_016'] <= .59
    triangles = sum(len(face.vertices)-2 for face in runtime_mesh.data.polygons)
    assert triangles <= 30000
    ranged_stage = stage/'Ranged.fbx'
    export_model(ranged_stage, [rig, runtime_mesh])
    arrow_model = arrow()
    arrow_stage = stage/'Arrow.fbx'
    export_model(arrow_stage, [arrow_model], static_baked=True)
    arrow_info = inspect_arrow_fbx(arrow_stage)
    arrow_info['triangles'] = sum(len(p.vertices)-2 for p in arrow_model.data.polygons)
    arrow_info['material_slots'] = [m.name for m in arrow_model.data.materials]
    arrow_model.location = (1, 0, .1)  # Separate editable asset for source inspection, after export.
    studio(stage, arrow_model, rig, args.samples)
    bpy.context.scene['README'] = 'Ranged-only patch v3: UpperArm length x1.15, LowerArm x1.60. BowLimb.Top/Bottom graded skin bends only the limbs; BowTip markers follow the controls with unchanged bind coordinates. Runtime full draw suggestion: 22.8 degrees, world axis Cross(bowUp,bowForward), Top negative and Bottom positive. Hands, grip, shelf and every unrelated skin weight unchanged. Run AFTER refine_minion_models.py. Base source remains read-only. BowRest is shaft centre. Arrow local +Z, tail zero. Studio objects excluded from FBX. See manifest for measured string ratio and non-bow skin checks.'
    source_stage = stage/'ArcherMotionRig.blend'
    bpy.ops.wm.save_as_mainfile(filepath=str(source_stage), compress=True)
    for path in protected:
        assert hashes[str(path.relative_to(root))] == hash_file(path), 'Protected file changed: ' + str(path)
    report = {'generator': 'tools/art/refine_archer_rig.py', 'version': 3,
              'run_after': 'tools/art/refine_minion_models.py',
              'base_source_sha256': hash_file(source), 'published': args.publish,
              'non_deform_markers': {n: {'head_blender': p, 'parent': parent} for n,(p,parent) in sockets.items()},
              'original_bone_count': len(baseline['bones']), 'bone_count': len(current['bones']),
              'arm_length_factors': {'UpperArm': UPPER_ARM_FACTOR, 'LowerArm': LOWER_ARM_FACTOR},
              'intentional_rest_changes': sorted(intentional_bones), 'arm_contract': arm_records,
              'original_vertex_order_unchanged': True,
              'non_bow_original_weights_unchanged': True,
              'bow_flex': {**{k:v for k,v in bow_flex.items() if k != 'changed_runtime_vertices'},
                           'changed_runtime_vertex_count': len(bow_flex['changed_runtime_vertices'])},
              'unrelated_bones_body_and_quiver_unchanged': True,
              'hands_grip_and_shelf_rigid_translation_only': True,
              'bow_grip_tip_offsets_unchanged': True, 'height_and_ground_anchor_unchanged': True,
              'bounds_before_blender': {end: baseline[end] for end in ('min', 'max')},
              'bounds_blender': {end: current[end] for end in ('min', 'max')},
              'ranged_triangles': triangles, 'arrow': arrow_info, 'protected_files_sha256': hashes,
              'files': {'Ranged.fbx': hash_file(ranged_stage),
                        'Arrow.fbx': hash_file(runtime/'Arrow.fbx') or hash_file(arrow_stage),
                        'ArcherMotionRig.blend': hash_file(source_stage)},
              'validation_boundary': 'Blender geometry/export contracts. Unity import, socket preservation and real animation clearances require subsequent runtime validation.'}
    if args.publish:
        backup = stage/'previous-runtime'
        backup.mkdir(exist_ok=True)
        for path in (runtime/'Ranged.fbx', runtime/'Arrow.fbx'):
            if path.exists() and not (backup/path.name).exists():
                shutil.copy2(path, backup/path.name)
        shutil.copy2(ranged_stage, runtime/'Ranged.fbx')
        if not (runtime/'Arrow.fbx').exists():
            shutil.copy2(arrow_stage, runtime/'Arrow.fbx')
        shutil.copy2(source_stage, new_source)
        target = root/'art/minions/archer-rig-manifest.json'
        target.write_text(json.dumps(report, indent=2)+'\n', encoding='utf8')
    (stage/'archer-rig-manifest.json').write_text(json.dumps(report, indent=2)+'\n', encoding='utf8')
    print('ARCHER_RIG_PATCH_DONE '+json.dumps({'published': args.publish, 'bones': len(current['bones']),
                                            'ranged_triangles': triangles, 'arrow': arrow_info}))


if __name__ == '__main__':
    main()
