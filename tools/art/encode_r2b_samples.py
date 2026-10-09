"""Encode actual Blender renders, preserve all motion; short crossfades between scenes."""
from pathlib import Path
import argparse, subprocess, json, hashlib, shutil
p=argparse.ArgumentParser();p.add_argument('--root',type=Path,required=True);args=p.parse_args()
root=args.root.resolve();capture=root/'artifacts/r2b-model-samples/video'
dest=root/'docs/ui3d/samples/r2b-model-20261009';dest.mkdir(parents=True,exist_ok=True)
ffmpeg=root/'artifacts/video-tools/imageio_ffmpeg/binaries/ffmpeg-win-x86_64-v7.1.exe'
report=json.loads((capture/'validation.json').read_text(encoding='utf-8'))
assert report['passed'] and len(report['clips'])==3
for clip in report['clips']:
    name=clip['name'];frames=sorted((capture/name).glob('*.png'))
    assert len(frames)==clip['frames'],name
    subprocess.run([str(ffmpeg),'-hide_banner','-loglevel','error','-y','-framerate','12',
       '-i',str(capture/name/'%04d.png'),'-vf','fps=24','-c:v','libx264','-crf','20','-preset','medium',
       '-pix_fmt','yuv420p','-movflags','+faststart',str(capture/(name+'.mp4'))],check=True)
output=dest/'R2B01-model-demo.mp4'
subprocess.run([str(ffmpeg),'-hide_banner','-loglevel','error','-y',
 '-i',str(capture/'ring.mp4'),'-i',str(capture/'skills.mp4'),'-i',str(capture/'grooves.mp4'),
 '-filter_complex','[0:v][1:v]xfade=transition=fade:duration=0.4:offset=7.6[v1];[v1][2:v]xfade=transition=fade:duration=0.4:offset=13.2[v]',
 '-map','[v]','-c:v','libx264','-crf','20','-preset','medium','-pix_fmt','yuv420p',
 '-movflags','+faststart',str(output)],check=True)
subprocess.run([str(ffmpeg),'-hide_banner','-loglevel','error','-i',str(output),'-f','null','-'],check=True)
names={'01_COMPLETE_SKILLS.png':'complete-skills.png','02_BADGE_COMPONENTS.png':'stone-components.png',
 '03_R2_ROTATING_ASSEMBLY.png':'ring-assembly.png','04_GROOVE_MACRO.png':'groove-depth.png'}
for src,dst in names.items():shutil.copy2(root/'artifacts/r2b-model-samples/final'/src,dest/dst)
shutil.copy2(root/'artifacts/r2b-model-samples/final/model-report.json',dest/'model-report.json')
shutil.copy2(capture/'validation.json',dest/'validation.json')
shutil.copy2(root/'artifacts/r2b-model-samples/groove-validation.json',dest/'groove-validation.json')
for name in ('01_FRAME_PARTS_OVERVIEW.png','02_SHALLOW_EXPLODED_ASSEMBLY.png','R2B01_FrameParts_ModelReport.json'):
    shutil.copy2(root/'artifacts/r2b-frame-parts'/name,dest/name)
source=root/'art/production/samples/R2B01/R2B01_ComponentSamples.blend'
parts=root/'art/production/samples/R2B01/R2B01_FrameParts.blend'
manifest={'video':output.name,'source_blend':str(source.relative_to(root)).replace('\\','/'),
 'source_sha256':hashlib.sha256(source.read_bytes()).hexdigest(),
 'video_sha256':hashlib.sha256(output.read_bytes()).hexdigest(),
 'frame_parts_source':str(parts.relative_to(root)).replace('\\','/'),
 'frame_parts_sha256':hashlib.sha256(parts.read_bytes()).hexdigest(),
 'seconds':17.2,'dimensions':[720,720],'fps_encoded':24,'fps_rendered':12,
 'audio':'silent','provenance':'Actual Blender CPU Cycles frames from saved editable source, with two 0.4s crossfades; no generative images or image-warp animation.',
 'loop':False,'full_decode_passed':True,'unity_integration':False,
 'quality_boundary':'Editable structure/material samples; concept-level sculpture, weathering and small-size readability still need user review.'}
(dest/'media-manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(manifest,ensure_ascii=False,indent=2))
