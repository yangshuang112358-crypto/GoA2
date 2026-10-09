"""Build the isolated R2B01 frame-parts source and two optional still renders.

Blender 4.5.14 --background --python tools/art/build_r2b_frame_parts.py --
    --root REPOSITORY --out NEW_ARTIFACT_DIRECTORY

This standalone generator does not import the component-sample generator or
open any existing .blend.  It owns only R2B01_FrameParts.blend and its named
render/report outputs.  All visible parts are mesh/curve geometry; there are
no image planes.  Recesses remain editable EXACT Boolean modifiers with their
hidden cutter objects preserved in each scene's MODELING_TOOLS collection.
"""

import argparse
import json
import math
from pathlib import Path
import sys

import bpy
from mathutils import Vector


TAU = math.tau
NAME_OUTLINE = [(-.85, .215), (.85, .215), (.95, .095),
                (.80, -.215), (-.80, -.215), (-.95, .095)]


def material(name, dark, light, metal=0, rough=.6, micro_bump=.013):
    """Same stone/bronze values as the sample, independently declared here."""
    mat = bpy.data.materials.new("R2B_FP / " + name)
    mat.use_nodes = True
    mat.diffuse_color = (*light, 1)
    nodes, links = mat.node_tree.nodes, mat.node_tree.links
    bsdf = nodes.get("Principled BSDF")
    bsdf.inputs["Metallic"].default_value = metal
    bsdf.inputs["Roughness"].default_value = rough
    noise = nodes.new("ShaderNodeTexNoise")
    noise.inputs["Scale"].default_value = 6
    noise.inputs["Detail"].default_value = 4
    ramp = nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.elements[0].position = .18
    ramp.color_ramp.elements[0].color = (*dark, 1)
    ramp.color_ramp.elements[1].position = .84
    ramp.color_ramp.elements[1].color = (*light, 1)
    links.new(noise.outputs["Fac"], ramp.inputs[0])
    links.new(ramp.outputs["Color"], bsdf.inputs["Base Color"])
    micro = nodes.new("ShaderNodeTexNoise")
    micro.inputs["Scale"].default_value = 175
    micro.inputs["Detail"].default_value = 2
    bump = nodes.new("ShaderNodeBump")
    bump.inputs["Strength"].default_value = .33
    bump.inputs["Distance"].default_value = micro_bump
    links.new(micro.outputs["Fac"], bump.inputs["Height"])
    links.new(bump.outputs["Normal"], bsdf.inputs["Normal"])
    return mat


def palette():
    return {
        "stone": material("graphite slate", (.016, .020, .026), (.069, .077, .087), 0, .76),
        "name": material("inscription slate", (.033, .038, .046), (.14, .15, .17), 0, .75),
        "red": material("red card stone", (.045, .009, .006), (.19, .035, .020), 0, .73),
        "blue": material("blue card stone", (.006, .022, .04), (.028, .080, .15), 0, .73),
        "green": material("green card stone", (.008, .025, .012), (.033, .12, .055), 0, .73),
        "gold": material("gold card stone", (.087, .052, .011), (.29, .205, .046), .12, .59),
        "silver": material("silver card stone", (.085, .095, .11), (.30, .33, .37), .22, .56),
        "bronze": material("aged bronze", (.028, .018, .009), (.24, .14, .058), .78, .41),
        "edge": material("bronze polished edge", (.16, .079, .021), (.41, .26, .10), .80, .32),
        "dark_metal": material("blackened steel", (.012, .018, .024), (.063, .079, .099), .62, .40),
        "ink": material("studio ivory", (.32, .33, .35), (.59, .60, .63), .2, .55, .002),
        "backdrop": material("charcoal stage", (.004, .006, .009), (.016, .024, .029), 0, .9),
    }


def bevel(obj, width, name="Dressed edges", segments=3):
    mod = obj.modifiers.new("R2B_FP " + name, "BEVEL")
    mod.width = width
    mod.segments = segments
    mod.limit_method = "ANGLE"
    return mod


class Builder:
    def __init__(self, scene, mats, font):
        self.scene, self.mats, self.font = scene, mats, font
        self.parts = bpy.data.collections.new(scene.name + " / PARTS")
        self.tools = bpy.data.collections.new(scene.name + " / MODELING_TOOLS")
        self.studio = bpy.data.collections.new(scene.name + " / STUDIO")
        for collection in (self.parts, self.tools, self.studio):
            scene.collection.children.link(collection)
        self.cuts = []

    def empty(self, name, location=(0, 0, 0)):
        obj = bpy.data.objects.new("R2B_FP " + name, None)
        self.parts.objects.link(obj)
        obj.location = location
        obj.empty_display_type = "PLAIN_AXES"
        obj.empty_display_size = .18
        return obj

    def mesh(self, name, verts, faces, mat, parent=None, edge=0):
        data = bpy.data.meshes.new("R2B_FP " + name + " Mesh")
        data.from_pydata(verts, [], faces)
        data.update()
        data.materials.append(self.mats[mat])
        obj = bpy.data.objects.new("R2B_FP " + name, data)
        self.parts.objects.link(obj)
        if parent is not None:
            obj.parent = parent
        if edge:
            bevel(obj, edge)
        return obj

    def polygon(self, name, points, low, high, mat, parent=None, edge=0):
        pts = list(points)
        area = sum(pts[i][0] * pts[(i + 1) % len(pts)][1] -
                   pts[(i + 1) % len(pts)][0] * pts[i][1] for i in range(len(pts)))
        if area < 0:
            pts.reverse()
        n = len(pts)
        faces = [tuple(reversed(range(n))), tuple(range(n, 2 * n))]
        faces += [(i, (i + 1) % n, (i + 1) % n + n, i + n) for i in range(n)]
        return self.mesh(name, [(x, y, z) for z in (low, high) for x, y in pts],
                         faces, mat, parent, edge)

    def disc(self, name, radius, low, high, mat, parent=None, edge=.018):
        points = [(radius * math.cos(i * TAU / 128), radius * math.sin(i * TAU / 128))
                  for i in range(128)]
        return self.polygon(name, points, low, high, mat, parent, edge)

    def annulus(self, name, outer, inner, low, high, mat, parent=None, edge=.01):
        n = 160
        verts = [(r * math.cos(i * TAU / n), r * math.sin(i * TAU / n), z)
                 for r, z in ((outer, low), (outer, high), (inner, high), (inner, low))
                 for i in range(n)]
        faces = [(k * n + i, k * n + (i + 1) % n,
                  ((k + 1) % 4) * n + (i + 1) % n, ((k + 1) % 4) * n + i)
                 for k in range(4) for i in range(n)]
        return self.mesh(name, verts, faces, mat, parent, edge)

    def recess(self, body, cutter, feature, depth):
        mod = body.modifiers.new("R2B_FP TRUE RECESS / " + feature, "BOOLEAN")
        mod.operation = "DIFFERENCE"
        mod.solver = "EXACT"
        mod.object = cutter
        # Retain a manipulable tool.  Hiding the operand does not remove it from
        # the modifier dependency graph, so the saved source remains editable.
        for collection in list(cutter.users_collection):
            collection.objects.unlink(cutter)
        self.tools.objects.link(cutter)
        cutter.hide_render = True
        cutter.display_type = "WIRE"
        cutter.hide_set(True)
        cutter["modeling_tool_only"] = True
        body["actual_recess"] = True
        self.cuts.append({"body": body.name, "cutter": cutter.name,
                          "feature": feature, "depth": depth,
                          "operation": "DIFFERENCE", "solver": "EXACT"})

    def caption(self, name, text, xyz, height=.18, max_width=None):
        data = bpy.data.curves.new("R2B_FP Label " + name, "FONT")
        data.body = text
        data.font = self.font
        data.align_x = "CENTER"
        data.align_y = "CENTER"
        data.size = 1
        data.extrude = .0008
        data.resolution_u = 8
        obj = bpy.data.objects.new("R2B_FP Studio label " + name, data)
        self.studio.objects.link(obj)
        data.materials.append(self.mats["ink"])
        bpy.context.view_layer.update()
        scale = height / max(obj.dimensions.y, .001)
        if max_width:
            scale = min(scale, max_width / max(obj.dimensions.x, .001))
        obj.scale = (scale, scale, 1)
        obj.location = xyz
        # No font dependency in the delivered source: captions are real mesh.
        bpy.ops.object.select_all(action="DESELECT")
        obj.select_set(True)
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.convert(target="MESH")
        obj.select_set(False)
        return obj

    def front_frame(self, name, location=(0, 0, 0)):
        root = self.empty(name, location)
        root["component_role"] = "Blank front housing; separate card ring and name insert"
        self.annulus("Frame structural shoulder", 1.51, 1.235, -.18, .055,
                     "dark_metal", root, .022)
        self.annulus("Card band seating channel", 1.49, 1.24, -.08, .078,
                     "bronze", root, .012)
        self.annulus("Outer bronze retention rim", 1.49, 1.425, .04, .158,
                     "bronze", root, .012)
        self.annulus("Polished outer edge", 1.49, 1.455, .133, .163,
                     "edge", root, .006)
        self.annulus("Inner artwork retention lip", 1.28, 1.24, .035, .115,
                     "bronze", root, .009)
        self.disc("Blank deep artwork well", 1.235, -.105, -.018,
                  "stone", root, .015)
        for i in range(4):
            a = math.pi / 4 + i * math.pi / 2
            pin = self.disc("Retention pin %02d" % (i + 1), .032, .159, .178,
                            "edge", root, .007)
            pin.location.x, pin.location.y = 1.443 * math.cos(a), 1.443 * math.sin(a)
        return root

    def card_ring(self, name, color="red", location=(0, 0, 0)):
        root = self.empty(name, location)
        ring = self.annulus("Replaceable " + color + " stone inlay", 1.420, 1.286,
                            0, .070, color, root, .009)
        ring["component_role"] = "Card colour only; independent of numeric buff/debuff material"
        ring["assembled_base_z"] = .082
        return root

    def name_slot(self, name, location=(0, 0, 0), explode_insert=0):
        root = self.empty(name, location)
        root["component_role"] = "Reusable nameplate pocket and removable blank stone insert"
        outer = [(x * 1.075, y * 1.22) for x, y in NAME_OUTLINE]
        pocket = self.polygon("Nameplate machined socket", outer, 0, .16,
                              "bronze", root, .014)
        cutter = self.polygon("CUT nameplate shallow pocket", NAME_OUTLINE,
                              .040, .30, "dark_metal", root, .009)
        self.recess(pocket, cutter, "nameplate pocket", .12)
        bevel(pocket, .003, "Pocket edge light", 2)
        insert_pts = [(x * .966, y * .94) for x, y in NAME_OUTLINE]
        insert = self.polygon("Blank name stone insert", insert_pts, .052, .136,
                              "name", root, .018)
        insert.location.z = explode_insert
        insert["blank_replaceable_insert"] = True
        insert["assembled_top_below_socket_lip"] = .024
        return root

    def back_plate(self, name, location=(0, 0, 0)):
        root = self.empty(name, location)
        root["component_role"] = "Metal discard back; true sunken X, shown rear face upward"
        body = self.disc("Metal backplate with recessed X", 1.465, -.12, .10,
                         "dark_metal", root, .026)
        # Two intersecting rounded bars form one continuous X-shaped cavity.
        # Their bottoms remain at .000, leaving .120 of solid metal underneath.
        bar = [(-1.12, -.145), (1.12, -.145), (1.12, .145), (-1.12, .145)]
        for i, angle in enumerate((math.pi / 4, -math.pi / 4)):
            points = [(x * math.cos(angle) - y * math.sin(angle),
                       x * math.sin(angle) + y * math.cos(angle)) for x, y in bar]
            cutter = self.polygon("CUT discard X bar %02d" % (i + 1), points,
                                  0, .30, "bronze", root, .018)
            self.recess(body, cutter, "discard X bar %02d" % (i + 1), .10)
        bevel(body, .006, "Recess mouth chamfer", 3)
        body["cavity_floor_z"] = 0.0
        body["cavity_surface_z"] = .10
        body["remaining_solid_thickness"] = .12
        self.annulus("Back aged bronze perimeter", 1.505, 1.405, -.085, .130,
                     "bronze", root, .016)
        self.annulus("Back narrow edge highlight", 1.496, 1.473, .113, .137,
                     "edge", root, .004)
        self.annulus("Back inset turned groove", 1.337, 1.318, .084, .091,
                     "bronze", root, .003)
        return root

    def studio_setup(self, ortho, target=(0, 0, 0), tilt=.14):
        camera = bpy.data.objects.new("R2B_FP Camera " + self.scene.name,
                                      bpy.data.cameras.new("R2B_FP CameraData " + self.scene.name))
        self.studio.objects.link(camera)
        camera.location = (target[0], target[1] - tilt * 14, target[2] + 14)
        camera.rotation_euler = (Vector(target) - camera.location).to_track_quat("-Z", "Y").to_euler()
        camera.data.type = "ORTHO"
        camera.data.ortho_scale = ortho
        self.scene.camera = camera
        for name, loc, energy, size, color in (
                ("Warm key", (-4, 5, 7), 950, 3.0, (1, .83, .64)),
                ("Cool fill", (4, 1, 5), 250, 4.0, (.57, .72, 1)),
                ("Copper edge", (1, 5, 4), 330, 3.0, (1, .95, .82))):
            data = bpy.data.lights.new("R2B_FP " + name, "AREA")
            data.energy, data.size, data.color = energy, size, color
            data.shape = "DISK"
            lamp = bpy.data.objects.new(data.name, data)
            self.studio.objects.link(lamp)
            lamp.location = loc
            lamp.rotation_euler = (Vector(target) - lamp.location).to_track_quat("-Z", "Y").to_euler()
        backdrop = self.polygon("Studio backdrop", [(-30, -30), (30, -30), (30, 30), (-30, 30)],
                                -1.9, -1.8, "backdrop")
        self.parts.objects.unlink(backdrop)
        self.studio.objects.link(backdrop)


def new_scene(name):
    scene = bpy.data.scenes.new(name)
    bpy.context.window.scene = scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 48
    scene.cycles.use_denoising = True
    scene.render.resolution_x = 1280
    scene.render.resolution_y = 1000
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    scene.render.film_transparent = False
    scene.view_settings.view_transform = "AgX"
    scene.world = bpy.data.worlds.new("R2B_FP World " + name)
    scene.world.use_nodes = True
    scene.world.node_tree.nodes["Background"].inputs[0].default_value = (.08, .105, .14, 1)
    scene.world.node_tree.nodes["Background"].inputs[1].default_value = .16
    return scene


def build_scenes(mats, font):
    builders = []
    b = Builder(new_scene("01_FRAME_PARTS_OVERVIEW"), mats, font)
    builders.append(b)
    b.front_frame("A / Blank front frame", (-2.55, 1.55, 0))
    b.card_ring("B / Replaceable red card stone ring", "red", (2.55, 1.55, .07))
    b.name_slot("C / Nameplate pocket and separate insert", (-2.55, -1.92, .03), .28)
    b.back_plate("D / Discard metal reverse", (2.55, -1.92, 0))
    b.caption("Title", "R2B 01 · 独立框体部件", (0, 3.80, .04), .27)
    b.caption("Front", "空白正面框 · 预留图标井", (-2.55, -.21, .02), .19, 3.85)
    b.caption("Ring", "可替换牌色嵌石环", (2.55, -.21, .02), .19, 3.85)
    b.caption("Pocket", "名牌插槽／空白石插片", (-2.55, -3.64, .02), .19, 3.85)
    b.caption("Back", "金属弃置背面 · 大叉内凹", (2.55, -3.64, .02), .19, 3.85)
    # Small palette rings are isolated meshes, not a texture strip.
    for i, color in enumerate(("gold", "silver", "red", "green", "blue")):
        sample = b.card_ring("Card inlay palette / " + color, color,
                            (-2.55 + (i - 2) * .59, -2.88, .015))
        sample.scale = (.145,) * 3
    b.studio_setup(10.7, (0, .10, .02), .12)

    b = Builder(new_scene("02_SHALLOW_EXPLODED_ASSEMBLY"), mats, font)
    builders.append(b)
    # Local assembly units match the main sample's radius and seats.  Only the
    # display offsets explode them; component meshes themselves remain intact.
    center_x = -1.80
    back = b.back_plate("Exploded 01 / rear metal cover", (center_x, -.36, -.36))
    back.rotation_euler.y = math.pi
    back["assembly_note"] = "Rear X faces -Z when installed; separate right-hand sample exposes it"
    b.front_frame("Exploded 02 / front housing", (center_x, -.13, .22))
    b.card_ring("Exploded 03 / card inlay", "red", (center_x, .08, .94))
    b.name_slot("Exploded 04 / nameplate socket", (center_x, -.67, 1.27), .17)
    b.back_plate("Rear-face close view / true X groove", (2.40, .10, .16))
    b.caption("Exploded title", "浅分层装配 · 各部件可独立编辑", (0, 3.02, .04), .26, 8.5)
    b.caption("Stack", "背盖 → 框体 → 嵌石环 → 名牌插槽", (-1.80, -2.44, .03), .165, 4.6)
    b.caption("Rear", "真实布尔凹叉／保留底部金属", (2.40, -2.10, .03), .17, 3.6)
    b.caption("Scope", "独立 Blender 模型样本 · 尚未接入游戏", (0, -3.27, .03), .17, 8.3)
    b.studio_setup(10.55, (0, -.02, .60), .40)
    return builders


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, required=True)
    parser.add_argument("--out", type=Path, required=True)
    parser.add_argument("--replace-generated", action="store_true",
                        help="Allow replacing only this generator's FrameParts source/output files")
    parser.add_argument("--no-render", action="store_true", help="Save editable source and report only")
    tail = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    args = parser.parse_args(tail)
    root, out = args.root.resolve(), args.out.resolve()
    if not root.is_dir():
        raise RuntimeError("Repository root does not exist: " + str(root))
    if tuple(bpy.app.version) != (4, 5, 14):
        raise RuntimeError("Use Blender 4.5.14 LTS; found " + bpy.app.version_string)
    source = root / "art/production/samples/R2B01/R2B01_FrameParts.blend"
    outputs = [out / "R2B01_FrameParts_ModelReport.json",
               out / "01_FRAME_PARTS_OVERVIEW.png", out / "02_SHALLOW_EXPLODED_ASSEMBLY.png"]
    occupied = [p for p in (source, *outputs) if p.exists()]
    if occupied and not args.replace_generated:
        raise RuntimeError("Owned outputs already exist; inspect them before --replace-generated: " +
                           ", ".join(str(p) for p in occupied))
    font_path = Path("C:/Windows/Fonts/msyhbd.ttc")
    if not font_path.is_file():
        raise RuntimeError("The sample's existing Chinese font is unavailable: " + str(font_path))

    # This script is intended for a new background Blender process.  It never
    # opens ComponentSamples.blend, Workbench.blend, or any production source.
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.preferences.filepaths.save_version = 0
    font = bpy.data.fonts.load(str(font_path))
    builders = build_scenes(palette(), font)
    out.mkdir(parents=True, exist_ok=True)
    source.parent.mkdir(parents=True, exist_ok=True)
    report = {"source": str(source.relative_to(root)), "blender": bpy.app.version_string,
              "renderer": "Cycles CPU", "samples": 48, "resolution": [1280, 1000],
              "external_image_textures": len(bpy.data.images), "runtime_changed": False,
              "source_isolated_from": ["R2B01_ComponentSamples.blend", "Workbench.blend"],
              "scenes": {}, "note": "Topology report is not visual acceptance."}
    for b in builders:
        bpy.context.window.scene = b.scene
        bpy.context.view_layer.update()
        depsgraph = bpy.context.evaluated_depsgraph_get()
        geometry = []
        for obj in b.parts.objects:
            if obj.type != "MESH":
                continue
            evaluated = obj.evaluated_get(depsgraph)
            mesh = evaluated.to_mesh()
            geometry.append({"object": obj.name, "base_vertices": len(obj.data.vertices),
                             "evaluated_vertices": len(mesh.vertices),
                             "evaluated_faces": len(mesh.polygons),
                             "actual_boolean_modifiers": sum(m.type == "BOOLEAN" for m in obj.modifiers)})
            evaluated.to_mesh_clear()
        b.scene.render.filepath = str(out / (b.scene.name + ".png"))
        report["scenes"][b.scene.name] = {"geometry": geometry, "editable_boolean_recesses": b.cuts}
    notes = bpy.data.texts.new("R2B_FP READ ME / Scope and editable parts")
    notes.write("Independent R2B01 frame-parts sample. Scene 01: empty front frame, replaceable card-colour stone ring, nameplate pocket plus blank insert, metal discard reverse. Scene 02: shallow exploded assembly plus visible rear-face sample. The large X and the nameplate pocket use true EXACT Boolean differences. Their hidden cutters remain in MODELING_TOOLS and can be edited. Scene 02 flips the installed rear cover so its X faces away from the skill front. All labels are mesh; all stone/metal materials are procedural. CPU Cycles 48, 1280x1000. This source does not modify or load ComponentSamples.blend or Workbench.blend, and is not Unity integration.")
    bpy.context.window.scene = builders[0].scene
    for area in bpy.context.screen.areas:
        if area.type == "VIEW_3D":
            area.spaces.active.region_3d.view_perspective = "CAMERA"
            area.spaces.active.shading.type = "MATERIAL"
    bpy.ops.wm.save_as_mainfile(filepath=str(source), compress=True)
    outputs[0].write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    if not args.no_render:
        for b in builders:
            bpy.context.window.scene = b.scene
            bpy.ops.render.render(write_still=True)
    print("R2B_FRAME_PARTS_DONE " + str(source))


if __name__ == "__main__":
    main()
