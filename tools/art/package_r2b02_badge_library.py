"""Package compact phone media from the saved R2B02 Blender renders."""
import argparse, hashlib, json, subprocess, zipfile
from pathlib import Path

p=argparse.ArgumentParser();p.add_argument('--root',type=Path,required=True)
a=p.parse_args();root=a.root.resolve()
src=root/'artifacts/r2b02-badges/final'
dst=root/'docs/ui3d/samples/r2b02-badges-20261009';dst.mkdir(parents=True,exist_ok=True)
ff=root/'artifacts/video-tools/imageio_ffmpeg/binaries/ffmpeg-win-x86_64-v7.1.exe'

def run(*args):
    subprocess.run([str(ff),'-hide_banner','-loglevel','error','-y',*map(str,args)],check=True)

validation=json.loads((src/'geometry-validation.json').read_text(encoding='utf8'))
if not validation['passed']:raise RuntimeError('Model geometry checks must pass before packaging.')
mapping={
    '00_CORRECTED_BOOMERANG':'00-complete-button',
    '01_BOOT_AND_HOURGLASS':'01-boot-hourglass',
    '02_SEVEN_CORE_BADGES':'02-seven-core',
    '03_CARD_COLORS_AND_MAGIC':'03-colors-magic',
    '04_COMPLETE_NUMERIC_FORMS':'04-incised-numerals',
    '05_ACTION_AND_HISTORY_MARKS':'05-action-marks',
}
for source,target in mapping.items():
    run('-i',src/(source+'.png'),'-vf','scale=900:-2','-frames:v','1','-q:v','2',dst/(target+'.jpg'))
frames=src/'frames'
if {f.name for f in frames.glob('*.png')}!={f'{i:04d}.png' for i in range(48)}:
    raise RuntimeError('Expected exactly 48 consecutive actual rendered frames.')
run('-framerate','12','-i',frames/'%04d.png','-an','-c:v','libx264','-preset','slow','-crf','22',
    '-pix_fmt','yuv420p','-movflags','+faststart',dst/'R2B02-Badges.mp4')
run('-i',dst/'R2B02-Badges.mp4','-vf','scale=480:400','-an','-c:v','libx264','-preset','slow','-crf','26',
    '-pix_fmt','yuv420p','-movflags','+faststart',dst/'R2B02-Badges-mobile.mp4')
for name in ('R2B02-Badges.mp4','R2B02-Badges-mobile.mp4'):
    run('-i',dst/name,'-f','null','NUL')
for name in ('model-report.json','geometry-validation.json'):
    (dst/name).write_text((src/name).read_text(encoding='utf8'),encoding='utf8',newline='\n')
source=root/'art/production/samples/R2B02-Badges/R2B02_SemanticBadges.blend'
with zipfile.ZipFile(dst/'R2B02-Badges-model.zip','w',zipfile.ZIP_DEFLATED,compresslevel=7) as z:
    for f in (source,source.with_name('README.md')):z.write(f,f.name)
with zipfile.ZipFile(dst/'R2B02-Badges-model.zip') as z:
    if z.testzip() is not None:raise RuntimeError('Model ZIP failed its CRC check.')
files={}
for f in [source,*sorted(dst.iterdir())]:
    if f.is_file() and f.name!='media-manifest.json':
        files[f.relative_to(root).as_posix()]={'bytes':f.stat().st_size,'sha256':hashlib.sha256(f.read_bytes()).hexdigest()}
manifest={'source':source.relative_to(root).as_posix(),'files':files,'seconds':4,'fps':12,'frames':48,
          'dimensions':[720,600],'mobile_dimensions':[480,400],'audio':'silent',
          'render':'Real saved Blender geometry rendered with CPU Cycles; actual small-angle pivot, no generated-image substitution.',
          'decode_passed':True,'zip_crc_passed':True,'unity_integrated':False,'user_art_approval':False}
(dst/'media-manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf8',newline='\n')
print(json.dumps({f.name:f.stat().st_size for f in dst.iterdir() if f.is_file()},ensure_ascii=False))
