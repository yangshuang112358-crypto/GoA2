"""Safety checks for unattended orchestration; no game rules mocked as validation."""
import hashlib
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import sys
import time
from types import SimpleNamespace

from ai_unattended import Runner, completed_checkpoint, exclusive_lock, may_start_batch, save, training_command, resume_metadata, GIB


class UnattendedTests(unittest.TestCase):
    def test_deadline_reserves_training_and_final_evaluation(self):
        self.assertFalse(may_start_batch(0, 3420))
        self.assertTrue(may_start_batch(0, 3421))

    def test_resume_preserves_seed_and_configuration(self):
        first = training_command(Path('root'), Path('one'))
        resumed = training_command(Path('root'), Path('two'), Path('checkpoint.pt'))
        for option in ('--seed', '--limit', '--rollout', '--architecture', '--epochs', '--batch-size'):
            self.assertEqual(first[first.index(option)+1], resumed[resumed.index(option)+1])
        self.assertEqual(resumed[-2:], ['--resume', 'checkpoint.pt'])

    def fixture(self, directory):
        checkpoint = directory / 'checkpoint-001.pt'
        checkpoint.write_bytes(b'test fixture, never loaded as a model')
        save(directory / 'summary.json', {'checkpoint_inference_equal': True, 'learner_samples': 128})
        save(directory / 'checkpoint-001.pt.evaluation.json',
             {'checkpoint_sha256': hashlib.sha256(checkpoint.read_bytes()).hexdigest(), 'results': [{}, {}]})
        return checkpoint

    def test_failed_batch_is_never_promoted(self):
        with tempfile.TemporaryDirectory() as d:
            directory = Path(d); self.fixture(directory)
            save(directory / 'failure.json', {'error': 'memory failure'})
            with self.assertRaisesRegex(RuntimeError, 'failure'):
                completed_checkpoint(directory)

    def test_checkpoint_corruption_is_rejected(self):
        with tempfile.TemporaryDirectory() as d:
            directory = Path(d); checkpoint = self.fixture(directory)
            checkpoint.write_bytes(b'changed')
            with self.assertRaisesRegex(RuntimeError, 'hash mismatch'):
                completed_checkpoint(directory)

    def test_atomic_status_and_verified_checkpoint(self):
        with tempfile.TemporaryDirectory() as d:
            directory = Path(d); expected = self.fixture(directory)
            actual, summary = completed_checkpoint(directory)
            self.assertEqual(actual, expected)
            self.assertEqual(summary['learner_samples'], 128)
            self.assertFalse(list(directory.glob('*.partial')))

    def test_duplicate_worker_lock_rejected_and_released(self):
        with tempfile.TemporaryDirectory() as d:
            path = Path(d) / 'worker.lock'
            with exclusive_lock(path):
                with self.assertRaises(OSError):
                    with exclusive_lock(path):
                        self.fail('duplicate worker acquired lock')
            with exclusive_lock(path):
                pass

    def test_waiting_stop_file_prevents_launch(self):
        with tempfile.TemporaryDirectory() as d:
            runner = Runner(Path(d) / 'run', 1)
            (runner.output / 'STOP').touch()
            self.assertFalse(runner.wait_resources(time.time()+30))

    def test_changed_source_blocks_launch(self):
        with tempfile.TemporaryDirectory() as d:
            runner = Runner(Path(d) / 'run', 1)
            with patch('ai_unattended.fingerprint', return_value={}):
                with self.assertRaisesRegex(RuntimeError, 'source/authority changed'):
                    runner.child([sys.executable, '-c', 'print(1)'], 'must-not-start', 2)
            self.assertFalse((runner.output / 'must-not-start.command.json').exists())

    def test_real_child_failure_stops_and_keeps_logs(self):
        with tempfile.TemporaryDirectory() as d:
            runner = Runner(Path(d) / 'run', 1)
            with self.assertRaisesRegex(RuntimeError, 'exit 7'):
                runner.child([sys.executable, '-c', 'print("preserved"); raise SystemExit(7)'], 'failed-child', 15)
            self.assertIn('preserved', (runner.output / 'failed-child.stdout.log').read_text())
            self.assertIsNone(runner.state['child_pid'])

    def test_real_hung_child_is_stopped_at_deadline(self):
        with tempfile.TemporaryDirectory() as d:
            runner = Runner(Path(d) / 'run', 1)
            with self.assertRaises(TimeoutError):
                runner.child([sys.executable, '-c', 'import time; time.sleep(60)'], 'hung-child', 0)
            import psutil
            pid = runner.state['child_pid']
            self.assertFalse(psutil.pid_exists(pid))

    def resume_fixture(self, directory):
        root = directory / 'source'
        trainer = root / 'ai/trainer'; trainer.mkdir(parents=True)
        authority = root / 'ai/Goa2.Ai.Cli/bin/Release/net10.0'; authority.mkdir(parents=True)
        (trainer / 'train.py').write_text('fixture only')
        (authority / 'Goa2.Rules.dll').write_bytes(b'fixture only')
        batch = directory / 'previous'; batch.mkdir()
        checkpoint = self.fixture(batch)
        save(Path(str(checkpoint) + '.json'), dict(format=2, boundary='episode', iteration=6, cursor=8,
            config=dict(seed=270140, python_files={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in trainer.iterdir()},
                        authority_assemblies={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in authority.iterdir()})))
        return root, checkpoint

    def test_resume_uses_interrupted_seed_and_restores_command(self):
        with tempfile.TemporaryDirectory() as d:
            root, checkpoint = self.resume_fixture(Path(d))
            with patch('ai_unattended.ROOT', root), patch('ai_unattended.fingerprint', return_value={}), \
                 patch('ai_unattended.subprocess.check_output', return_value='fixture-commit'):
                runner = Runner(Path(d)/'new', 8, checkpoint)
            self.assertEqual(runner.state['resume']['next_seed'], 270144)
            self.assertEqual(runner.state['resume']['next_swap'], 0)
            with patch('ai_unattended.may_start_batch', side_effect=[True, True]), \
                 patch.object(runner, 'wait_resources', return_value=True), \
                 patch.object(runner, 'child', side_effect=RuntimeError('test stops after launch capture')) as child:
                with self.assertRaises(RuntimeError):
                    runner.run()
            self.assertEqual(child.call_args.args[0][-2:], ['--resume', str(checkpoint.resolve())])

    def test_resume_rejects_changed_trainer_source(self):
        with tempfile.TemporaryDirectory() as d:
            root, checkpoint = self.resume_fixture(Path(d))
            (root/'ai/trainer/train.py').write_text('changed')
            with self.assertRaisesRegex(ValueError, 'provenance mismatch'):
                resume_metadata(root, checkpoint)

    def test_memory_requires_consecutive_headroom_and_clears_wait_reason(self):
        with tempfile.TemporaryDirectory() as d:
            runner = Runner(Path(d)/'new', 1)
            readings=iter([6.5, 6.5, 5.9, 6.5, 6.5, 6.5])
            def memory():
                return SimpleNamespace(available=next(readings)*GIB)
            with patch('ai_unattended.psutil.virtual_memory', side_effect=memory), \
                 patch.object(runner, 'publish') as publish, patch('ai_unattended.time.sleep') as sleep:
                self.assertTrue(runner.wait_resources(time.time()+100))
            self.assertEqual(sleep.call_count, 5)
            self.assertEqual(publish.call_args.kwargs, dict(status='resources_ready', reason=None))

    def test_no_resource_window_after_resume_does_not_evaluate_missing_new_batch(self):
        with tempfile.TemporaryDirectory() as d:
            root, checkpoint = self.resume_fixture(Path(d))
            with patch('ai_unattended.ROOT', root), patch('ai_unattended.fingerprint', return_value={}), \
                 patch('ai_unattended.subprocess.check_output', return_value='fixture-commit'):
                runner = Runner(Path(d)/'new', 8, checkpoint)
            with patch('ai_unattended.may_start_batch', return_value=False), patch.object(runner, 'child') as child:
                runner.run()
            child.assert_not_called()
            self.assertEqual(runner.state['status'], 'no_training_resource_window')

    def test_failed_final_export_keeps_completed_game_summary_and_fixed_holdout(self):
        with tempfile.TemporaryDirectory() as d:
            runner = Runner(Path(d)/'new', 8, holdout_seed=670270)
            runner.state['batches'] = [{'fixture': 'already completed training'}]
            captured = []
            def child(command, label, maximum):
                captured.append(command)
                directory = runner.output/label; directory.mkdir()
                save(directory/'summary.json', {'games': 4, 'gameplay_complete': True,
                    'pipeline_complete': False, 'replay_status': 'failed'})
                raise RuntimeError('export timeout fixture')
            with patch('ai_unattended.may_start_batch', return_value=False), \
                 patch.object(runner, 'wait_resources', return_value=True), patch.object(runner, 'child', side_effect=child):
                with self.assertRaises(RuntimeError):
                    runner.run()
            self.assertEqual(runner.state['status'], 'failed_preserved')
            self.assertEqual(runner.state['final_evaluations'][0]['summary']['games'], 4)
            self.assertEqual(captured[0][captured[0].index('--seed')+1], '670270')
            self.assertEqual(runner.state['holdout_seeds'], [670270, 670271])


if __name__ == '__main__':
    unittest.main()
