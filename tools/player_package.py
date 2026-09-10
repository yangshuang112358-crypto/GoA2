"""Build inventory verification and local Windows Player packaging."""
from pathlib import Path, PurePosixPath
import argparse
import hashlib
import json
import re
import zipfile


class PackageError(ValueError):
    pass


def require(condition, message):
    if not condition:
        raise PackageError(message)


def strict_json(data):
    def pairs(items):
        result = {}
        for key, value in items:
            require(key not in result, 'Duplicate JSON field: '+key)
            result[key] = value
        return result
    try:
        return json.loads(data, object_pairs_hook=pairs)
    except (ValueError, UnicodeError) as error:
        raise PackageError('Invalid build inventory JSON: '+str(error)) from error


def safe_relative(value):
    require(type(value) is str and value and '\\' not in value and ':' not in value, 'Invalid relative path')
    path = PurePosixPath(value)
    require(not path.is_absolute() and all(part not in ('', '.', '..') for part in value.split('/')), 'Path escapes inventory root')
    return value


def regular_file(root, relative):
    path = root / safe_relative(relative)
    require(path.resolve().is_relative_to(root.resolve()), 'File escapes inventory root: '+relative)
    for part in [path, *path.parents]:
        if part == root:
            break
        require(not part.is_symlink() and not (hasattr(part, 'is_junction') and part.is_junction()), 'Linked files are not captured: '+relative)
    require(path.is_file(), 'Missing inventory file: '+relative)
    return path


def digest(stream):
    sha = hashlib.sha256()
    for chunk in iter(lambda: stream.read(1024*1024), b''):
        sha.update(chunk)
    return sha.hexdigest()


def file_digest(path):
    with path.open('rb') as stream:
        return digest(stream)


SOURCE_DIRS = ('core/com.goa2.core/Runtime', 'unity/Assets', 'unity/Packages', 'unity/ProjectSettings', 'content/canonical')


def source_paths(root):
    paths = set()
    for directory in SOURCE_DIRS:
        for path in (root / directory).rglob('*'):
            if path.is_file():
                relative = path.relative_to(root).as_posix()
                if relative == 'unity/Assets/StreamingAssets/Goa2.meta' or relative.startswith('unity/Assets/StreamingAssets/Goa2/'):
                    continue
                paths.add(relative)
    for relative in ('content/manifest.json', 'core/com.goa2.core/package.json'):
        if (root / relative).is_file():
            paths.add(relative)
    return paths


def inventory(info):
    fields = {'SchemaVersion', 'EngineVersion', 'UnityVersion', 'ContentHash', 'ProtocolVersion', 'RulesVersion',
              'BuiltUtc', 'PrimaryCards', 'DefenseCards', 'Files', 'SourceFiles'}
    require(type(info) is dict and set(info) == fields, 'Build inventory has missing or unknown fields')
    require(type(info['SchemaVersion']) is int and info['SchemaVersion'] == 1, 'Unknown build inventory version')
    require(type(info['EngineVersion']) is int and info['EngineVersion'] >= 0, 'Invalid engine version')
    require(type(info['ContentHash']) is str and re.fullmatch('[a-f0-9]{64}', info['ContentHash']), 'Invalid content hash')
    for key in ('UnityVersion', 'ProtocolVersion', 'RulesVersion', 'BuiltUtc'):
        require(type(info[key]) is str and info[key], 'Missing build metadata: '+key)
    for key in ('PrimaryCards', 'DefenseCards'):
        require(type(info[key]) is list and all(type(card) is str and card for card in info[key]), 'Invalid capability list')
        require(len(set(info[key])) == len(info[key]), 'Duplicate capability')
    for key in ('Files', 'SourceFiles'):
        require(type(info[key]) is list and 0 < len(info[key]) <= 10000, 'Invalid inventory entries')
        names = set()
        for entry in info[key]:
            require(type(entry) is dict and set(entry) == {'Path', 'Bytes', 'Sha256'}, 'Invalid file record')
            relative = safe_relative(entry['Path'])
            require(relative.casefold() not in names, 'Duplicate path: '+relative)
            names.add(relative.casefold())
            require(type(entry['Bytes']) is int and 0 <= entry['Bytes'] <= 2*1024**3, 'Invalid byte count')
            require(type(entry['Sha256']) is str and re.fullmatch('[a-f0-9]{64}', entry['Sha256']), 'Invalid file hash')
    required = {'Goa2V1.exe', 'UnityPlayer.dll', 'Goa2V1_Data/globalgamemanagers',
                *['Goa2V1_Data/Managed/Goa2.'+part+'.dll' for part in ('Domain', 'Rules', 'Application', 'Infrastructure', 'Presentation')]}
    payload = {entry['Path'] for entry in info['Files']}
    require(required <= payload, 'Required Player files are missing from inventory')
    require('build-info.json' not in payload, 'Inventory cannot include itself')
    require(not any('Tests' in name or name.endswith('.log') or '/saves/' in name or name.endswith('.save.json') for name in payload), 'Test or session output appears in Player inventory')
    return info


def summary(info):
    return {'engine_version': info['EngineVersion'], 'unity_version': info['UnityVersion'],
            'payload_files': len(info['Files']), 'payload_bytes': sum(entry['Bytes'] for entry in info['Files']),
            'primary_cards': len(info['PrimaryCards']), 'defense_cards': len(info['DefenseCards'])}


def verify_build(player, source_root=None):
    player = Path(player).resolve()
    info_path = regular_file(player, 'build-info.json')
    info = inventory(strict_json(info_path.read_text(encoding='utf-8-sig')))
    actual = {path.relative_to(player).as_posix() for path in player.rglob('*') if path.is_file()}
    require(actual == {entry['Path'] for entry in info['Files']} | {'build-info.json'}, 'Player contains missing or unlisted files')
    for entry in info['Files']:
        path = regular_file(player, entry['Path'])
        require(path.stat().st_size == entry['Bytes'] and file_digest(path) == entry['Sha256'], 'Player file changed: '+entry['Path'])
    if source_root is not None:
        source_root = Path(source_root).resolve()
        require(source_paths(source_root) == {entry['Path'] for entry in info['SourceFiles']}, 'Build input file set changed; rebuild before packaging')
        for entry in info['SourceFiles']:
            path = regular_file(source_root, entry['Path'])
            require(path.stat().st_size == entry['Bytes'] and file_digest(path) == entry['Sha256'], 'Build source changed: '+entry['Path'])
    return summary(info)


def create_package(player, destination, source_root=None):
    player, destination = Path(player).resolve(), Path(destination).resolve()
    result = verify_build(player, source_root)
    require(not destination.is_relative_to(player), 'Package destination must be outside Player directory')
    require(not destination.exists(), 'Package already exists; choose a new output name')
    destination.parent.mkdir(parents=True, exist_ok=True)
    info = inventory(strict_json((player / 'build-info.json').read_text(encoding='utf-8-sig')))
    with zipfile.ZipFile(destination, 'x', compression=zipfile.ZIP_DEFLATED, compresslevel=6) as zipped:
        for relative in sorted([entry['Path'] for entry in info['Files']] + ['build-info.json']):
            zipped.write(regular_file(player, relative), 'Goa2V1/'+relative)
    require(verify_package(destination) == result, 'Package verification differs from source inventory')
    return {**result, 'archive': str(destination), 'archive_sha256': file_digest(destination)}


def verify_package(archive):
    try:
        with zipfile.ZipFile(archive) as zipped:
            names = zipped.namelist()
            require(len(names) == len({name.casefold() for name in names}), 'Duplicate archive entry')
            for name in names:
                safe_relative(name)
                require(name.startswith('Goa2V1/'), 'Unexpected archive root')
            require('Goa2V1/build-info.json' in names, 'Archive has no build inventory')
            require(zipped.getinfo('Goa2V1/build-info.json').file_size <= 4*1024**2, 'Build inventory is too large')
            info = inventory(strict_json(zipped.read('Goa2V1/build-info.json')))
            require(set(names) == {'Goa2V1/'+entry['Path'] for entry in info['Files']} | {'Goa2V1/build-info.json'}, 'Archive file set differs from build inventory')
            for entry in info['Files']:
                name = 'Goa2V1/'+entry['Path']
                require(zipped.getinfo(name).file_size == entry['Bytes'], 'Archive file size changed: '+name)
                with zipped.open(name) as stream:
                    require(digest(stream) == entry['Sha256'], 'Archive file changed: '+name)
            return summary(info)
    except (zipfile.BadZipFile, KeyError, OSError) as error:
        raise PackageError('Cannot verify Player archive: '+str(error)) from error


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest='command', required=True)
    build = sub.add_parser('verify-build')
    build.add_argument('player', type=Path)
    build.add_argument('--source-root', type=Path)
    package = sub.add_parser('create')
    package.add_argument('player', type=Path)
    package.add_argument('destination', type=Path)
    package.add_argument('--source-root', type=Path, required=True)
    verify = sub.add_parser('verify-archive')
    verify.add_argument('archive', type=Path)
    args = parser.parse_args()
    try:
        if args.command == 'create':
            result = create_package(args.player, args.destination, args.source_root)
        elif args.command == 'verify-build':
            result = verify_build(args.player, args.source_root)
        else:
            result = verify_package(args.archive)
        print(json.dumps({'result': 'PASS', **result}, ensure_ascii=False, indent=2))
    except (PackageError, OSError) as error:
        parser.exit(1, 'Package verification failed: '+str(error)+'\n')


if __name__ == '__main__':
    main()
