"""Shared saved-model material and transparent baking setup for R2B runtime art."""
import bpy, math
from mathutils import Vector

def materials():
    def find(prefix):return next(m for m in bpy.data.materials if m.name.startswith(prefix))
    result={k:find(p) for k,p in {
        'stone':'01 Graphite','edge':'02 Pale','red':'03 Iron-red','red_edge':'04 Red dressed',
        'dark':'05 Blackened','cavity':'06b Deep','metal':'07 Forged','bronze':'08 Antique',
        'green_magic':'12 Green','red_magic':'13 Red','glass':'R2B02 Hourglass / actual',
        'sand':'R2B02 Hourglass / warm','glow':'R2B02 Spell crest',
        'ultimate_glow':'R2B02 Ultimate crest'}.items()}
    for color in ('blue','green','gold','silver','purple'):
        for suffix in ('','_edge'):result[color+suffix]=find('R2B02 '+color+suffix+' / card stone')
    return result

def emission(name,rgb,strength=1):
    m=bpy.data.materials.new(name);m.use_nodes=True
    nodes=m.node_tree.nodes;nodes.clear();out=nodes.new('ShaderNodeOutputMaterial');e=nodes.new('ShaderNodeEmission')
    e.inputs[0].default_value=(*rgb,1);e.inputs[1].default_value=strength;m.node_tree.links.new(e.outputs[0],out.inputs[0])
    m.diffuse_color=(*rgb,1);return m

def studio(name,size=256,scale=2.15,samples=24):
    sc=bpy.data.scenes.new(name);bpy.context.window.scene=sc
    col=bpy.data.collections.new(name+' / editable objects');sc.collection.children.link(col)
    sc.render.engine='CYCLES';sc.cycles.device='CPU';sc.cycles.samples=samples;sc.cycles.use_denoising=True;sc.cycles.max_bounces=5
    sc.render.film_transparent=True;sc.render.image_settings.file_format='PNG';sc.render.image_settings.color_mode='RGBA'
    sc.render.resolution_x=sc.render.resolution_y=size;sc.render.resolution_percentage=100
    sc.view_settings.view_transform='AgX';sc.view_settings.look='AgX - Medium High Contrast'
    world=bpy.data.worlds.new(name+' world');world.use_nodes=True
    bg=next(n for n in world.node_tree.nodes if n.type=='BACKGROUND');bg.inputs[0].default_value=(.12,.15,.19,1);bg.inputs[1].default_value=.28;sc.world=world
    cam=bpy.data.objects.new(name+' camera',bpy.data.cameras.new(name+' camera'));sc.collection.objects.link(cam)
    cam.data.type='ORTHO';cam.data.ortho_scale=scale;cam.location=(0,0,15);sc.camera=cam
    for nm,loc,energy,sz,color in [('raking key',(-4,6,7),750,3.0,(1,.9,.78)),('cool fill',(4,1,6),300,4,(.70,.82,1)),('rim',(0,4,3),190,2,(1,1,1))]:
        ld=bpy.data.lights.new(name+nm,'AREA');ld.energy=energy;ld.size=sz;ld.color=color
        ob=bpy.data.objects.new(name+nm,ld);sc.collection.objects.link(ob);ob.location=loc
        ob.rotation_euler=(-ob.location).to_track_quat('-Z','Y').to_euler()
    return sc,col

def render(scene,path):
    path.parent.mkdir(parents=True,exist_ok=True);bpy.context.window.scene=scene
    scene.render.filepath=str(path);bpy.ops.render.render(write_still=True)

def floor_mask(scene,obj,path,size=256):
    """Rasterize the *actual cut floor* into a linear alpha mask, not a font overlay."""
    import numpy as np
    from bpy_extras.object_utils import world_to_camera_view
    ss=2;side=size*ss;alpha=np.zeros((side,side),dtype=np.float32)
    bpy.context.view_layer.update();deps=bpy.context.evaluated_depsgraph_get();ev=obj.evaluated_get(deps);me=ev.to_mesh();me.calc_loop_triangles()
    for tri in me.loop_triangles:
        if tri.normal.z<.9:continue
        q=[world_to_camera_view(scene,scene.camera,ev.matrix_world@me.vertices[i].co) for i in tri.vertices]
        a,b,c=[np.array((p.x*side,p.y*side)) for p in q]
        lo=np.maximum(0,np.floor(np.minimum(np.minimum(a,b),c)).astype(int));hi=np.minimum(side-1,np.ceil(np.maximum(np.maximum(a,b),c)).astype(int))
        if any(hi<lo):continue
        yy,xx=np.mgrid[lo[1]:hi[1]+1,lo[0]:hi[0]+1];xx=xx+.5;yy=yy+.5
        den=(b[1]-c[1])*(a[0]-c[0])+(c[0]-b[0])*(a[1]-c[1])
        if abs(den)<1e-8:continue
        u=((b[1]-c[1])*(xx-c[0])+(c[0]-b[0])*(yy-c[1]))/den
        v=((c[1]-a[1])*(xx-c[0])+(a[0]-c[0])*(yy-c[1]))/den
        inside=(u>=0)&(v>=0)&(u+v<=1)
        alpha[lo[1]:hi[1]+1,lo[0]:hi[0]+1]=np.maximum(alpha[lo[1]:hi[1]+1,lo[0]:hi[0]+1],inside)
    ev.to_mesh_clear();alpha=alpha.reshape(size,ss,size,ss).mean(axis=(1,3))
    rgba=np.ones((size,size,4),dtype=np.float32);rgba[:,:,3]=alpha
    im=bpy.data.images.new(path.stem,size,size,alpha=True);im.pixels.foreach_set(rgba.ravel());im.filepath_raw=str(path);im.file_format='PNG';im.save();bpy.data.images.remove(im)
