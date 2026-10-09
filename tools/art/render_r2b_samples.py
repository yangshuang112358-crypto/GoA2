"""Render and validate a saved R2B sample .blend. Never changes the source."""
import bpy, sys, argparse, json, math
from pathlib import Path
from mathutils import Vector
p=argparse.ArgumentParser();p.add_argument('--out',type=Path,required=True);p.add_argument('--frames',action='store_true')
args=p.parse_args(sys.argv[sys.argv.index('--')+1:]);out=args.out.resolve();out.mkdir(parents=True,exist_ok=True)
sc=bpy.data.scenes['03_R2_ROTATING_ASSEMBLY'];bpy.context.window.scene=sc
skills=[o for o in sc.objects if o.name.startswith('R2B_SKILL_FIXED_')]
sc.frame_set(1);bpy.context.view_layer.update()
before={o.name:[list(row) for row in o.matrix_world] for o in skills}
controls=[o for o in sc.objects if o.name.startswith('R2_') and o.animation_data]
control_before={o.name:list(o.rotation_euler) for o in controls}
sc.frame_set(121);bpy.context.view_layer.update()
after={o.name:[list(row) for row in o.matrix_world] for o in skills}
checks={'five_fixed_skill_roots':len(skills)==5,'skills_do_not_rotate':before==after,
 'decorative_controls_animate':any(list(o.rotation_euler)!=control_before[o.name] for o in controls),
 'actual_cut_meshes':sum('cut stone' in o.name for o in bpy.data.objects)>=30,
 'magic_is_recessed':all(o.get('channel_depth_below_surface',0)>.1 for o in bpy.data.objects if o.name.startswith('R2B Magic in recessed')),
 'range_and_distance_separate':any('range cut stone' in o.name for o in bpy.data.objects) and any('distance cut stone' in o.name for o in bpy.data.objects),
 'no_image_billboard_icons':not any(i.filepath.endswith('.png') for i in bpy.data.images)}
# Explicit model provenance and bounds. Source is not runtime-ready; no Unity assertion.
report={'checks':checks,'passed':all(checks.values()),'fixed_skill_roots':list(before),
 'animated_controls':list(control_before),'source':bpy.data.filepath,
 'scope':'Actual saved Blender model only; no Unity gameplay, performance, live values or user visual acceptance.',
 'clips':[]}
(out/'validation.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
if not report['passed']:raise RuntimeError('Model validation failed')
if args.frames:
    clips=[('ring','03_R2_ROTATING_ASSEMBLY',96),('skills','01_COMPLETE_SKILLS',72),('grooves','04_GROOVE_MACRO',48)]
    for clip,scene_name,count in clips:
        sc=bpy.data.scenes[scene_name];bpy.context.window.scene=sc
        sc.render.engine='CYCLES';sc.cycles.device='CPU';sc.cycles.samples=12
        sc.cycles.use_denoising=True;sc.render.use_persistent_data=True
        sc.render.resolution_x=720;sc.render.resolution_y=720;sc.render.resolution_percentage=100
        sc.render.image_settings.file_format='PNG'
        folder=out/clip;folder.mkdir(parents=True,exist_ok=True)
        if clip=='skills':
            # The two actual model groups gently pivot, no screen-space image warp.
            for ob in sc.objects:
                if ob.name.startswith('R2B_SKILL_FIXED_') and ob.animation_data:
                    for curve in ob.animation_data.action.fcurves:
                        curve.modifiers.new('CYCLES')
        for i in range(count):
            frame=1+i*2;sc.frame_set(frame)
            if clip=='grooves':
                theta=(i/(count-1)-.5)*.28
                cam=sc.camera;target=Vector((0,.1,0))
                cam.location=(math.sin(theta)*3.2,-2.28,14)
                cam.rotation_euler=(target-cam.location).to_track_quat('-Z','Y').to_euler()
            sc.render.filepath=str(folder/f'{i:04d}.png')
            bpy.ops.render.render(write_still=True)
            if i%12==0:print(f'R2B_VIDEO_PROGRESS {clip} {i+1}/{count}',flush=True)
        report['clips'].append({'name':clip,'fps':12,'frames':count,'seconds':count/12,
          'scene':scene_name,'source_frames':[1,1+(count-1)*2],'render':'CPU Cycles; actual animated scene geometry; silent',
          'loop':False})
    (out/'validation.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
print('R2B_SOURCE_VERIFIED')
