"""Production-style semantic stone badges: real cut numerals and separate emblems.

Independent of scene setup. Geometry is authored in XY with its front facing +Z.
The same component can be reused for primary and secondary actions by material.
"""
import math
import bpy
import bmesh
from mathutils import Vector
from r2b02_emblems import build_emblem


OUTLINES={
    # A side-view boot with a forward toe, inset instep and a separate heel.
    'boot':[(-.47,.79),(.18,.79),(.24,.59),(.20,.23),(.27,.01),(.45,-.13),(.72,-.20),
            (.84,-.34),(.84,-.56),(.72,-.69),(.04,-.69),(-.08,-.57),(-.23,-.57),
            (-.23,-.71),(-.53,-.71),(-.61,-.58),(-.60,-.15),(-.47,.10)],
    'shield':[(0,.83),(.33,.67),(.67,.55),(.64,-.30),(.43,-.58),(0,-.85),(-.43,-.58),(-.64,-.30),(-.67,.55),(-.33,.67)],
    'sword':[(0,.92),(.23,.65),(.23,.30),(.58,.12),(.65,-.08),(.48,-.18),(.46,-.59),(0,-.94),(-.46,-.59),(-.48,-.18),(-.65,-.08),(-.58,.12),(-.23,.30),(-.23,.65)],
    'skill':[(0,.88),(.24,.62),(.31,.58),(.54,.22),(.62,-.23),(.42,-.61),(0,-.86),(-.42,-.61),(-.62,-.23),(-.54,.22),(-.31,.58),(-.24,.62)],
    'hourglass':[(-.51,.79),(.51,.79),(.56,.66),(.41,.47),(.28,.15),(.33,-.17),
                 (.47,-.49),(.57,-.64),(.51,-.79),(-.51,-.79),(-.57,-.64),(-.47,-.49),
                 (-.33,-.17),(-.28,.15),(-.41,.47),(-.56,.66)],
    'range':[(.67*math.cos(i*math.tau/96),.81*math.sin(i*math.tau/96)) for i in range(96)],
    'distance':[(0,.94),(.43,.60),(.64,.21),(.58,-.48),(0,-.94),(-.58,-.48),(-.64,.21),(-.43,.60)],
    'utility':[(-.47,.63),(.47,.63),(.63,.43),(.63,-.43),(.43,-.63),(-.43,-.63),(-.63,-.43),(-.63,.43)],
    'glyph':[(-.53,.49),(.53,.49),(.62,.38),(.62,-.38),(.51,-.50),(-.51,-.50),(-.62,-.38),(-.62,.38)]
}


def mesh(name,verts,faces,collection,mat):
    me=bpy.data.meshes.new(name);me.from_pydata(verts,[],faces);me.update()
    bm=bmesh.new();bm.from_mesh(me);bmesh.ops.recalc_face_normals(bm,faces=bm.faces);bm.to_mesh(me);bm.free()
    ob=bpy.data.objects.new(name,me);collection.objects.link(ob)
    if mat:me.materials.append(mat)
    return ob


def bevel(ob,width=.02,apply=True,segments=2):
    mod=ob.modifiers.new('Dressed edge / true chamfer','BEVEL');mod.width=width;mod.segments=segments
    if apply:
        bpy.context.view_layer.objects.active=ob;bpy.ops.object.modifier_apply(modifier=mod.name)
    return ob


def poly(name,pts,bottom,top,collection,mat,edge=0):
    if sum(pts[i][0]*pts[(i+1)%len(pts)][1]-pts[(i+1)%len(pts)][0]*pts[i][1] for i in range(len(pts)))<0:
        pts=list(reversed(pts))
    n=len(pts)
    ob=mesh(name,[(x,y,z) for z in (bottom,top) for x,y in pts],
            [tuple(reversed(range(n))),tuple(range(n,2*n))]+[(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)],collection,mat)
    if edge:bevel(ob,edge)
    return ob


def path(name,pts,width,collection,mat):
    cu=bpy.data.curves.new(name,'CURVE');cu.dimensions='3D';cu.bevel_depth=width;cu.bevel_resolution=2;cu.use_fill_caps=True
    s=cu.splines.new('POLY');s.points.add(len(pts)-1)
    for i,(pt,p) in enumerate(zip(s.points,pts)):
        pt.co=(*p,1);pt.radius=.4+.6*math.sin(math.pi*(i+.5)/len(pts))
    ob=bpy.data.objects.new(name,cu);collection.objects.link(ob);cu.materials.append(mat)
    return ob


def subtract(body,cutter,label):
    if cutter.type!='MESH':
        bpy.ops.object.select_all(action='DESELECT');cutter.select_set(True)
        bpy.context.view_layer.objects.active=cutter;bpy.ops.object.convert(target='MESH')
    mod=body.modifiers.new('Incised / '+label,'BOOLEAN');mod.operation='DIFFERENCE';mod.solver='EXACT';mod.object=cutter
    bpy.context.view_layer.objects.active=body;bpy.ops.object.modifier_apply(modifier=mod.name)
    bpy.data.objects.remove(cutter,do_unlink=True)


def glyph(text,center,height,width,collection,font,depth=.16):
    cu=bpy.data.curves.new('Engraving cutter '+text,'FONT');cu.body=text;cu.font=font
    cu.size=1;cu.align_x='CENTER';cu.align_y='CENTER';cu.extrude=depth;cu.resolution_u=12
    ob=bpy.data.objects.new('Engraving cutter '+text,cu);collection.objects.link(ob)
    bpy.context.view_layer.update()
    s=min(height/ob.dimensions.y,width/ob.dimensions.x);ob.scale=(s,s,1)
    bpy.ops.object.select_all(action='DESELECT');ob.select_set(True);bpy.context.view_layer.objects.active=ob
    bpy.ops.object.convert(target='MESH');bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    xs=[v.co.x for v in ob.data.vertices];ys=[v.co.y for v in ob.data.vertices]
    mx=(min(xs)+max(xs))/2;my=(min(ys)+max(ys))/2
    for v in ob.data.vertices:v.co.x-=mx;v.co.y-=my
    ob.location=center
    return ob


def engrave(body,text,xy,height,width,collection,font,mat,state,tag):
    cutter=glyph(text,(xy[0],xy[1],.598),height,width,collection,font)
    floor=cutter.copy();floor.data=cutter.data.copy();collection.objects.link(floor)
    floor.name=tag+' / incised numeral floor '+text
    zs=[v.co.z for v in floor.data.vertices];lo=min(zs);span=max(zs)-lo
    for v in floor.data.vertices:v.co.z=(v.co.z-lo)/span*.006
    floor.location.z=.440
    floor.data.materials.clear();floor.data.materials.append(mat)
    floor['recess_below_face']=.117;floor['glyph']=text;floor['state']=state
    subtract(body,cutter,'numeral '+text)
    bevel(body,.010,False)
    return floor


def build_badge(kind,value,collection,materials,font,*,tag=None,stone_key='stone',edge_key='edge',state='normal'):
    """Create one editable badge around origin, then place its returned root.

    Value=None is intentional no-number layout. State changes only the numeral.
    The utility kinds are action/history marks and never get an invented value.
    """
    tag=tag or ('BADGE '+kind+' '+str(value))
    before=set(collection.objects)
    is_utility=kind in ('discard','recover','reaction','quickmove','ultimate')
    outline_kind='utility' if is_utility else kind
    pts=OUTLINES[outline_kind]
    def scaled(s):return [(x*s,y*s) for x,y in pts]
    socket=poly(tag+' / fitted black seat',scaled(1.027),.095,.35,collection,materials['dark'],.030)
    rim=poly(tag+' / stone edge',pts,.26,.53,collection,materials[edge_key],.040)
    pocket=poly('CUT inner pocket',scaled(.86),.35,.72,collection,materials['dark'])
    subtract(rim,pocket,'hollow socket keeps numeral unobstructed')
    body=poly(tag+' / dressed stone face',scaled(.90),.39,.563,collection,materials[stone_key],.024)
    for i in (1,len(pts)//2):
        x,y=pts[i];x*=.97;y*=.97
        cutter=poly('CUT edge spall',[(x-.028,y-.036),(x+.031,y-.005),(x+.008,y+.034)],.479,.61,collection,materials['dark'])
        subtract(rim,cutter,'controlled corner spall')
    if kind=='boot':
        iconcenter=(-.16,.43,.568);iconscale=.98
        numberxy=(.09,-.27);numberheight=.52;numberwidth=.70
        # Separate sole/heel undercut; geometry follows the foot, not a rectangular tab.
        for nm,points in [('toe',[(-.02,-.62),(.66,-.62),(.76,-.51)]),('heel',[(-.53,-.61),(-.30,-.61)])]:
            cut=path('CUT boot '+nm,[(x,y,.552) for x,y in points],.012,collection,materials['dark']);subtract(body,cut,'boot '+nm)
    elif kind=='hourglass':
        iconcenter=(0,.445,.568);iconscale=1.03
        numberxy=(0,-.32);numberheight=.54;numberwidth=.67
    elif kind in ('sword','distance'):
        iconcenter=(0,.40,.568);iconscale=1.11
        numberxy=(0,-.36);numberheight=.63;numberwidth=.70
    elif kind=='shield':
        iconcenter=(0,.43,.568);iconscale=1.10
        numberxy=(0,-.25);numberheight=.67;numberwidth=.86
    elif kind=='range':
        iconcenter=(0,.39,.568);iconscale=1.12
        numberxy=(0,-.29);numberheight=.62;numberwidth=.82
    elif kind=='glyph':
        iconcenter=(0,0,.568);iconscale=0
        numberxy=(0,0);numberheight=.63;numberwidth=.96
    else:
        iconcenter=(0,0,.568);iconscale=2.08 if value is None else 1.12
        if value is not None:iconcenter=(0,.40,.568)
        numberxy=(0,-.32);numberheight=.60;numberwidth=.78
    if value is not None:
        text=str(value)
        if kind=='shield' and text=='2':
            # The broad baseline of a serif 2 needs more margin at the
            # shield's sloping lower sides than the other single digits.
            numberxy=(0,-.18);numberheight=.60;numberwidth=.72
        if kind=='shield' and len(text)>1:
            numberxy=(0,-.17);numberheight=.55;numberwidth=.73
        if len(text)>1 and text!='∞':numberheight*=.87
        if kind=='hourglass' and len(text)>1:
            # The narrowed waist cannot take a wide two-digit cut at mid-height.
            # Fit it in the lower bulb, preserving material all around the slot.
            numberxy=(0,-.38);numberheight=.45;numberwidth=.55
        floor=engrave(body,text,numberxy,numberheight,numberwidth,collection,font,
                      materials['cavity'] if state=='normal' else materials[state],state,tag)
    iconmat={k:materials[k] for k in ('stone','edge','metal','dark','glass','sand','glow')}
    if kind=='ultimate' and 'ultimate_glow' in materials:iconmat['glow']=materials['ultimate_glow']
    if iconscale:
        emblems=build_emblem(kind,collection,iconmat,center=iconcenter,scale=iconscale)
        # Seat the complete relief as one assembly slightly into the face.
        # Each emblem has a different sculpted lower bound, so a fixed center
        # would leave some apparently floating above their stone socket.
        lower=min(v.co.z for ob in emblems if ob.type=='MESH' for v in ob.data.vertices)
        for ob in emblems:ob.location.z+=.560-lower
        for ob in emblems:ob.name=tag+' / inner '+ob.name;ob['semantic_emblem']=kind
    # Two shallow incisions at quiet side edges, avoiding icon and numeral areas.
    if kind not in ('boot','hourglass','glyph'):
        for j,points in enumerate([[(-.48,.12),(-.41,.08),(-.43,-.01)],[(.40,-.53),(.32,-.58),(.29,-.65)]]):
            cut=path('CUT weathered seam',[(x,y,.559) for x,y in points],.006,collection,materials['dark'])
            subtract(body,cut,'side fissure '+str(j))
    root=bpy.data.objects.new(tag,None);collection.objects.link(root)
    for ob in set(collection.objects)-before:
        if ob!=root and ob.parent is None:ob.parent=root
    root['semantic']=kind;root['value']='none' if value is None else str(value)
    root['stone']=stone_key;root['numeric_modifier']=state
    root['inside_emblem']=iconscale>0
    root['shape_width']=max(x for x,y in pts)-min(x for x,y in pts)
    root['shape_height']=max(y for x,y in pts)-min(y for x,y in pts)
    return root
