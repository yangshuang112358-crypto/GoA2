"""Editable, centrally symmetric connected rock mesh and two spiral altar halves.
Blender coordinates x,-world-z,height; deterministic art noise, no rule data mutation.
"""
import bpy,json,math,sys
from pathlib import Path
from mathutils import Vector
root=Path(sys.argv[sys.argv.index('--')+1]);out=root/'unity/Assets/Resources/UI3D/Terrain';source=root/'art/terrain'
out.mkdir(parents=True,exist_ok=True);source.mkdir(parents=True,exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True);bpy.context.preferences.filepaths.save_version=0
cells=[c for c in json.loads((root/'content/canonical/map.json').read_text(encoding='utf8'))['cells'] if c['obstacle']]
def world(x,y):return Vector((math.sqrt(3)*(x+y*.5),1.5*y,0))
center=world(0,.5);centers=[world(c['x'],c['y']) for c in cells];verts=[];faces=[]
def tri(a,b,c):
    index=len(verts);verts.extend([tuple(a),tuple(b),tuple(c)]);faces.append((index,index+1,index+2))
def noise(p):
    d=p-center;return math.cos(d.x*3.7)+math.cos(d.y*2.3)
for cell in cells:
    if (cell['x'],cell['y']) in [(0,0),(0,1)]:continue
    at=world(cell['x'],cell['y']);corners=[]
    for i in range(6):
        angle=math.radians(30+i*60);p=at+Vector((math.cos(angle),math.sin(angle),0));d=p-center
        p+=d.normalized()*noise(p)*.057;p.z=1.04+noise(p)*.08;corners.append(p)
    d=at-center;peak=at+d.normalized()*.12;peak.z=1.21+.11*math.cos(d.x*2+d.y*3)
    for i in range(6):
        a=corners[i];b=corners[(i+1)%6];mid=(a+b)*.5
        tri(peak,a,b)
        other=at+(mid-at)*2
        other.z=0
        if any((other-c).length<.18 for c in centers if (c-at).length>.2):continue
        def lower(p,t,z):
            near=[c for c in centers if Vector((p.x-c.x,p.y-c.y,0)).length<1.14]
            average=sum(near,Vector())/len(near);v=p.lerp(average,t);v.z=z;return v
        rings=[(a,b)]
        for inset,z in [(.04,.80),(.14,.63),(.105,.49),(.25,.27),(.32,.07)]:rings.append((lower(a,inset,z),lower(b,inset,z)))
        for j in range(len(rings)-1):
            a,b=rings[j];c,d=rings[j+1];tri(a,c,d);tri(a,d,b)
def mesh(name,v,f):
    data=bpy.data.meshes.new(name);data.from_pydata(v,[],f);data.update();obj=bpy.data.objects.new(name,data);bpy.context.collection.objects.link(obj)
    mat=bpy.data.materials.get('Atlantis hewn stone')
    if not mat:
        mat=bpy.data.materials.new('Atlantis hewn stone');mat.diffuse_color=(.31,.34,.33,1);mat.use_nodes=True;node=next(n for n in mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED');node.inputs['Base Color'].default_value=mat.diffuse_color;node.inputs['Roughness'].default_value=.74
    data.materials.append(mat)
    return obj
rock=mesh('ConnectedRocks',verts,faces)
(out/'rock-layout.json').write_text(json.dumps({'width':max(v[0] for v in verts)-min(v[0] for v in verts),'height':max(v[2] for v in verts),'centerX':center.x,'centerZ':-center.y}),encoding='utf8')
def export(obj,name):
    bpy.ops.object.select_all(action='DESELECT');obj.select_set(True);bpy.context.view_layer.objects.active=obj
    bpy.ops.export_scene.fbx(filepath=str(out/(name+'.fbx')),use_selection=True,object_types={'MESH'},bake_anim=False,axis_forward='-Z',axis_up='Y')
export(rock,'ConnectedRocks')
verts=[];faces=[];segments=48
# Cross-sections twist at the foot and progressively broaden into a true flat circular plate.
rings=[]
for radius,z,twist in [(.38,.02,100),(.48,.23,78),(.60,.5,48),(.82,.78,23),(1.12,1.05,0),(1.12,1.14,0)]:
    ring=[]
    for i in range(segments+1):
        a=-math.pi/2+i*math.pi/segments+math.radians(twist);ring.append(Vector((math.cos(a)*radius,math.sin(a)*radius,z)))
    rings.append(ring)
for level in range(len(rings)-1):
    for i in range(segments):tri(rings[level][i],rings[level][i+1],rings[level+1][i+1]);tri(rings[level][i],rings[level+1][i+1],rings[level+1][i])
    for end in [0,segments]:
        a,b=rings[level][end],rings[level+1][end];low=Vector((0,0,a.z));high=Vector((0,0,b.z))
        if end==0:tri(low,b,a);tri(low,high,b)
        else:tri(low,a,b);tri(low,b,high)
top=Vector((0,0,1.14))
for i in range(segments):tri(top,rings[-1][i],rings[-1][i+1])
for i in range(segments):
    a=-math.pi/2+i*math.pi/segments;b=a+math.pi/segments
    def p(angle,r,z):return Vector((math.cos(angle)*r,math.sin(angle)*r,z))
    for r,reverse in [(1.12,False),(1.045,True)]:
        u,v,w,x=p(a,r,1.14),p(b,r,1.14),p(a,r,1.28),p(b,r,1.28)
        if reverse:tri(u,w,x);tri(u,x,v)
        else:tri(u,x,w);tri(u,v,x)
    tri(p(a,1.045,1.28),p(a,1.12,1.28),p(b,1.12,1.28));tri(p(a,1.045,1.28),p(b,1.12,1.28),p(b,1.045,1.28))
half=mesh('CentralSpiralHalf',verts,faces);export(half,'CentralSpiralHalf')
half.location=center
other=half.copy();other.data=half.data;other.rotation_euler.z=math.pi;bpy.context.collection.objects.link(other)
bpy.ops.wm.save_as_mainfile(filepath=str(source/'Atlantis_Connected_Rocks.blend'),compress=True)
print('GOA_ROCKS_READY',len(cells),'obstacle cells; editable connected cliffs and spiral halves')
