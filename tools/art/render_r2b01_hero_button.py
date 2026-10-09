"""Inspect and render the saved single-button source; no source modification."""
import argparse, json, math, sys
from pathlib import Path
import bpy
from mathutils import Vector

p=argparse.ArgumentParser()
p.add_argument('--out',type=Path,required=True)
p.add_argument('--video',action='store_true')
p.add_argument('--stills',action='store_true')
p.add_argument('--magic-only',action='store_true')
a=p.parse_args(sys.argv[sys.argv.index('--')+1:])
out=a.out.resolve();out.mkdir(parents=True,exist_ok=True)
sc=bpy.context.scene;root=bpy.data.objects['BUTTON / rotate this complete assembly']
sc.frame_set(1);root.rotation_euler=(0,0,0);bpy.context.view_layer.update()
deps=bpy.context.evaluated_depsgraph_get()
results=[]
for floor in [o for o in sc.objects if o.name.startswith('Actual recessed patina floor')]:
    ev=floor.evaluated_get(deps);me=ev.to_mesh();me.calc_loop_triangles()
    triangles=[t for t in me.loop_triangles if t.normal.z>.95]
    triangles.sort(key=lambda t:t.area,reverse=True)
    samples=[]
    for tri in triangles[:5]:
        xyz=sum((me.vertices[i].co for i in tri.vertices),Vector())/3
        world=ev.matrix_world@xyz
        hit,loc,normal,face,ob,matrix=sc.ray_cast(deps,world+Vector((0,0,1)),Vector((0,0,-1)))
        samples.append({'unobstructed':bool(hit and abs(loc.z-world.z)<.017),
                        'hit':ob.name if hit else None,'depth_error':round(loc.z-world.z,5) if hit else None})
    results.append({'floor':floor.name,'actual_recess':floor['depth_below_surface'],
                    'samples':samples,'pass':bool(samples) and all(s['unobstructed'] for s in samples)})
    ev.to_mesh_clear()
report={'source':bpy.data.filepath,'floors':results,'passed':all(x['pass'] for x in results),
        'scope':'Geometric recessed floors and front occlusion, not art approval or Unity readiness.'}
(out/'groove-validation.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf8')
if not report['passed']:raise RuntimeError('A recessed floor is occluded; see report.')

base=bpy.data.objects['Primary / attack / recessed face']
# Identify the primary by its actual location instead of relying on duplicate suffix order.
patina=min((o for o in sc.objects if o.name.startswith('Actual recessed patina floor')),
           key=lambda o:(o.location.x+2.05)**2+(o.location.y+1.4)**2)
variants=[o for o in sc.objects if o.name.startswith('VARIANT ')]


def state(which):
    base.hide_render=which!='normal'
    patina.hide_render=which!='normal'
    for ob in variants:
        ob.hide_render=which not in ob.name or which=='normal'


def point_camera(loc,target,scale):
    sc.camera.location=loc
    sc.camera.rotation_euler=(Vector(target)-Vector(loc)).to_track_quat('-Z','Y').to_euler()
    sc.camera.data.ortho_scale=scale


sc.render.engine='CYCLES';sc.cycles.device='CPU';sc.cycles.use_denoising=True
sc.render.image_settings.file_format='PNG';sc.render.resolution_percentage=100
if a.stills or a.magic_only:
    sc.cycles.samples=64;sc.render.resolution_x=1120;sc.render.resolution_y=1120
    if not a.magic_only:
        state('normal');point_camera((6,-4.8,18),(0,0,.1),7.3)
        sc.render.filepath=str(out/'02-oblique.png');bpy.ops.render.render(write_still=True)
    sc.render.resolution_x=960;sc.render.resolution_y=960
    point_camera((-4.0,-4.0,9.5),(-2.02,-1.32,.46),2.30)
    for which in (('green_magic','red_magic') if a.magic_only else ('normal','green_magic','red_magic')):
        state(which);sc.render.filepath=str(out/('03-groove-'+which+'.png'))
        bpy.ops.render.render(write_still=True)

if a.video:
    folder=out/'frames';folder.mkdir(exist_ok=True)
    sc.render.use_persistent_data=True;sc.cycles.samples=16
    sc.render.resolution_x=640;sc.render.resolution_y=640
    # 8 seconds, actual geometry pivot; the last 3 seconds demonstrate optional magic floors.
    for i in range(96):
        sc.frame_set(i*2+1)
        root.rotation_euler=(.02*math.sin(i/95*math.tau),.27*math.sin(i/95*math.tau),0)
        point_camera((0,-1.15,22),(0,0,.10),6.95)
        state('normal' if i<60 else ('green_magic' if i<78 else 'red_magic'))
        sc.render.filepath=str(folder/f'{i:04d}.png')
        bpy.ops.render.render(write_still=True)
        if i%12==0:print(f'HERO_VIDEO {i+1}/96',flush=True)
print('HERO_SOURCE_CHECK_AND_RENDER_DONE',flush=True)
