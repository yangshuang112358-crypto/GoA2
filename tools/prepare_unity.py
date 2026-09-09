"""Stage canonical content into Unity; generated files are never a second source."""
from pathlib import Path
import argparse
import hashlib
import json
import shutil

ROOT = Path(__file__).resolve().parents[1]

def prepare(root=ROOT):
    manifest = json.loads((root / 'content/manifest.json').read_text(encoding='utf-8'))
    entries = manifest['files']
    expected = {'content/canonical/' + name + '.json' for name in ('cards', 'heroes', 'map', 'ruleset')}
    if {entry['path'] for entry in entries} != expected or len(entries) != 4:
        raise ValueError('Manifest must contain exactly the four canonical files')
    for entry in entries:
        source = root / entry['path']
        if hashlib.sha256(source.read_bytes()).hexdigest() != entry['sha256']:
            raise ValueError('Content hash mismatch: ' + entry['path'])
    destination = root / 'unity/Assets/StreamingAssets/Goa2'
    for relative in ['content/manifest.json', *sorted(expected)]:
        target = destination / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(root / relative, target)
    return destination

if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--root', type=Path, default=ROOT)
    args = parser.parse_args()
    print('Staged verified canonical content to', prepare(args.root))
