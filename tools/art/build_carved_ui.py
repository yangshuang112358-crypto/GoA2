"""Carved slate / aged brass UI. Editable Blender reliefs, never baked game data.

Run with Blender --background --python this.py -- <repo> [--only name...].
Owns CarvedUI.blend and the listed baked images; never reads Workbench.blend.
Small, intentional fractures and broad bevels survive the actual 156px footprint.
"""
import bpy
import math
import random
import sys
import json
from pathlib import Path

args = sys.argv[sys.argv.index('--') + 1:]
root = Path(args[0]).resolve()
only = set(args[2:]) if len(args) > 1 and args[1] == '--only' else None
out = root / 'unity/Assets/Resources/UI3D'
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.preferences.filepaths.save_version = 0
variants = {}
active = None


def mat(name, dark, light, metallic=0, rough=.6, scale=5, bump=.08):
    m = bpy.data.materials.new(name)
    m.diffuse_color = (*light, 1)
    m.use_nodes = True
    n, l = m.node_tree.nodes, m.node_tree.links
    bs = n.get('Principled BSDF')
    bs.inputs['Metallic'].default_value = metallic
    bs.inputs['Roughness'].default_value = rough
    noise = n.new('ShaderNodeTexNoise'); noise.inputs['Scale'].default_value = scale
    noise.inputs['Detail'].default_value = 3; noise.inputs['Roughness'].default_value = .7
    ramp = n.new('ShaderNodeValToRGB')
    ramp.color_ramp.elements[0].position = .17; ramp.color_ramp.elements[0].color = (*dark, 1)
    ramp.color_ramp.elements[1].position = .83; ramp.color_ramp.elements[1].color = (*light, 1)
    l.new(noise.outputs['Fac'], ramp.inputs[0]); l.new(ramp.outputs[0], bs.inputs['Base Color'])
    micro = n.new('ShaderNodeTexNoise'); micro.inputs['Scale'].default_value = 145
    relief = n.new('ShaderNodeBump'); relief.inputs['Strength'].default_value = bump
    relief.inputs['Distance'].default_value = .016
    l.new(micro.outputs['Fac'], relief.inputs['Height']); l.new(relief.outputs[0], bs.inputs['Normal'])
    return m


slate = mat('Slate - mineral layers', (.052,.078,.090), (.12,.17,.18), rough=.81, bump=.25)
edge = mat('Slate - worn cut', (.16,.22,.23), (.36,.43,.41), rough=.69, bump=.18)
pale = mat('Stat stone - cool alabaster', (.25,.29,.28), (.48,.53,.48), rough=.62, bump=.12)
bronze = mat('Brass - oxidised valleys', (.11,.063,.026), (.31,.19,.066), .78, .37)
gold = mat('Brass - hand polished edges', (.40,.235,.070), (.76,.54,.24), .72, .27, bump=.055)
dark = mat('Deep cut and contact shadow', (.008,.017,.022), (.020,.035,.04), .08, .88)
steel = mat('Reverse - blackened steel', (.055,.074,.084), (.20,.26,.28), .72, .42, bump=.12)
inner = mat('Skill face - blue slate', (.021,.044,.058), (.065,.105,.12), .1, .76, scale=3, bump=.08)


def variant(name, path, scale, size):
    global active
    active = bpy.data.collections.new(name); bpy.context.scene.collection.children.link(active)
    variants[name] = (active, path, scale, size)


def mesh(name, verts, faces, material, bevel=0):
    data = bpy.data.meshes.new(name); data.from_pydata(verts, [], faces); data.update()
    ob = bpy.data.objects.new(name, data); active.objects.link(ob); data.materials.append(material)
    if bevel:
        mod = ob.modifiers.new('Soft tool-cut edges', 'BEVEL'); mod.width = bevel; mod.segments = 3
        ob.modifiers.new('Weighted bevel normals', 'WEIGHTED_NORMAL')
    return ob


def poly(name, pts, z, depth, material, bevel=.018):
    n = len(pts)
    return mesh(name, [(x,y,h) for h in (z-depth/2,z+depth/2) for x,y in pts],
                [tuple(reversed(range(n))), tuple(range(n,n*2))] +
                [(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)], material, bevel)


def circle(name, r, z, depth, material, n=96):
    return poly(name, [(math.cos(i*math.tau/n)*r,math.sin(i*math.tau/n)*r) for i in range(n)], z, depth, material, .014)


def ring(name, ro, ri, z, material, depth=.10, n=128):
    verts = [(math.cos(i*math.tau/n)*r, math.sin(i*math.tau/n)*r, h)
             for r,h in [(ro,z),(ro-.028,z+depth),(ri+.024,z+depth),(ri,z)] for i in range(n)]
    return mesh(name, verts, [(row*n+i,row*n+(i+1)%n,(row+1)*n+(i+1)%n,(row+1)*n+i)
                             for row in range(3) for i in range(n)], material)


def line(name, pts, width, material):
    curve = bpy.data.curves.new(name, 'CURVE'); curve.dimensions='3D'
    curve.resolution_u=2; curve.bevel_depth=width; curve.bevel_resolution=2
    spline=curve.splines.new('POLY'); spline.points.add(len(pts)-1)
    for p, co in zip(spline.points, pts): p.co=(*co,1)
    ob=bpy.data.objects.new(name,curve); active.objects.link(ob); curve.materials.append(material)
    return ob


def carving(name, pts, z, width=.012):
    line(name+' light cut lip', [(x-.005,y-.009,z) for x,y in pts], width, edge)
    line(name+' recessed seam', [(x,y,z+.004) for x,y in pts], width*.72, dark)


def pin(x, y, z, size=.034):
    ob=circle('Brass fastening',size,z,.05,gold,16); ob.location.x=x; ob.location.y=y
    line('Pin tool slot',[(x-size*.55,y,z+.027),(x+size*.55,y,z+.027)],.006,bronze)


variant('front', 'SkillDiscs/front.png',3.16,(768,768))
circle('Recessed outer shadow',1.44,-.14,.18,dark)
ring('Thick antique brass casing',1.42,1.24,-.02,bronze,.15)
ring('Broad worn chamfer',1.40,1.32,.12,gold,.065)
ring('Under-cut colour channel',1.30,1.19,.11,dark,.065)
ring('Inside cut edge',1.19,1.13,.10,bronze,.035)
circle('Concave slate skill face',1.135,.075,.12,inner)
# Radial flutes catch highlights at the outer rim, leaving centre clear for live names.
for i in range(32):
    a=i*math.tau/32
    line('Radial forged groove',[(math.cos(a)*1.345,math.sin(a)*1.345,.19),
                                (math.cos(a)*1.395,math.sin(a)*1.395,.165)],.009,bronze)
for i in range(8):
    a=(i+.5)*math.pi/4; pin(math.cos(a)*1.365,math.sin(a)*1.365,.205,.031)
# Symmetrical engraved tidal flourishes on exposed stone; no invented gameplay symbol.
for sign in (-1,1):
    carving('Tidal stone engraving',[(sign*x,y) for x,y in [(0,.86),(.20,.75),(.36,.82),(.56,.67),(.74,.65)]],.144,.012)
    carving('Lower tidal engraving',[(sign*x,-y) for x,y in [(0,.83),(.18,.74),(.33,.78),(.49,.65)]],.144,.010)

variant('back','SkillDiscs/back.png',3.16,(768,768))
circle('Discard casing',1.44,-.1,.2,dark)
ring('Discard gilded rim',1.42,1.23,.03,gold,.13)
circle('Solid dark steel reverse',1.24,.05,.18,steel)
ring('Reverse deep machined border',1.13,1.08,.15,bronze,.035)
cross=[(-.61,-.43),(-.43,-.61),(0,-.18),(.43,-.61),(.61,-.43),(.18,0),(.61,.43),(.43,.61),(0,.18),(-.43,.61),(-.61,.43),(-.18,0)]
poly('Chamfer at sunken X',[(x*1.1,y*1.1) for x,y in cross],.151,.02,edge,.026)
poly('Deep recessed X',cross,.165,.016,dark,.014)
for i in range(8):
    a=i*math.pi/4; pin(math.cos(a)*1.33,math.sin(a)*1.33,.19)

glyphs={
 'boot':[(-.16,.20),(.07,.20),(.06,-.06),(.25,-.13),(.25,-.24),(-.20,-.24),(-.20,-.10),(-.13,-.03)],
 'shield':[(-.24,.18),(0,.27),(.24,.18),(.19,-.12),(0,-.29),(-.19,-.12)],
 'sword':[(-.23,-.30),(-.31,-.22),(-.07,.02),(-.20,.14),(-.13,.22),(0,.09),(.20,.34),(.32,.35),(.31,.22),(.10,0),(.23,-.12),(.15,-.20),(.02,-.07)],
 'spark':[(0,.32),(.075,.085),(.30,0),(.075,-.075),(0,-.30),(-.075,-.075),(-.30,0),(-.075,.085)],
 'arrow':[(-.24,-.30),(-.31,-.23),(.06,.15),(-.09,.20),(.33,.34),(.20,-.09),(.16,.07)],
 'range':[(0,.30),(.30,0),(0,-.30),(-.30,0)]}
outline=[(-.36,-.48),(-.49,-.30),(-.47,.26),(-.30,.44),(-.07,.49),(.30,.43),(.48,.25),(.46,-.34),(.27,-.48)]
for kind,glyph in glyphs.items():
    variant(kind,'SkillDiscs/'+kind+'.png',1.12,(320,360))
    poly('Thick socket',[(x*1.06,y*1.06-.012) for x,y in outline],-.055,.22,dark,.043)
    poly('Brass retaining socket',outline,-.013,.20,bronze,.038)
    poly('Carved stone broad bevel',[(x*.95,y*.95) for x,y in outline],.06,.22,pale,.047)
    poly('Indented numeral face',[(x*.77,y*.79-.015) for x,y in outline],.176,.04,slate,.025)
    # Symbol is larger and separated from the numeral by an incised lip.
    poly('Action relief '+kind,[(x*.51,y*.51+.286) for x,y in glyph],.211,.015,gold,.009)
    if kind=='shield': line('Shield centre ridge',[(0,.18,.228),(0,.407,.228)],.009,bronze)
    if kind=='range':
        dot=circle('Distance target centre',.042,.223,.008,slate,24); dot.location.y=.286
    carving('Stone fracture',[(-.37,.12),(-.30,.075),(-.31,-.005)],.19,.009)

variant('speed','SkillDiscs/speed.png',1.90,(512,320))
for side in (-1,1):
    for j in range(3):
        y=-.28+j*.12
        pts=[(side*.16,y-.05),(side*.43,y-.025),(side*.65,y+.06),(side*(.77-j*.055),y+.245),
             (side*.48,y+.145),(side*.26,y+.11)]
        if side<0:pts.reverse()
        poly('Speed carved wing',pts,.02+j*.017,.13,pale,.022)
        line('Feather cut',[(side*.30,y+.04,.115+j*.017),(side*.48,y+.085,.115+j*.017),
                           (side*.64,y+.165,.115+j*.017)],.014,slate)
speed=[(-.28,-.43),(-.43,-.23),(-.36,.26),(-.18,.41),(.15,.41),(.36,.23),(.42,-.25),(.21,-.43)]
poly('Speed bronze socket',[(x*1.07,y*1.07) for x,y in speed],.07,.17,bronze,.05)
poly('Speed stone chamfer',speed,.14,.18,pale,.052)
poly('Speed live numeral recess',[(x*.76,y*.78) for x,y in speed],.238,.03,slate,.028)

variant('wheel','SkillDiscs/wheel.png',4.2,(1024,1024))
ring('Steel structural ring',2.0,1.79,0,steel,.13)
ring('Outer brass bead',2.008,1.966,.095,gold,.054)
ring('Inner bronze bevel',1.835,1.785,.10,bronze,.036)
for i in range(60):
    a=i*math.tau/60
    line('Chiselled radial ring slot',[(math.cos(a)*1.89,math.sin(a)*1.89,.134),
        (math.cos(a)*1.95,math.sin(a)*1.95,.134)],.009,bronze)
for i in range(10):
    a=(i+.5)*math.tau/10;pin(math.cos(a)*1.9,math.sin(a)*1.9,.15,.025)


def slab_outline(notched,w=4.0,h=1.52):
    # The body, bevel and inlaid lines use the same top/bottom sockets.
    pts=[(-w+.23,h)]
    if notched:
        for i in range(25):
            a=math.pi+i*math.pi/24;pts.append((.70*math.cos(a),h+.39*math.sin(a)))
    pts += [(w-.27,h),(w,h-.24),(w-.035,.56),(w,-h+.25),(w-.25,-h)]
    if notched:
        for i in range(25):
            a=i*math.pi/24;pts.append((.70*math.cos(a),-h+.39*math.sin(a)))
    pts += [(-w+.20,-h),(-w,-h+.23),(-w+.024,-.35),(-w,h-.25)]
    return list(reversed(pts))


for notched in (False,True):
    name='main' if notched else 'response'
    variant(name,'CarvedUI/slab-'+name+'.png',8.36,(1200,464))
    pts=slab_outline(notched)
    poly('Stone deep lower wall',[(x,y-.07) for x,y in pts],-.22,.42,dark,.075)
    poly('Hand-cut stone bevel',pts,-.025,.40,edge,.09)
    poly('Recessed slate tablet',[(x*.969,y*.935) for x,y in pts],.175,.10,slate,.045)
    # Wide irregular bevel cuts, localized outside typography instead of noise everywhere.
    for side in (-1,1):
        for j,(yy,length) in enumerate([(1.15,.50),(.54,.33),(-.77,.43),(-1.20,.30)]):
            x=side*3.87
            carving('Weathered edge crack',[(x,yy),(x-side*.19,yy-.08),(x-side*length,yy-.035)],.232,.019)
        # Broken ancient corner clamps. Never a continuous flat coloured window outline.
        for top in (-1,1):
            x=side*3.58;y=top*1.22
            pts2=[(x-side*.23,y+top*.14),(x+side*.21,y+top*.14),(x+side*.34,y),
                  (x+side*.34,y-top*.33),(x+side*.18,y-top*.33),(x+side*.17,y-top*.05),(x-side*.23,y-top*.025)]
            poly('Antique brass corner binding',pts2,.267,.08,bronze,.034)
            line('Polished binding crest',[(x-side*.18,y+top*.08,.319),(x+side*.16,y+top*.08,.319),
                 (x+side*.27,y-top*.04,.319),(x+side*.27,y-top*.24,.319)],.022,gold)
            pin(x+side*.05,y-top*.045,.328,.046)
    for yy in (-1.30,1.30):
        for side in (-1,1):
            line('Interrupted fine inlay',[(side*.98,yy,.24),(side*3.06,yy,.24)],.011,bronze)
    # Right edge chip with light upper facet gives stone its thickness in a front bake.
    poly('Chipped edge facet',[(3.94,.36),(3.67,.21),(3.79,-.13),(3.96,-.24)],.257,.016,dark,.005)
    poly('Chip exposed mineral',[(3.92,.36),(3.67,.21),(3.75,.18)],.269,.01,pale,.003)

for family,glyph in [('attack','sword'),('defense','shield'),('movement','boot'),('skill','spark')]:
    variant('action-'+family,'CarvedUI/action-'+family+'.png',1.30,(256,256))
    octagon=[(-.43,-.58),(.43,-.58),(.58,-.43),(.58,.43),(.43,.58),(-.43,.58),(-.58,.43),(-.58,-.43)]
    poly('Action icon socket',octagon,0,.17,bronze,.025)
    poly('Action icon dark enamel',[(x*.83,y*.83) for x,y in octagon],.096,.025,inner,.025)
    poly('Large action relief',[(x*1.14,y*1.14) for x,y in glyphs[glyph]],.17,.075,gold,.022)

variant('settings-base','CarvedUI/settings-base.png',3.16,(520,360))
base=[(-1.18,-.93),(-1.46,-.64),(-1.46,.64),(-1.18,.93),(1.18,.93),(1.46,.64),(1.46,-.64),(1.18,-.93)]
poly('Settings deep stone body',base,-.13,.36,dark,.065)
poly('Settings brass bevel',[(x*.97,y*.97+.025) for x,y in base],.04,.23,gold,.04)
poly('Settings carved slate bevel',[(x*.89,y*.84+.035) for x,y in base],.16,.18,edge,.045)
poly('Settings recessed inscription plane',[(x*.82,y*.74+.035) for x,y in base],.269,.04,slate,.035)
for side in (-1,1):
    pin(side*1.25,0,.32,.05)
    carving('Settings fracture',[(side*1.17,.57),(side*1.03,.42),(side*1.09,.26)],.298,.021)
ob=circle('Gear recessed socket',.56,.30,.014,dark);ob.location.y=.24

variant('settings-gear','CarvedUI/settings-gear.png',1.3,(320,320))
gear=[]
for i in range(48):
    a=i*math.tau/48;r=.57 if i%6 in (0,1,4,5) else .465
    gear.append((math.cos(a)*r,math.sin(a)*r))
poly('Forged gear solid',gear,.04,.16,bronze,.013)
poly('Gear bright cut chamfer',[(x*.94,y*.94) for x,y in gear],.13,.065,gold,.016)
circle('Gear inset eye',.24,.167,.015,dark,64)
circle('Gear central cut stone',.165,.185,.018,slate,48)

# The continuous level boss and hand band follow the user's drawing. Experience
# arcs, five card colours, level forecast and upgrade arrows remain live UI.
variant('hero-plate','CarvedUI/hero-plate.png',7.6875,(984,272))
plate=[]
def plate_point(x,y): return ((x-123)/32,(71-y)/32)
def curve_points(a,b,c,d):
    for k in range(1,25):
        t=k/24;s=1-t
        plate.append(plate_point(s*s*s*a[0]+3*s*s*t*b[0]+3*s*t*t*c[0]+t*t*t*d[0],
                                 s*s*s*a[1]+3*s*s*t*b[1]+3*s*t*t*c[1]+t*t*t*d[1]))
plate.append(plate_point(35,38))
curve_points((35,38),(55,38),(60,54),(72,61))
curve_points((72,61),(94,72),(164,63),(242,64))
plate += [plate_point(242,102),plate_point(35,102)]
curve_points((35,102),(-8,102),(-8,38),(35,38))
poly('Hero continuous cast bronze silhouette',list(reversed(plate)),0,.16,bronze,.042)
# The band is deliberately subdued so five gameplay colours carry the contrast.
poly('Joined stone enamel well',[(x*.989,y*.91) for x,y in reversed(plate)],.10,.06,inner,.022)
line('Forged outer shoulder highlight',[(x,y,.105) for x,y in plate[:49]],.016,gold)
boss_x,boss_y=plate_point(35,70)
for label,ro,ri,z,m in [('Level outer bronze bevel',.981,.905,.125,gold),('Level inner cut',.842,.785,.15,bronze)]:
    ob=ring(label,ro,ri,z,m,.035);ob.location.x=boss_x;ob.location.y=boss_y
ob=circle('Level inset slate',.795,.115,.06,inner);ob.location.x=boss_x;ob.location.y=boss_y
for x in (65,240):
    xx,yy=plate_point(x,100);pin(xx,yy,.155,.018)

variant('panel','CarvedUI/panel.png',8.0,(1000,700))
framepts=[(-3.75,-2.55),(-3.88,-2.40),(-3.88,2.40),(-3.75,2.55),(3.75,2.55),(3.88,2.40),(3.88,-2.40),(3.75,-2.55)]
poly('Panel deep cast body',framepts,-.12,.30,dark,.045)
poly('Panel narrow bronze fillet',[(x*.992,y*.987) for x,y in framepts],.025,.12,bronze,.025)
poly('Panel inset stone lip',[(x*.977,y*.966) for x,y in framepts],.091,.08,edge,.022)
poly('Panel reading plane',[(x*.969,y*.953) for x,y in framepts],.135,.035,inner,.014)
for sx in (-1,1):
    for sy in (-1,1):
        x,y=sx*3.75,sy*2.41
        line('Panel corner brass binding',[(x-sx*.28,y,.18),(x,y,.18),(x,y-sy*.28,.18)],.028,gold)
        pin(x-sx*.10,y-sy*.10,.18,.025)

scene=bpy.context.scene
scene.render.engine='CYCLES'; scene.cycles.device='CPU'; scene.cycles.samples=64
scene.cycles.use_denoising=True
scene.render.film_transparent=True; scene.render.image_settings.file_format='PNG'
scene.render.image_settings.color_mode='RGBA'; scene.render.resolution_percentage=100
scene.view_settings.view_transform='AgX'
scene.world=bpy.data.worlds.new('Studio soft cool fill');scene.world.use_nodes=True
scene.world.node_tree.nodes.get('Background').inputs[0].default_value=(.40,.46,.54,1)
scene.world.node_tree.nodes.get('Background').inputs[1].default_value=.25
camera=bpy.data.objects.new('Orthographic UI bake',bpy.data.cameras.new('Orthographic UI bake'))
scene.collection.objects.link(camera);camera.location=(0,0,12);camera.data.type='ORTHO';scene.camera=camera
for name,loc,energy,color,size in [('Large warm upper left',(-3,4,5),620,(1,.88,.68),3),
                                 ('Cool rim right',(4,1,4),330,(.63,.80,1),2),
                                 ('Soft face fill',(-1,-4,6),90,(.76,.86,1),4)]:
    data=bpy.data.lights.new(name,'AREA');data.energy=energy;data.color=color;data.shape='DISK';data.size=size
    ob=bpy.data.objects.new(name,data);scene.collection.objects.link(ob);ob.location=loc
    ob.rotation_euler=(-ob.location).to_track_quat('-Z','Y').to_euler()
manifest=[]
for name,(collection,path,scale,size) in variants.items():
    manifest.append({'asset':name,'path':'unity/Assets/Resources/UI3D/'+path,'size':size,
                     'objects':len(collection.objects),'representation':'Blender relief baked to RGBA; live Unity text and values'})
    if only and name not in only:continue
    for n,(col,*_) in variants.items():col.hide_render=col.hide_viewport=n!=name
    camera.data.ortho_scale=scale;scene.render.resolution_x,scene.render.resolution_y=size
    target=out/path;target.parent.mkdir(parents=True,exist_ok=True);scene.render.filepath=str(target)
    bpy.ops.render.render(write_still=True)
for name,(col,*_) in variants.items():col.hide_render=col.hide_viewport=name!='front'
camera.data.ortho_scale=3.16;scene.render.resolution_x=scene.render.resolution_y=768
scene['README']='One named collection per relief. Runtime numbers, names, colour inlays and game state are NOT baked. Source owned by build_carved_ui.py. No external artwork.'
source=root/'art/production/assets/CarvedUI.blend'
bpy.ops.wm.save_as_mainfile(filepath=str(source),compress=True)
(root/'art/ui/CarvedUI-manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print('GOA_CARVED_UI_READY')
