"""Pinned, replaceable EasyTier component; personal configuration is never bundled."""
import argparse
from concurrent.futures import ThreadPoolExecutor
import hashlib
import io
import json
from pathlib import Path
import re
import shutil
import tarfile
import time
import tomllib
import urllib.request
import zipfile

VERSION = '2.6.4'
BINARY_URL = 'https://github.com/EasyTier/EasyTier/releases/download/v2.6.4/easytier-windows-x86_64-v2.6.4.zip'
BINARY_SHA = '27af91e270e554709b048bd32327fefd2dfce5062ae1e8701af7550c6f525f84'
SOURCE_URL = 'https://codeload.github.com/EasyTier/EasyTier/zip/refs/tags/v2.6.4'
SOURCE_SHA = 'b08ecc378b7ad679b3f4188fa5b9f6417670b5e3c1e5f53b2f19d06c021814cb'
SOURCE_RELEASE = 'https://github.com/yangshuang112358-crypto/GoA2/releases/download/invite-20261003-engine97/Goa2-EasyTier-2.6.4-Sources.zip'

def digest(p):
    return hashlib.sha256(p.read_bytes()).hexdigest()

def fetch(url, target, expected=None):
    target.parent.mkdir(parents=True, exist_ok=True)
    if not target.exists():
        for attempt in range(4):
            try:
                with urllib.request.urlopen(url, timeout=45) as response:
                    data=response.read()
                if expected and hashlib.sha256(data).hexdigest()!=expected:
                    raise ValueError('Download checksum mismatch: '+target.name)
                target.write_bytes(data)
                break
            except Exception:
                if attempt==3: raise
                time.sleep(attempt+1)
    if expected and digest(target)!=expected: raise ValueError('Cached checksum mismatch: '+target.name)
    return target

def prepare(root, sources=False):
    cache=root/'artifacts/easytier-distribution'; cache.mkdir(parents=True,exist_ok=True)
    binary=fetch(BINARY_URL,cache/'easytier-windows-x86_64-v2.6.4.zip',BINARY_SHA)
    source=fetch(SOURCE_URL,cache/'EasyTier-v2.6.4-source.zip',SOURCE_SHA)
    out=cache/'component';out.mkdir(exist_ok=True)
    with zipfile.ZipFile(binary) as z:
        for name in ('easytier-cli.exe',):
            (out/name).write_bytes(z.read('easytier-windows-x86_64/'+name))
    custom_lock=json.loads((root/'network/easytier-userspace.lock.json').read_text())
    custom=fetch(custom_lock['Url'],cache/'Goa2-EasyTier-2.6.4-userspace.zip',custom_lock['Sha256'])
    with zipfile.ZipFile(custom) as z:
        for name in ('easytier-core.exe','Goa2-EasyTier-2.6.4-modified-source.zip'):
            (out/name).write_bytes(z.read(name))
    modified_source=out/'Goa2-EasyTier-2.6.4-modified-source.zip'
    with zipfile.ZipFile(source) as z:
        (out/'LICENSE-LGPL-3.0.txt').write_bytes(z.read('EasyTier-2.6.4/LICENSE'))
        lock=tomllib.loads(z.read('EasyTier-2.6.4/Cargo.lock').decode())
        source_only=cache/'EasyTier-v2.6.4-upstream-source-only.zip'
        with zipfile.ZipFile(source_only,'w',zipfile.ZIP_DEFLATED) as target:
            for info in z.infolist():
                if not info.is_dir() and Path(info.filename).suffix.lower() not in ('.dll','.lib','.sys','.exe'):
                    target.writestr(info,z.read(info.filename))
    gpl=fetch('https://raw.githubusercontent.com/spdx/license-list-data/main/text/GPL-3.0-or-later.txt',cache/'LICENSE-GPL-3.0.txt')
    shutil.copy2(gpl,out/gpl.name)
    # Remove only the obsolete known cache file; never distribute upstream capture DLLs inside source archives.
    if (out/source.name).exists(): (out/source.name).unlink()
    (out/'SOURCE-AND-REPLACEMENT.txt').write_text(
        'EasyTier '+VERSION+' is an independent LGPL-3.0 component, modified for Goa2 userspace transport.\n'
        'It is not linked into Goa2 rule or game assemblies. No EasyTier drivers are shipped or installed.\n'
        'Upstream: https://github.com/EasyTier/EasyTier/tree/v2.6.4\n'
        'Exact modified source, Cargo.lock, dependency patch and build instructions are included in Goa2-EasyTier-2.6.4-modified-source.zip.\n'
        'The co-released complete source bundle includes locked registry crates and git dependencies:\n'+SOURCE_RELEASE+'\n'
        'To build/modify: extract upstream source, follow its README and .github/workflows Windows x86_64 builds, using Cargo.lock.\n'
        'You may replace the two executables for your private use. Update their SHA256 entries in component.json accordingly.\n'
        'This integrity manifest is not a restriction on replacing or debugging LGPL components.\n'
        'Goa2 source is https://github.com/yangshuang112358-crypto/GoA2\n',encoding='utf-8')
    if sources:
        packages=[p for p in lock['package'] if p.get('source','').startswith('registry+')]
        def crate(p):
            name=f"{p['name']}-{p['version']}.crate"
            f=fetch(f"https://static.crates.io/crates/{p['name']}/{name}",cache/'dependencies'/name,p['checksum'])
            notices=[]
            with tarfile.open(f) as tar:
                for member in tar.getmembers():
                    if member.isfile() and re.search(r'(?i)(^|/)(license|licence|copying|notice|authors)([./_-]|$)',member.name) and member.size<1048576:
                        data=tar.extractfile(member).read().decode('utf-8',errors='replace')
                        notices.append('\n===== '+name+' / '+member.name+' =====\n'+data)
            return ''.join(notices)
        with ThreadPoolExecutor(max_workers=12) as pool:
            notices=list(pool.map(crate,packages))
        git_sources=sorted({p['source'] for p in lock['package'] if p.get('source','').startswith('git+')})
        # GitHub source archives omit git submodule contents. Pin those native sources too.
        git_sources += [
            'git+https://github.com/skywind3000/kcp#7f9805887b0909c52c825925f123e7a84da37167',
            'git+https://github.com/EasyTier/WinDivert#b90fad29446667c6230accd078e91a77074b7788',
        ]
        for item in git_sources:
            repo,commit=item[4:].split('#');repo=repo.split('?')[0].removesuffix('.git')
            slug=repo.removeprefix('https://github.com/')
            f=fetch('https://codeload.github.com/'+slug+'/zip/'+commit,cache/'dependencies'/(slug.replace('/','-')+'-'+commit+'.zip'))
            with zipfile.ZipFile(f) as z:
                for name in z.namelist():
                    if not name.endswith('/') and re.search(r'(?i)(^|/)(license|licence|copying|notice)([./_-]|$)',name):
                        notices.append('\n===== '+slug+' / '+name+' =====\n'+z.read(name).decode('utf-8',errors='replace'))
        (out/'DEPENDENCY-NOTICES.txt').write_text('License texts from the upstream locked workspace dependencies (superset of the Windows core build).\n'+''.join(notices),encoding='utf-8')
        bundle=cache/'Goa2-EasyTier-2.6.4-Sources.zip'
        with zipfile.ZipFile(bundle,'w',zipfile.ZIP_DEFLATED,compresslevel=1) as z:
            z.write(modified_source,modified_source.name)
            z.write(source_only,source_only.name)
            for f in sorted((cache/'dependencies').iterdir()): z.write(f,'dependencies/'+f.name)
            for f in out.glob('*.txt'):z.write(f,f.name)
        print('Source bundle:',bundle.name,bundle.stat().st_size,digest(bundle),flush=True)
    if not (out/'DEPENDENCY-NOTICES.txt').exists():
        raise RuntimeError('Run prepare_easytier.py --sources once to collect dependency license notices and co-release source bundle.')
    files=[{'Name':p.name,'Sha256':digest(p)} for p in sorted(out.iterdir()) if p.name!='component.json']
    (out/'component.json').write_text(json.dumps({'Version':VERSION,'Variant':'Goa2-userspace-1','ReleaseSha256':BINARY_SHA,'UpstreamSourceSha256':SOURCE_SHA,'UserspaceBuild':custom_lock,'Files':files},indent=2),encoding='utf-8')
    return out

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--sources',action='store_true');args=parser.parse_args()
    print(prepare(Path(__file__).resolve().parents[1],args.sources))
