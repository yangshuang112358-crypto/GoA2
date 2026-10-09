"""Small phone-friendly media from actual saved-model Cycles renders."""
import argparse, hashlib, json, shutil, subprocess, zipfile
from pathlib import Path

p=argparse.ArgumentParser();p.add_argument('--root',type=Path,required=True)
a=p.parse_args();root=a.root.resolve()
src=root/'artifacts/r2b01-hero/final'
dst=root/'docs/ui3d/samples/r2b01-hero-20261009';dst.mkdir(parents=True,exist_ok=True)
ff=root/'artifacts/video-tools/imageio_ffmpeg/binaries/ffmpeg-win-x86_64-v7.1.exe'


def run(*args):
    subprocess.run([str(ff),'-hide_banner','-loglevel','error','-y',*map(str,args)],check=True)


for stem in ('01-front','02-oblique','03-groove-normal','03-groove-green_magic','03-groove-red_magic'):
    width=900 if stem.startswith(('01','02')) else 720
    run('-i',src/(stem+'.png'),'-vf',f'scale={width}:{width}','-frames:v','1','-q:v','2',dst/(stem+'.jpg'))
frames=src/'frames'
expected={f'{i:04d}.png' for i in range(96)}
if {f.name for f in frames.glob('*.png')}!=expected:
    raise RuntimeError('Expected exactly 96 consecutive real render frames.')
run('-framerate','12','-i',frames/'%04d.png','-an','-c:v','libx264','-preset','slow','-crf','22',
    '-pix_fmt','yuv420p','-movflags','+faststart',dst/'R2B01-Boomerang.mp4')
run('-i',dst/'R2B01-Boomerang.mp4','-vf','scale=480:480','-an','-c:v','libx264','-preset','slow','-crf','27',
    '-pix_fmt','yuv420p','-movflags','+faststart',dst/'R2B01-Boomerang-mobile.mp4')
for name in ('R2B01-Boomerang.mp4','R2B01-Boomerang-mobile.mp4'):
    run('-i',dst/name,'-f','null','NUL')
for name in ('model-report.json','groove-validation.json'):
    # Hash the same LF bytes Git publishes, not Windows text-mode CRLF bytes.
    (dst/name).write_text((src/name).read_text(encoding='utf8'),encoding='utf8',newline='\n')
source=root/'art/production/samples/R2B01-Hero/R2B01_Boomerang.blend'
readme=source.with_name('README.md')
with zipfile.ZipFile(dst/'R2B01-Boomerang-model.zip','w',zipfile.ZIP_DEFLATED,compresslevel=7) as z:
    for f in (source,readme):z.write(f,f.name)
with zipfile.ZipFile(dst/'R2B01-Boomerang-model.zip') as z:
    if z.testzip() is not None:raise RuntimeError('Model archive failed its CRC check.')
files={}
for f in [source,*sorted(dst.iterdir())]:
    if f.is_file() and f.name!='media-manifest.json':
        files[str(f.relative_to(root)).replace('\\','/')]={'bytes':f.stat().st_size,'sha256':hashlib.sha256(f.read_bytes()).hexdigest()}
manifest={'source':str(source.relative_to(root)).replace('\\','/'),'files':files,'seconds':8,'fps':12,
          'frames':96,'dimensions':[640,640],'mobile_dimensions':[480,480],'audio':'silent',
          'render':'Real Blender CPU Cycles geometry with an actual small-angle pivot; no imagegen frame or image warping.',
          'modifier_preview':'Last 3 seconds switch primary value to green4 then red2; illustrative material states, not a played match.',
          'decode_passed':True,'zip_crc_passed':True,'unity_integrated':False,'user_art_approval':False}
(dst/'media-manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf8',newline='\n')
print(json.dumps({f.name:f.stat().st_size for f in dst.iterdir() if f.is_file()},ensure_ascii=False))
