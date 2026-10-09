"""Check actual saved geometry for the early prototype's hidden-magic regression."""
import bpy,sys,json,hashlib
from pathlib import Path
from mathutils import Vector
output=Path(sys.argv[sys.argv.index('--')+1])
results=[]
for scene in bpy.data.scenes:
    bpy.context.window.scene=scene;bpy.context.view_layer.update()
    deps=bpy.context.evaluated_depsgraph_get()
    for fill in [o for o in scene.objects if o.name.startswith('R2B Magic in recessed')]:
        me=fill.data;me.calc_loop_triangles()
        top=max(v.co.z for v in me.vertices)
        triangles=[t for t in me.loop_triangles if all(abs(me.vertices[i].co.z-top)<1e-4 for i in t.vertices)]
        triangles=sorted(triangles,key=lambda t:t.area,reverse=True)[:5]
        normal=(fill.parent.matrix_world.to_3x3()@Vector((0,0,1))).normalized()
        occluded=[]
        for triangle in triangles:
            point=fill.matrix_world@(sum((me.vertices[i].co for i in triangle.vertices),Vector())/3)
            origin=point+normal*.5
            for part in fill.parent.children:
                if part==fill or part.type!='MESH':continue
                ev=part.evaluated_get(deps);inv=ev.matrix_world.inverted()
                hit,loc,_,_=ev.ray_cast(inv@origin,(inv.to_3x3()@(-normal)).normalized())
                if hit and (ev.matrix_world@loc-point).dot(normal)>.003:
                    occluded.append(part.name)
        bodies=[o for o in fill.parent.children if 'cut stone' in o.name]
        # All these siblings share the badge coordinate system; root may be tilted.
        fill_max=max((fill.matrix_local@v.co).z for v in me.vertices)
        body_max=max((b.matrix_local@v.co).z for b in bodies for v in b.data.vertices)
        results.append({'scene':scene.name,'magic':fill.name,'samples':len(triangles),
                        'occluders':sorted(set(occluded)),'depth_below_stone':body_max-fill_max,
                        'passed':bool(triangles) and not occluded and body_max-fill_max>.10})
report={'source_sha256':hashlib.sha256(Path(bpy.data.filepath).read_bytes()).hexdigest(),
        'method':'Evaluated meshes; rays from outside five largest glyph-face triangles along the badge front normal. Detects other socket layers covering magic. Also measures actual vertex depths.',
        'samples':results,'passed':len(results)>=5 and all(r['passed'] for r in results)}
output.parent.mkdir(parents=True,exist_ok=True);output.write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
print('MAGIC_GEOMETRY',report['passed'],len(results))
if not report['passed']:raise RuntimeError('Magic geometry is hidden or not recessed')
