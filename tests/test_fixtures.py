from pathlib import Path
import hashlib, json, shutil, sys, tempfile, unittest

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'tools'))
from validate import ValidationError, read, validate_fixtures


class HistoricalFixtureTests(unittest.TestCase):
    def setUp(self):
        scratch = (ROOT / 'artifacts/tests').resolve()
        scratch.mkdir(parents=True, exist_ok=True)
        self.temp = tempfile.TemporaryDirectory(prefix='fixture-test-', dir=scratch)
        self.root = Path(self.temp.name).resolve()
        if not self.root.is_relative_to(scratch):
            raise RuntimeError('Temporary fixture workspace is outside its intended directory')
        self.addCleanup(self.temp.cleanup)
        shutil.copytree(ROOT / 'tests/fixtures', self.root / 'tests/fixtures')
        self.manifest_path = self.root / 'tests/fixtures/manifest.json'
        self.manifest = read(self.manifest_path)

    def write_manifest(self):
        self.manifest_path.write_text(json.dumps(self.manifest), encoding='utf-8')

    def test_current_captured_files_and_versions_match_the_manifest(self):
        report = validate_fixtures(self.root)
        self.assertEqual(report, {'frozen_fixtures': 78, 'frozen_files': 155})
        self.assertEqual(sum(entry['engine_version'] == 9 for entry in self.manifest['fixtures']), 2)

    def test_semantically_equivalent_save_whitespace_still_changes_captured_bytes(self):
        path = self.root / self.manifest['fixtures'][0]['save']
        path.write_bytes(path.read_bytes() + b'\n')
        with self.assertRaises(ValidationError):
            validate_fixtures(self.root)

    def test_git_style_newline_conversion_of_a_captured_input_is_rejected(self):
        path = self.root / 'tests/fixtures/engine4-barrier-scenario.json'
        original = path.read_bytes()
        self.assertIn(b'\r\n', original)
        path.write_bytes(original.replace(b'\r\n', b'\n'))
        with self.assertRaises(ValidationError):
            validate_fixtures(self.root)

    def test_unregistered_historical_file_cannot_escape_the_guard(self):
        source = self.root / self.manifest['fixtures'][0]['save']
        shutil.copyfile(source, self.root / 'tests/fixtures/engine99-unregistered.json')
        with self.assertRaises(ValidationError):
            validate_fixtures(self.root)

    def test_engine_metadata_must_match_the_original_save(self):
        self.manifest['fixtures'][2]['engine_version'] = 3
        self.write_manifest()
        with self.assertRaises(ValidationError):
            validate_fixtures(self.root)

    def test_boolean_false_is_not_legacy_engine_zero_even_with_a_matching_digest(self):
        entry = self.manifest['fixtures'][0]
        path = self.root / entry['save']
        state = read(path)
        state['EngineVersion'] = False
        path.write_text(json.dumps(state), encoding='utf-8')
        entry['save_sha256'] = hashlib.sha256(path.read_bytes()).hexdigest()
        self.write_manifest()
        with self.assertRaises(ValidationError):
            validate_fixtures(self.root)

    def test_manifest_cannot_read_outside_the_fixture_directory(self):
        self.manifest['fixtures'][0]['save'] = '../outside.json'
        self.write_manifest()
        with self.assertRaises(ValidationError):
            validate_fixtures(self.root)

    def test_duplicate_fixture_entries_do_not_count_as_coverage(self):
        self.manifest['fixtures'].append(self.manifest['fixtures'][0])
        self.write_manifest()
        with self.assertRaises(ValidationError):
            validate_fixtures(self.root)

    def test_missing_input_reference_does_not_hide_an_existing_capture(self):
        self.manifest['fixtures'][3]['input'] = None
        self.manifest['fixtures'][3]['input_sha256'] = None
        self.write_manifest()
        with self.assertRaises(ValidationError):
            validate_fixtures(self.root)


if __name__ == '__main__':
    unittest.main()
