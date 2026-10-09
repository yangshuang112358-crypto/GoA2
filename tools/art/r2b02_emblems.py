"""R2B02 fitted metal emblems. Editable solids, no images or font glyphs.

build_emblem(kind, collection, materials, center=(0, 0, 0), scale=1)
returns the created Blender mesh objects without changing the scene, selection,
active object, material library or render setup. Local construction envelope:
X [-.25,.25], Y [-.22,.22], Z [0,.10], face +Z. The builder applies center and
uniform scale to the finished geometry. Bevel modifiers stay editable.

Kinds: boot, hourglass, shield, sword, skill, range, distance, discard,
recover, reaction, quickmove, ultimate. Materials: stone, edge, metal, dark, glass,
sand, glow. Dark seams are shaped solid inserts rather than texture strokes.
"""

import math

import bpy
import bmesh
from mathutils import Vector


class _Builder:
    def __init__(self, kind, collection, materials, center, scale):
        self.prefix = "R2B02_" + kind + "_"
        self.collection = collection
        self.materials = materials
        self.center = Vector(center)
        self.scale = float(scale)
        self.objects = []

    def mesh(self, name, vertices, faces, material="metal", bevel=0, smooth=False):
        data = bpy.data.meshes.new(self.prefix + name + "Mesh")
        data.from_pydata([tuple(self.center + Vector(v) * self.scale)
                          for v in vertices], [], faces)
        data.update()
        bm = bmesh.new()
        bm.from_mesh(data)
        bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
        bm.to_mesh(data)
        bm.free()
        data.update()
        obj = bpy.data.objects.new(self.prefix + name, data)
        self.collection.objects.link(obj)
        data.materials.append(self.materials[material])
        for face in data.polygons:
            face.use_smooth = smooth
        if bevel:
            mod = obj.modifiers.new("Fine forged bevel", "BEVEL")
            mod.width = bevel * self.scale
            mod.segments = 3
            mod.limit_method = "ANGLE"
            mod.harden_normals = True
            normals = obj.modifiers.new("Weighted cast normals", "WEIGHTED_NORMAL")
            normals.keep_sharp = True
            normals.weight = 35
        obj["R2B02_emblem"] = True
        obj["emblem_kind"] = self.prefix.split("_")[1]
        obj["surface_method"] = "Editable solid relief; no texture billboard"
        self.objects.append(obj)
        return obj

    def solid(self, name, outline, low, high, material="metal", bevel=.003):
        n = len(outline)
        vertices = [(x, y, low) for x, y in outline]
        vertices += [(x, y, high) for x, y in outline]
        faces = [tuple(reversed(range(n))), tuple(range(n, n * 2))]
        faces += [(i, (i + 1) % n, (i + 1) % n + n, i + n)
                  for i in range(n)]
        return self.mesh(name, vertices, faces, material, bevel)

    def tube(self, name, path, radius, material="metal", sides=10):
        """A closed round extrusion through an explicitly sampled curved path."""
        points = [Vector(p) for p in path]
        vertices, faces = [], []
        for i, point in enumerate(points):
            tangent = points[min(i + 1, len(points) - 1)] - points[max(0, i - 1)]
            tangent.normalize()
            normal = Vector((0, 0, 1))
            if abs(tangent.dot(normal)) > .92:
                normal = Vector((0, 1, 0))
            lateral = tangent.cross(normal).normalized()
            up = tangent.cross(lateral).normalized()
            r = radius[i] if isinstance(radius, (list, tuple)) else radius
            for j in range(sides):
                a = math.tau * j / sides
                vertices.append(tuple(point + r * (lateral * math.cos(a) + up * math.sin(a))))
        for i in range(len(points) - 1):
            for j in range(sides):
                a, b = i * sides + j, i * sides + (j + 1) % sides
                faces.append((a, b, b + sides, a + sides))
        faces += [tuple(reversed(range(sides))),
                  tuple(range((len(points) - 1) * sides, len(points) * sides))]
        return self.mesh(name, vertices, faces, material, smooth=True)

    def ring(self, name, radius, width, z, material="metal", x=0, y=0,
             start=0, stop=math.tau, flatten=1):
        count = max(14, round(abs(stop - start) / math.tau * 72))
        return self.tube(name, [(x + radius * math.cos(start + (stop - start) * i / count),
                                y + radius * math.sin(start + (stop - start) * i / count), z)
                               for i in range(count + 1)], width, material)

    def jewel(self, name, x, y, z, width=.021, height=None, material="edge"):
        h = height or width
        vertices = [(x, y + h, z), (x + width, y, z), (x, y - h, z),
                    (x - width, y, z), (x, y, z + min(.012, width * .6)),
                    (x, y, z - .008)]
        faces = [(i, (i + 1) % 4, 4) for i in range(4)]
        faces += [((i + 1) % 4, i, 5) for i in range(4)]
        return self.mesh(name, vertices, faces, material, bevel=.0007)


def _map(points, sx=1, sy=1, tx=0, ty=0):
    return [(x * sx + tx, y * sy + ty) for x, y in points]


def _boot(b, quick=False):
    # Deliberately asymmetric side silhouette. Toe points right, heel left. The
    # upright shaft is much taller than the foot, unlike an undifferentiated blob.
    sx, tx = (.88, .025) if quick else (1, 0)
    def p(points):
        return _map(points, sx, 1, tx)
    outline = [(-.145, .205), (-.013, .205), (.008, .175), (.002, .065),
               (.040, .016), (.077, -.021), (.178, -.058), (.209, -.090),
               (.216, -.128), (.196, -.160), (.045, -.176),
               (-.077, -.165), (-.078, -.192), (-.167, -.192),
               (-.178, -.156), (-.157, -.034), (-.156, .128)]
    b.solid("Boot_dark_fitted_underlay", p(outline), .009, .035, "dark", .007)
    upper = [(-.136, .184), (-.025, .184), (-.016, .165), (-.024, .061),
             (.017, .000), (.062, -.043), (.169, -.078), (.190, -.098),
             (.188, -.125), (.167, -.140), (.033, -.153),
             (-.095, -.140), (-.148, -.151), (-.136, -.042), (-.145, .117)]
    b.solid("Boot_sculpted_upper", p(upper), .030, .060, "metal", .011)
    sole = [(-.169, -.148), (-.088, -.136), (.028, -.155), (.199, -.139),
            (.211, -.160), (.187, -.177), (.034, -.189), (-.086, -.177),
            (-.086, -.201), (-.169, -.201)]
    b.solid("Boot_separate_heavy_sole_and_heel", p(sole), .028, .051, "edge", .004)
    cuff = [(-.154, .183), (-.015, .183), (-.010, .205),
            (-.034, .217), (-.148, .213), (-.163, .197)]
    b.solid("Boot_folded_cuff", p(cuff), .044, .080, "edge", .005)
    # The ankle strap follows the oblique instep. A substantial buckle identifies
    # a boot at icon scale; thin ornamental laces would dissolve into noise.
    strap = [(-.118, .031), (-.012, .065), (.012, .036), (-.110, -.004)]
    b.solid("Boot_ankle_strap", p(strap), .058, .075, "dark", .003)
    buckle = p([(-.064, .004), (-.014, .020), (-.006, .050), (-.058, .034), (-.064, .004)])
    b.tube("Boot_raised_buckle", [(x, y, .079) for x, y in buckle], .0056, "edge")
    seam = p([(-.116, .165), (-.115, .090), (-.098, .021),
              (-.064, -.058), (.006, -.113), (.119, -.132)])
    b.tube("Boot_curved_leather_seam", [(x, y, .065) for x, y in seam], .0023, "dark")
    toe = p([(.122, -.066), (.133, -.099), (.121, -.141)])
    b.tube("Boot_fitted_toe_cap", [(x, y, .064) for x, y in toe], .0031, "edge")
    if quick:
        for i in range(2):
            y = .036 - i * .071
            b.solid("Quickmove_speed_cut_%d" % i,
                    [(-.245, y + .016), (-.125, y + .024),
                     (-.110, y + .008), (-.225, y - .010)],
                    .031, .052, "edge", .002)


def _hourglass(b):
    # Narrow frame, with real collapsed-neck glass volume. Its 0.236 x 0.428
    # envelope is recognizably vertical even when the surrounding badge is wide.
    for sign, label in ((1, "top"), (-1, "bottom")):
        cap = [(-.119, sign * .170), (-.100, sign * .193),
               (.100, sign * .193), (.119, sign * .170)]
        if sign < 0:
            cap.reverse()
        b.solid("Hourglass_%s_cast_cap" % label, cap, .008, .073, "metal", .005)
        edge = [(-.119, sign * .191), (-.105, sign * .211),
                (.105, sign * .211), (.119, sign * .191)]
        if sign < 0:
            edge.reverse()
        b.solid("Hourglass_%s_cap_lip" % label, edge, .015, .080, "edge", .003)
    for x in (-.100, .100):
        b.tube("Hourglass_side_pillar_%s" % x,
               [(x, -.170, .029), (x * 1.06, -.112, .036),
                (x * 1.03, .105, .036), (x, .170, .029)], .008, "metal")
    # Flattened lathe about Y: front and back surfaces form a continuous glass
    # vessel, not crossed bars. Explicit bulb shoulders create the sandglass.
    profile = [(-.170, .043), (-.151, .063), (-.119, .070),
               (-.083, .060), (-.045, .035), (-.015, .013),
               (0, .010), (.015, .013), (.045, .035),
               (.083, .060), (.119, .070), (.151, .063), (.170, .043)]
    vertices, faces, sides = [], [], 32
    for y, width in profile:
        for j in range(sides):
            a = math.tau * j / sides
            vertices.append((width * math.cos(a), y, .038 + width * .53 * math.sin(a)))
    for i in range(len(profile) - 1):
        for j in range(sides):
            a, n = i * sides + j, i * sides + (j + 1) % sides
            faces.append((a, n, n + sides, a + sides))
    faces += [tuple(reversed(range(sides))),
              tuple(range((len(profile) - 1) * sides, len(profile) * sides))]
    b.mesh("Hourglass_continuous_glass_bulb", vertices, faces, "glass", smooth=True)
    # Interior sand remains slightly forward for a readable inset relief, below
    # the glass shoulder. Top reservoir, thin falling stream and lower dune.
    b.solid("Hourglass_upper_sand_reservoir",
            [(-.050, .125), (.050, .125), (.046, .092), (.008, .032),
             (0, .020), (-.010, .035), (-.044, .091)], .040, .055, "sand", .002)
    b.tube("Hourglass_falling_sand", [(0, .020, .054), (0, -.068, .054)], .003, "sand")
    b.solid("Hourglass_lower_sand_dune",
            [(-.045, -.149), (.045, -.149), (.052, -.119),
             (.025, -.112), (0, -.074), (-.027, -.112), (-.053, -.122)],
            .039, .057, "sand", .002)
    for side in (-1, 1):
        b.tube("Hourglass_%s_shaped_glass_rim" % side,
               [(side * width, y, .042) for y, width in profile], .0024, "edge")


def _shield(b):
    outline = [(-.156, .180), (0, .210), (.156, .180), (.145, -.018),
               (.096, -.116), (0, -.210), (-.096, -.116), (-.145, -.018)]
    b.solid("Shield_outer_casting", outline, .012, .050, "edge", .005)
    inner = _map(outline, .78, .78)
    b.solid("Shield_recess_shadow", inner, .041, .055, "dark", .003)
    n = len(inner)
    vertices = [(x, y, .052) for x, y in inner] + [(0, .025, .089)]
    vertices += [(x, y, .037) for x, y in inner]
    faces = [(i, (i + 1) % n, n) for i in range(n)]
    faces += [(i, n + 1 + i, n + 1 + (i + 1) % n, (i + 1) % n) for i in range(n)]
    faces += [tuple(reversed(range(n + 1, n * 2 + 1)))]
    b.mesh("Shield_forged_convex_face", vertices, faces, "metal", .002)
    b.tube("Shield_center_keel", [(0, -.135, .066), (0, .025, .094),
                                 (0, .151, .068)], .004, "edge")
    for x in (-.119, .119):
        b.jewel("Shield_corner_pin_%s" % x, x, .149, .053, .008, material="edge")


def _sword(b, diagonal=False):
    # Symmetric four-plane blade with a high central ridge and honed side edges.
    def transform(vertices):
        if not diagonal:
            return vertices
        a = -.58
        return [(math.cos(a) * x - math.sin(a) * y,
                 math.sin(a) * x + math.cos(a) * y, z) for x, y, z in vertices]
    vertices = [(-.030, -.085, .054), (.030, -.085, .054),
                (.038, .117, .050), (0, .218, .050), (-.038, .117, .050),
                (0, -.074, .086), (0, .132, .086),
                (-.026, -.082, .031), (.026, -.082, .031),
                (.032, .115, .031), (0, .209, .031), (-.032, .115, .031)]
    faces = [(0, 1, 5), (1, 2, 6, 5), (2, 3, 6), (3, 4, 6),
             (4, 0, 5, 6), (7, 11, 10, 9, 8), (0, 7, 8, 1),
             (1, 8, 9, 2), (2, 9, 10, 3), (3, 10, 11, 4), (4, 11, 7, 0)]
    b.mesh("Sword_diamond_section_blade", transform(vertices), faces, "metal", .0014)
    # The guard, grip, pommel are each their own closed casting.
    for name, outline, z0, z1, mat in (
        ("Sword_curved_guard", [(-.106, -.073), (-.060, -.061), (-.022, -.079),
                                (.023, -.079), (.060, -.061), (.106, -.073),
                                (.092, -.091), (.034, -.101), (-.034, -.101),
                                (-.092, -.091)], .040, .071, "edge"),
        ("Sword_wrapped_grip", [(-.021, -.095), (.021, -.095),
                                (.023, -.182), (-.023, -.182)], .040, .070, "dark"),
        ("Sword_pommel", [(-.025, -.177), (.025, -.177), (.030, -.196),
                          (0, -.216), (-.030, -.196)], .034, .078, "edge")):
        if diagonal:
            verts = transform([(x, y, 0) for x, y in outline])
            outline = [(x, y) for x, y, z in verts]
        b.solid(name, outline, z0, z1, mat, .0024)
    for i in range(3):
        y = -.115 - i * .022
        b.tube("Sword_grip_binding_%d" % i,
               transform([(-.021, y + .008, .073), (.021, y - .008, .073)]),
               .0027, "metal")


def _skill(b):
    # An unmistakable spell seal: five balanced cast rays enclosing a hovering
    # split diamond. Large silhouettes are retained when the badge is small.
    b.ring("Spell_seal_dark_socket", .147, .012, .030, "dark")
    b.ring("Spell_seal_cast_ring", .147, .005, .049, "edge")
    for i in range(5):
        a = math.pi * .5 + i * math.tau / 5
        dx, dy = math.cos(a), math.sin(a)
        lx, ly = -dy, dx
        points = [(dx * .166 + lx * .020, dy * .166 + ly * .020),
                  (dx * .212, dy * .212),
                  (dx * .166 - lx * .020, dy * .166 - ly * .020),
                  (dx * .153, dy * .153)]
        b.solid("Spell_seal_ray_%d" % i, points, .024, .060, "metal", .0015)
    b.solid("Spell_split_diamond_left",
            [(-.006, .104), (-.068, .011), (-.006, -.092), (-.018, .006)],
            .037, .078, "metal", .002)
    b.solid("Spell_split_diamond_right",
            [(.006, .104), (.068, .011), (.006, -.092), (.018, .006)],
            .037, .078, "edge", .002)
    b.tube("Spell_rune_core", [(0, .079, .053), (0, -.061, .053)], .0042, "glow")
    for x in (-.101, .101):
        b.jewel("Spell_orbit_stud_%s" % x, x, -.036, .043, .012, material="edge")


def _range(b):
    # Concentric fields alone; crosshairs are reserved for attack distance.
    for index, (radius, width) in enumerate(((.190, .010), (.125, .009), (.060, .008))):
        b.ring("Range_field_%d_dark_seat" % index, radius, width + .003, .022, "dark")
        b.ring("Range_field_%d_cast_ridge" % index, radius, width, .040 + index * .008, "metal")
    b.jewel("Range_center_gem", 0, 0, .055, .017, material="edge")


def _distance(b):
    # The broad spear blade must be identifiable at roughly 50 px. Keep the
    # reticle smaller and offset down-left so the whole triangular shoulder
    # clears the ring, rather than disappearing as a thicker diagonal tick.
    d = Vector((.59, .807, 0))
    side = Vector((-d.y, d.x, 0))
    reticle = d * -.045
    b.ring("Distance_target_socket", .102, .011, .022, "dark", x=reticle.x, y=reticle.y)
    b.ring("Distance_target_ring", .102, .007, .039, "metal", x=reticle.x, y=reticle.y)
    for axis in range(4):
        a = axis * math.pi / 2
        b.tube("Distance_crosshair_tick_%d" % axis,
               [(reticle.x + r * math.cos(a), reticle.y + r * math.sin(a), .041)
                for r in (.081, .137)], .006, "edge")
    b.tube("Distance_spear_cast_shaft", [tuple(d * t + Vector((0, 0, .066)))
                                       for t in (-.245, .123)], .009, "metal")
    center = d * .118
    vertices = [tuple(center + side * -.068 + Vector((0, 0, .060))),
                tuple(d * .255 + Vector((0, 0, .060))),
                tuple(center + side * .068 + Vector((0, 0, .060))),
                tuple(d * .095 + Vector((0, 0, .060))),
                tuple(d * .165 + Vector((0, 0, .097))),
                tuple(d * .165 + Vector((0, 0, .044)))]
    faces = [(i, (i + 1) % 4, 4) for i in range(4)]
    faces += [((i + 1) % 4, i, 5) for i in range(4)]
    blade = b.mesh("Distance_spear_broad_triangle_head", vertices, faces, "edge", .001)
    blade.data.materials.append(b.materials["metal"])
    # Two bevel faces receive different metals so the high central keel reads
    # clearly even when the key light is close to the surface normal.
    blade.data.polygons[1].material_index = 1
    blade.data.polygons[2].material_index = 1
    collar = d * .090 + Vector((0, 0, .074))
    b.tube("Distance_spear_blade_socket", [tuple(collar - side * .016),
                                          tuple(collar + side * .016)], .005, "edge")
    for i in range(3):
        c = d * (-.164 - i * .021) + Vector((0, 0, .072))
        b.tube("Distance_spear_grip_wrap_%d" % i,
               [tuple(c + side * -.009 - d * .006), tuple(c + side * .009 + d * .006)],
               .0032, "dark")


def _card_operation(b, recover=False):
    # A real bevelled card slab, tray and broad arrow. Opposite arrow directions
    # distinguish putting down from taking back without relying on color.
    b.solid("Card_tray_shadow", [(-.186, -.108), (.186, -.108),
                                (.156, -.184), (-.156, -.184)], .012, .042, "dark", .004)
    b.solid("Card_tray_front_lip", [(-.171, -.155), (.171, -.155),
                                   (.156, -.184), (-.156, -.184)], .035, .067, "edge", .004)
    b.solid("Card_tray_left_lip", [(-.186, -.108), (-.161, -.108),
                                  (-.144, -.155), (-.171, -.166)], .031, .063, "metal", .003)
    b.solid("Card_tray_right_lip", [(.186, -.108), (.161, -.108),
                                   (.144, -.155), (.171, -.166)], .031, .063, "metal", .003)
    b.solid("Card_beveled_slab", [(-.109, -.094), (.085, -.094), (.109, .161),
                                 (-.085, .175)], .033, .057, "metal", .006)
    b.solid("Card_inset_face", [(-.083, -.067), (.063, -.067), (.082, .136),
                               (-.064, .147)], .055, .061, "dark", .004)
    arrow = [(-.029, .183), (.029, .183), (.029, .042), (.074, .042),
             (0, -.033), (-.074, .042), (-.029, .042)]
    if recover:
        arrow = [(x, .164 - y) for x, y in arrow]
    b.solid("Card_%s_arrow" % ("recover_up" if recover else "discard_down"),
            arrow, .061, .092, "edge", .002)


def _reaction(b):
    # One continuous silhouette and one solid casting: the wide triangular head
    # grows directly from the arc. There is no dark centre stud, material change
    # or layered dart to make the head read as a separate piece at small sizes.
    start, stop, steps = -.23 * math.pi, 1.37 * math.pi, 80
    outer, inner = [], []
    for i in range(steps):
        t = i / (steps - 1)
        a = start + t * (stop - start)
        width = .009 + .025 * min(1, t * 7)
        outer.append(((.150 + width) * math.cos(a), (.150 + width) * math.sin(a)))
        inner.append(((.150 - width) * math.cos(a), (.150 - width) * math.sin(a)))
    base = Vector((.150 * math.cos(stop), .150 * math.sin(stop), 0))
    tangent = Vector((-math.sin(stop), math.cos(stop), 0))
    radial = Vector((math.cos(stop), math.sin(stop), 0))
    # The triangle base is perpendicular to the arc tangent. Its shoulders are
    # attached to the same outline, guaranteeing a continuous visible junction.
    arrow = [base + radial * .069, base + tangent * .077, base - radial * .069]
    outline = outer + [(p.x, p.y) for p in arrow] + list(reversed(inner))
    b.solid("Reaction_single_cast_return_arrow", outline, .030, .068, "metal", .002)


def _ultimate(b):
    # A three-pronged crown enclosing a large faceted star crystal. It is a
    # category emblem without a number, differentiated by both silhouette and
    # purple material supplied by the caller.
    crown = [(-.184, .085), (-.135, .018), (-.099, .135),
             (-.045, .054), (0, .211), (.045, .054), (.099, .135),
             (.135, .018), (.184, .085), (.128, -.141),
             (.077, -.173), (-.077, -.173), (-.128, -.141)]
    b.solid("Ultimate_cast_crown", crown, .017, .047, "edge", .003)
    inset = [(-.135, .019), (-.090, -.039), (-.062, .039),
             (0, .138), (.062, .039), (.090, -.039), (.135, .019),
             (.097, -.119), (.060, -.140), (-.060, -.140), (-.097, -.119)]
    b.solid("Ultimate_crown_shadow_inset", inset, .044, .052, "dark", .002)
    b.jewel("Ultimate_violet_crystal", 0, -.013, .062, .057, .089, "glow")
    b.tube("Ultimate_lower_brow", [(-.114, -.117, .057), (-.075, -.139, .057),
                                    (0, -.150, .057), (.075, -.139, .057),
                                    (.114, -.117, .057)], .008, "metal")
    for x in (-.068, .068):
        b.jewel("Ultimate_brow_rivet_%s" % x, x, -.132, .064, .009, material="edge")


def build_emblem(kind, collection, materials, center=(0, 0, 0), scale=1):
    """Build one fitted emblem in ``collection`` and return its editable parts."""
    if scale <= 0:
        raise ValueError("Emblem scale must be positive")
    builders = {"boot": _boot, "hourglass": _hourglass, "shield": _shield,
                "sword": _sword, "skill": _skill, "range": _range,
                "distance": _distance, "discard": _card_operation,
                "recover": lambda b: _card_operation(b, True),
                "reaction": _reaction, "quickmove": lambda b: _boot(b, True),
                "ultimate": _ultimate}
    if kind not in builders:
        raise ValueError("Unknown R2B02 emblem: " + str(kind))
    required = {"metal", "edge", "dark", "glass", "sand", "glow"}
    missing = required.difference(materials)
    if missing:
        raise ValueError("Missing emblem materials: " + ", ".join(sorted(missing)))
    b = _Builder(kind, collection, materials, center, scale)
    builders[kind](b)
    return b.objects
