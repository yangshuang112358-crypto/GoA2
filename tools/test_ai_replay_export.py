"""Failure-path coverage; real game replay is separately checked against saved cases."""
from contextlib import ExitStack
import importlib.util
from pathlib import Path
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import MagicMock, patch

import psutil
from ai_replay_export import export_replay, evaluation_summary, run_bounded, read, write


class ReplayExportTests(unittest.TestCase):
    def fixture(self, base, count=2):
        source = base/'authority'; source.mkdir()
        write(source/'commands.json', {'Steps': [{}]*count})
        write(source/'contract.json', {})
        write(source/'result.json', {'Stop': 'terminated'})
        (source/'final-save.json').write_text('{}', encoding='utf-8')
        return source, base/'replays'/'case'

    def test_capacity_failure_keeps_original_and_records_error(self):
        with tempfile.TemporaryDirectory() as d:
            source, dest = self.fixture(Path(d), 1001)
            before = (source/'commands.json').read_bytes()
            with patch('ai_replay_export.run_bounded') as run:
                with self.assertRaisesRegex(ValueError, 'capacity'):
                    export_replay(Path(d), Path('dotnet'), source, dest, 1, lambda: None)
            run.assert_not_called()
            self.assertEqual((source/'commands.json').read_bytes(), before)
            self.assertEqual(read(dest.parent/'case.attempt.json')['status'], 'failed')

    def test_failed_attempt_cannot_be_overwritten(self):
        with tempfile.TemporaryDirectory() as d:
            source, dest = self.fixture(Path(d), 1001)
            for expected in (ValueError, FileExistsError):
                with self.assertRaises(expected):
                    export_replay(Path(d), Path('dotnet'), source, dest, 1, lambda: None)

    def test_success_exit_without_matching_evidence_is_rejected(self):
        with tempfile.TemporaryDirectory() as d:
            source, dest = self.fixture(Path(d))
            def fake_run(*args):
                dest.mkdir()
                write(dest/'result.json', {'ScenarioVerified': True, 'StateHash': 'wrong', 'Stop': 'terminated'})
                write(dest/'scenario-report.json', {'Passed': True, 'Complete': True, 'TotalSteps': 2})
            with patch('ai_replay_export.run_bounded', side_effect=fake_run):
                with self.assertRaisesRegex(ValueError, 'evidence mismatch'):
                    export_replay(Path(d), Path('dotnet'), source, dest, 1, lambda: None)

    def test_resource_guard_prevents_launch(self):
        with tempfile.TemporaryDirectory() as d:
            with patch('ai_replay_export.subprocess.Popen') as launch:
                with self.assertRaises(MemoryError):
                    run_bounded([], Path(d)/'log', 1, lambda: (_ for _ in ()).throw(MemoryError('guard')))
            launch.assert_not_called()

    def test_timeout_preserves_stdout_and_stops_owned_child(self):
        with tempfile.TemporaryDirectory() as d:
            log = Path(d)/'timeout.log'
            with self.assertRaises(TimeoutError):
                run_bounded([sys.executable, '-u', '-c', 'import time; print("before timeout"); time.sleep(60)'],
                            log, 1, lambda: None)
            info = read(log.with_suffix('.process.json'))
            self.assertFalse(psutil.pid_exists(info['pid']))
            self.assertIn('before timeout', log.read_text())

    def test_nonzero_exit_is_not_success(self):
        with tempfile.TemporaryDirectory() as d:
            with self.assertRaisesRegex(RuntimeError, 'exit 7'):
                run_bounded([sys.executable, '-c', 'raise SystemExit(7)'], Path(d)/'log', 5, lambda: None)

    def test_truncation_is_not_terminal_or_win_and_export_failure_is_separate(self):
        result = dict(terminated=False, truncated=True, reward=0, environment_steps=1201, learner_decisions=304)
        s = evaluation_summary([result], 1, 10, [], 'failed', replay_exceptions=1)
        self.assertTrue(s['gameplay_complete'])
        self.assertFalse(s['pipeline_complete'])
        self.assertEqual((s['terminated'], s['truncated'], s['team_wins'], s['exceptions'], s['replay_exceptions']), (0, 1, 0, 0, 1))


class EvaluatorPersistenceTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        path = Path(__file__).with_name('evaluate-ai-checkpoint.py')
        spec = importlib.util.spec_from_file_location('evaluate_under_test', path)
        cls.module = importlib.util.module_from_spec(spec); spec.loader.exec_module(cls.module)

    def exercise(self, output, run_episode, exporter):
        m = self.module; checkpoint = output.parent/'fixture.pt'; checkpoint.write_bytes(b'not a real model')
        host = MagicMock(); host.last_reply = {'fixture': 'private decision'}
        host.call.return_value = {}; bridge = MagicMock(); bridge.__enter__.return_value = host
        # The fake host creates only terminal authority markers needed by the export loop.
        def episode(*args, **kwargs):
            row = run_episode()
            directory = output/'authority'/('train-%d'%len(list((output/'authority').glob('*'))))
            directory.mkdir(parents=True); write(directory/'result.json', {'Stop': 'terminated'})
            return row
        with ExitStack() as stack:
            for name, value in dict(Bridge=lambda *a: bridge, Encoder=lambda *a: SimpleNamespace(contract={}),
                    configure=lambda *a: SimpleNamespace(type='cpu'), require_interactive_memory=lambda: None,
                    ResourceMonitor=lambda: SimpleNamespace(report=lambda: {}),
                    load_checkpoint=lambda *a: (SimpleNamespace(eval=lambda: None, shape={}, parameters=lambda: []), {'config': {}}),
                    run_episode=episode, export_replay=exporter).items():
                stack.enter_context(patch.object(m, name, value))
            stack.enter_context(patch.object(sys, 'argv', ['evaluate', '--checkpoint', str(checkpoint),
                '--output', str(output), '--pairs', '2', '--export-replays']))
            m.main()

    def test_complete_results_exist_before_export_and_survive_timeout(self):
        with tempfile.TemporaryDirectory() as d:
            output = Path(d)/'evaluation'
            def exporter(*args):
                summary = read(output/'summary.json')
                self.assertTrue(summary['gameplay_complete'])
                self.assertEqual(summary['games'], 4)
                raise TimeoutError('original replay timeout preserved')
            row = dict(terminated=True, truncated=False, reward=1, environment_steps=10, learner_decisions=2)
            with self.assertRaises(TimeoutError):
                self.exercise(output, lambda: row, exporter)
            s = read(output/'summary.json')
            self.assertEqual((s['games'], s['team_wins'], s['exceptions'], s['replay_exceptions']), (4, 4, 0, 1))
            self.assertEqual(s['replay_status'], 'failed')
            self.assertFalse(s['pipeline_complete'])
            self.assertFalse((output/'failure-policy-input.json').exists())

    def test_gameplay_error_is_preserved_without_fabricated_results(self):
        with tempfile.TemporaryDirectory() as d:
            output = Path(d)/'evaluation'
            with self.assertRaisesRegex(RuntimeError, 'rule failure'):
                self.exercise(output, lambda: (_ for _ in ()).throw(RuntimeError('rule failure')), MagicMock())
            s = read(output/'summary.json')
            self.assertEqual((s['games'], s['exceptions'], s['replay_exceptions']), (0, 1, 0))
            self.assertFalse(s['gameplay_complete'])
            self.assertTrue((output/'failure-policy-input.json').exists())


if __name__ == '__main__':
    unittest.main()
