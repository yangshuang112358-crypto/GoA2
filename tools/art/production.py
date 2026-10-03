"""Run inside pinned Blender. Creates an isolated studio, never edits shipped assets."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import sys

import bpy
from mathutils import Vector


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def studio(path):
    if path.exists():
        raise RuntimeError("Template exists; refusing to overwrite artist work")
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.unit_settings.system = "METRIC"
    scene.unit_settings.scale_length = 1.0
    scene.render.fps = 30
    scene.frame_start, scene.frame_end = 1, 60
    for name in ("EXPORT", "SOURCE", "RIGS", "FX_GUIDES", "STUDIO"):
        scene.collection.children.link(bpy.data.collections.new(name))
    palette = {"Stone": (.15, .19, .23, 1), "Bronze": (.34, .20, .07, 1),
               "TeamRed": (.42, .045, .06, 1), "TeamBlue": (.025, .15, .42, 1)}
    for name, color in palette.items():
        mat = bpy.data.materials.new(name)
        mat.use_nodes = True
        mat.diffuse_color = color
        shader = mat.node_tree.nodes.get("Principled BSDF")
        shader.inputs["Base Color"].default_value = color
        shader.inputs["Roughness"].default_value = .6
        shader.inputs["Metallic"].default_value = .65 if name == "Bronze" else 0
        mat.use_fake_user = True
    bpy.ops.mesh.primitive_cube_add(size=1, location=(0, 0, .5))
    cube = bpy.context.object
    cube.name = "Calibration_1m_REPLACE_BEFORE_PRODUCTION"
    for collection in list(cube.users_collection):
        collection.objects.unlink(cube)
    bpy.data.collections["EXPORT"].objects.link(cube)
    cube.data.materials.append(bpy.data.materials["Stone"])
    cube["purpose"] = "One metre pipeline calibration; not a final game model"
    # Studio elements must never be part of the runtime export.
    stage = bpy.data.collections["STUDIO"]
    for name, location, energy, color in (
            ("Key", (-3, -4, 5), 500, (1, .85, .7)),
            ("Fill", (4, -1, 3), 250, (.65, .8, 1)),
            ("Rim", (0, 3, 4), 400, (.65, .8, 1))):
        light = bpy.data.lights.new(name, "AREA")
        light.energy, light.color, light.size = energy, color, 3
        obj = bpy.data.objects.new(name, light)
        stage.objects.link(obj)
        obj.location = location
        obj.rotation_euler = (Vector((0, 0, .5)) - obj.location).to_track_quat("-Z", "Y").to_euler()
    data = bpy.data.cameras.new("BoardReadabilityCamera")
    camera = bpy.data.objects.new(data.name, data)
    stage.objects.link(camera)
    camera.location = (2.5, -4, 3.5)
    camera.rotation_euler = (Vector((0, 0, .5)) - camera.location).to_track_quat("-Z", "Y").to_euler()
    data.type, data.ortho_scale = "ORTHO", 3
    scene.camera = camera
    scene.world = bpy.data.worlds.new("NeutralWorld")
    scene.world.color = (.08, .08, .08)
    scene.render.engine = "CYCLES"
    scene.cycles.device, scene.cycles.samples = "CPU", 16
    scene.render.resolution_x, scene.render.resolution_y = 640, 360
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.view_settings.view_transform = "AgX"
    scene["goa_pipeline"] = "v1: metres, 30fps, EXPORT collection only, no runtime root motion"
    path.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=str(path))


def export_asset(destination):
    scene = bpy.context.scene
    if scene.unit_settings.system != "METRIC" or abs(scene.unit_settings.scale_length - 1) > .0001:
        raise RuntimeError("Use metric units with scale 1 before export")
    collection = bpy.data.collections.get("EXPORT")
    if not collection:
        raise RuntimeError("Create an EXPORT collection containing only runtime meshes/rigs")
    objects = [o for o in collection.all_objects if o.type in {"MESH", "ARMATURE"}]
    if not objects or not any(o.type == "MESH" for o in objects):
        raise RuntimeError("No runtime mesh in EXPORT")
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        if obj.hide_get() or obj.hide_viewport:
            raise RuntimeError("Export objects must be visible: " + obj.name)
        if any(abs(value - 1) > .0001 for value in obj.scale):
            raise RuntimeError("Apply scale before export: " + obj.name)
        if any(abs(value) > .0001 for value in obj.rotation_euler):
            raise RuntimeError("Apply object rotation before export: " + obj.name)
        for modifier in obj.modifiers:
            if modifier.type == "ARMATURE" and modifier.object not in objects:
                raise RuntimeError("Skinned mesh requires its armature in EXPORT: " + obj.name)
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.export_scene.fbx(filepath=str(destination), use_selection=True,
        object_types={"MESH", "ARMATURE"}, add_leaf_bones=False,
        axis_forward="-Z", axis_up="Y", apply_unit_scale=True,
        bake_anim=False, use_mesh_modifiers=True, mesh_smooth_type="FACE",
        path_mode="AUTO", embed_textures=False)
    triangles = 0
    for obj in objects:
        if obj.type == "MESH":
            evaluated = obj.evaluated_get(bpy.context.evaluated_depsgraph_get())
            mesh = evaluated.to_mesh()
            try:
                mesh.calc_loop_triangles()
                triangles += len(mesh.loop_triangles)
            finally:
                evaluated.to_mesh_clear()
    return {"objects": [o.name for o in objects], "triangles": triangles,
            "animation_exported": False, "sha256": digest(destination)}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", required=True, type=Path)
    parser.add_argument("--mode", choices=("init", "check", "export"), required=True)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--asset", type=Path)
    args = parser.parse_args(sys.argv[sys.argv.index("--")+1:])
    if bpy.app.version[:3] != (4, 5, 14):
        raise RuntimeError("Production baseline is Blender 4.5.14; validate upgrades separately")
    root, output = args.root.resolve(), args.output.resolve()
    config = os.environ.get("BLENDER_USER_CONFIG", "")
    if not config or Path(config).resolve() != root / "artifacts/blender-user-config":
        raise RuntimeError("Use tools/art/blender.ps1 to isolate project preferences")
    if not output.is_relative_to(root / "artifacts"):
        raise RuntimeError("Staged exports and reports must stay under project artifacts")
    output.mkdir(parents=True, exist_ok=False)
    template = root / "art/production/templates/Goa2_Studio.blend"
    if args.mode == "init" and not template.exists():
        studio(template)
    if args.mode == "init":
        prefs = bpy.context.preferences.filepaths
        prefs.use_auto_save_temporary_files = True
        prefs.auto_save_time, prefs.save_version = 2, 3
        bpy.ops.wm.save_userpref()
    asset = args.asset.resolve() if args.asset else template
    if not asset.is_file():
        raise RuntimeError("Asset does not exist")
    original = digest(asset)
    bpy.ops.wm.open_mainfile(filepath=str(asset))
    report = {"blender": bpy.app.version_string, "source_sha256": original,
              "mode": args.mode, "unit_scale": bpy.context.scene.unit_settings.scale_length,
              "fps": bpy.context.scene.render.fps, "python": sys.version.split()[0]}
    bpy.ops.preferences.addon_enable(module="io_scene_fbx")
    report["fbx"] = export_asset(output / "asset.fbx")
    if args.mode in {"init", "check"}:
        # CPU keeps the environment smoke check independent of the unresolved GPU hang.
        bpy.context.scene.cycles.device = "CPU"
        bpy.context.scene.render.filepath = str(output / "preview.png")
        bpy.ops.render.render(write_still=True)
        bpy.ops.wm.read_factory_settings(use_empty=True)
        bpy.ops.import_scene.fbx(filepath=str(output / "asset.fbx"))
        meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
        report["roundtrip_meshes"] = len(meshes)
        report["roundtrip_dimensions"] = [list(o.dimensions) for o in meshes]
        if asset == template:
            assert len(meshes) == 1 and all(abs(v-1) < .001 for v in meshes[0].dimensions)
        assert not any(o.type in {"CAMERA", "LIGHT"} for o in bpy.context.scene.objects)
    assert digest(asset) == original, "Source asset was unexpectedly changed"
    report["passed"] = True
    (output / "report.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    print("GOA_PRODUCTION_READY " + str(output / "report.json"))


if __name__ == "__main__":
    main()
