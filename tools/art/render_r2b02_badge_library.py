"""Validate the saved badge source and render full-quality stills / a short turn."""
import argparse, json, math, sys
from pathlib import Path
import bpy
from mathutils import Vector

p=argparse.ArgumentParser();p.add_argument('--out',type=Path,required=True)
p.add_argument('--stills',action='store_true');p.add_argument('--video',action='store_true')
p.add_argument('--only',default='')
a=p.parse_args(sys.argv[sys.argv.index('--')+1:])
out=a.out.resolve();out.mkdir(parents=True,exist_ok=True)
report={'source':bpy.data.filepath,'checks':[],'glyphs':[],'scope':'Model geometry and numerical margins only; user art approval and Unity runtime remain separate.'}
scenes=[s for s in bpy.data.scenes if s.name[:2].isdigit()]
core=next(s for s in scenes if s.name.startswith('02_'))
roots=[o for o in core.objects if o.type=='EMPTY' and o.get('semantic')]
sem={o['semantic'] for o in roots}
report['checks'].append({'name':'Seven actual stat semantics','passed':sem=={'boot','shield','sword','skill','range','distance','hourglass'}})
report['checks'].append({'name':'Every semantic has a separate inner emblem','passed':all(any(ch.get('semantic_emblem')==o['semantic'] for ch in o.children_recursive) for o in roots)})
hour=next(o for o in roots if o['semantic']=='hourglass')
report['checks'].append({'name':'Hourglass is taller than wide','passed':hour['shape_width']/hour['shape_height']<.75})
report['checks'].append({'name':'Skill with no value has no zero engraved','passed':next(o for o in roots if o['semantic']=='skill')['value']=='none'})

for sc in scenes:
    bpy.context.window.scene=sc;sc.frame_set(1);bpy.context.view_layer.update()
    deps=bpy.context.evaluated_depsgraph_get()
    floors=[o for o in sc.objects if o.get('recess_below_face')]
    for floor in floors:
        body=next(o for o in floor.parent.children if o.name.endswith('/ dressed stone face'))
        ev=floor.evaluated_get(deps);be=body.evaluated_get(deps)
        me=ev.to_mesh();me.calc_loop_triangles()
        triangles=[t for t in me.loop_triangles if t.normal.z>.95]
        triangles.sort(key=lambda t:t.area,reverse=True)
        positions=[sum((me.vertices[i].co for i in t.vertices),Vector())/3 for t in triangles]
        chosen=positions[:14]
        for axis in (0,1):
            chosen.extend(sorted(positions,key=lambda q:q[axis])[:4])
            chosen.extend(sorted(positions,key=lambda q:q[axis])[-4:])
        normal=(ev.matrix_world.to_3x3()@Vector((0,0,1))).normalized()
        failures=[]
        for xyz in chosen:
            world=ev.matrix_world@xyz
            hit,loc,norm,face,obj,mat=sc.ray_cast(deps,world+normal, -normal)
            front_ok=hit and abs((loc-world).dot(normal))<.016
            inv=be.matrix_world.inverted()
            bh,bp,bn,bi=be.ray_cast(inv@(world+normal),inv.to_3x3()@(-normal))
            # A patina/energy mesh floating beyond the stone outline is not a valid cavity.
            solid_under=bool(bh and .0001 < (world-be.matrix_world@bp).dot(normal)<.021)
            if not front_ok or not solid_under:
                failures.append({'point':list(world),'front_unobstructed':bool(front_ok),'stone_floor_present':solid_under,
                                 'hit':obj.name if hit else None})
        report['glyphs'].append({'scene':sc.name,'object':floor.name,'value':floor['glyph'],
                                 'samples':len(chosen),'passed':not failures,'failures':failures})
        ev.to_mesh_clear()
report['passed']=all(c['passed'] for c in report['checks']) and all(g['passed'] for g in report['glyphs'])
(out/'geometry-validation.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf8',newline='\n')
if not report['passed']:
    print('FAILED_GLYPHS '+json.dumps([g['object'] for g in report['glyphs'] if not g['passed']],ensure_ascii=False),flush=True)
    raise RuntimeError('Badge geometry verification failed; inspect geometry-validation.json')
print('R2B02_GEOMETRY_OK '+str(len(report['glyphs']))+' engraved badges',flush=True)

if a.stills:
    for sc in sorted(scenes,key=lambda s:s.name):
        if a.only and sc.name not in a.only.split(','):continue
        bpy.context.window.scene=sc;sc.frame_set(1)
        sc.render.resolution_percentage=100;sc.cycles.samples=64;sc.cycles.device='CPU';sc.cycles.use_denoising=True
        sc.render.filepath=str(out/(sc.name+'.png'));bpy.ops.render.render(write_still=True)
        print('R2B02_STILL_DONE '+sc.name,flush=True)
if a.video:
    sc=next(s for s in scenes if s.name.startswith('01_'));bpy.context.window.scene=sc
    moving=[o for o in sc.objects if o.type=='EMPTY' and o.get('semantic')]
    sc.render.resolution_x=720;sc.render.resolution_y=600;sc.render.resolution_percentage=100
    sc.cycles.samples=16;sc.render.use_persistent_data=True
    folder=out/'frames';folder.mkdir(exist_ok=True)
    for i in range(48):
        sc.frame_set(int(round(1+i*2.5)))
        for ob in moving:ob.rotation_euler=(.07*math.sin(i/47*math.tau),.32*math.sin(i/47*math.tau),0)
        sc.render.filepath=str(folder/f'{i:04d}.png');bpy.ops.render.render(write_still=True)
        if i%12==0:print('R2B02_VIDEO '+str(i+1)+'/48',flush=True)
print('R2B02_RENDER_COMPLETE',flush=True)
