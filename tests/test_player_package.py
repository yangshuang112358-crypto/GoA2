from pathlib import Path
import hashlib, json, sys, tempfile, unittest, zipfile

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'tools'))
from player_package import PackageError, verify_build, create_package, verify_package


class PlayerPackageTests(unittest.TestCase):
    def setUp(self):
        scratch = (ROOT / 'artifacts/tests').resolve()
        scratch.mkdir(parents=True, exist_ok=True)
        self.temp = tempfile.TemporaryDirectory(prefix='package-test-', dir=scratch)
        self.root = Path(self.temp.name).resolve()
        if not self.root.is_relative_to(scratch):
            raise RuntimeError('Package fixture is outside its intended directory')
        self.addCleanup(self.temp.cleanup)
        self.player = self.root / 'player'
        self.player.mkdir()
        self.source = self.root / 'source'
        self.source.mkdir()
        self.rule_path = 'core/com.goa2.core/Runtime/rules.cs'
        (self.source / self.rule_path).parent.mkdir(parents=True)
        (self.source / self.rule_path).write_text('rule source', encoding='utf-8')
        self.paths = ['Goa2V1.exe', 'UnityPlayer.dll', 'Goa2V1_Data/globalgamemanagers',
                      *['Goa2V1_Data/Managed/Goa2.'+part+'.dll' for part in ['Domain', 'Rules', 'Application', 'Infrastructure', 'Presentation']]]
        for relative in self.paths:
            target = self.player / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(('fixture:'+relative).encode())
        self.info = {'SchemaVersion': 1, 'EngineVersion': 7, 'UnityVersion': '6000.3.23f1',
                     'ContentHash': 'a'*64, 'ProtocolVersion': '1.0.0', 'RulesVersion': 'fixture',
                     'BuiltUtc': '2026-09-10T09:00:00Z', 'PrimaryCards': ['fixture-primary'],
                     'DefenseCards': ['fixture-defense'],
                     'Files': [self.record(self.player, path) for path in self.paths],
                     'SourceFiles': [self.record(self.source, self.rule_path)]}
        self.write_info()

    @staticmethod
    def record(root, relative):
        data = (root / relative).read_bytes()
        return {'Path': relative, 'Bytes': len(data), 'Sha256': hashlib.sha256(data).hexdigest()}

    def write_info(self):
        (self.player / 'build-info.json').write_text(json.dumps(self.info), encoding='utf-8')

    def test_valid_inventory_can_be_packaged_and_verified_without_source_paths_in_the_zip(self):
        result = verify_build(self.player, self.source)
        self.assertEqual(result['engine_version'], 7)
        archive = self.root / 'player.zip'
        create_package(self.player, archive, self.source)
        self.assertEqual(verify_package(archive)['payload_files'], len(self.paths))
        with zipfile.ZipFile(archive) as zipped:
            self.assertEqual(set(zipped.namelist()), {'Goa2V1/'+p for p in self.paths+['build-info.json']})

    def test_payload_changes_are_rejected_before_packaging(self):
        (self.player / 'Goa2V1.exe').write_bytes(b'changed')
        with self.assertRaises(PackageError):
            verify_build(self.player, self.source)

    def test_missing_or_unlisted_payload_is_rejected(self):
        (self.player / 'local-save.json').write_text('{}', encoding='utf-8')
        with self.assertRaises(PackageError):
            verify_build(self.player)
        (self.player / 'local-save.json').unlink()
        (self.player / 'Goa2V1.exe').unlink()
        with self.assertRaises(PackageError):
            verify_build(self.player)

    def test_source_changes_require_a_new_build(self):
        (self.source / self.rule_path).write_text('different rules', encoding='utf-8')
        with self.assertRaises(PackageError):
            verify_build(self.player, self.source)

    def test_new_source_file_is_not_hidden_by_the_previous_build_inventory(self):
        (self.source / self.rule_path).with_name('new-rule.cs').write_text('new rule', encoding='utf-8')
        with self.assertRaises(PackageError):
            verify_build(self.player, self.source)

    def test_debug_preset_metadata_and_scenario_changes_require_a_rebuild(self):
        inputs = ('tools/debug-positions.json', 'tests/scenarios/fixture.json')
        for relative in inputs:
            path = self.source / relative
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text('{}', encoding='utf-8')
            self.info['SourceFiles'].append(self.record(self.source, relative))
        self.write_info()
        verify_build(self.player, self.source)
        for relative in inputs:
            with self.subTest(relative=relative):
                path = self.source / relative
                path.write_text('{"changed":true}', encoding='utf-8')
                with self.assertRaises(PackageError):
                    verify_build(self.player, self.source)
                path.write_text('{}', encoding='utf-8')

    def test_packaging_does_not_overwrite_an_existing_output(self):
        archive = self.root / 'existing.zip'
        archive.write_bytes(b'existing output')
        with self.assertRaises(PackageError):
            create_package(self.player, archive, self.source)
        self.assertEqual(archive.read_bytes(), b'existing output')

    def test_boolean_is_not_a_numeric_engine_version(self):
        self.info['EngineVersion'] = False
        self.write_info()
        with self.assertRaises(PackageError):
            verify_build(self.player)

    def test_manifest_cannot_include_a_parent_path(self):
        self.info['Files'][0]['Path'] = '../outside.exe'
        self.write_info()
        with self.assertRaises(PackageError):
            verify_build(self.player)

    def test_duplicate_case_insensitive_paths_are_rejected(self):
        self.info['Files'].append({**self.info['Files'][0], 'Path': 'goa2v1.EXE'})
        self.write_info()
        with self.assertRaises(PackageError):
            verify_build(self.player)

    def test_missing_required_game_assembly_cannot_be_hidden_by_editing_the_manifest(self):
        missing = 'Goa2V1_Data/Managed/Goa2.Rules.dll'
        (self.player / missing).unlink()
        self.info['Files'] = [entry for entry in self.info['Files'] if entry['Path'] != missing]
        self.write_info()
        with self.assertRaises(PackageError):
            verify_build(self.player)

    def test_archive_tampering_is_detected_by_internal_manifest(self):
        archive = self.root / 'tampered.zip'
        with zipfile.ZipFile(archive, 'w') as zipped:
            zipped.writestr('Goa2V1/build-info.json', json.dumps(self.info))
            for relative in self.paths:
                zipped.writestr('Goa2V1/'+relative, b'changed' if relative == 'Goa2V1.exe' else (self.player / relative).read_bytes())
        with self.assertRaises(PackageError):
            verify_package(archive)


if __name__ == '__main__':
    unittest.main()
