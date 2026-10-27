"""Supervision loss and continuation boundaries, without pretending to test rules."""
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import time
import unittest
from unittest.mock import patch

import psutil
import ai_quota_watch as q


class QuotaTests(unittest.TestCase):
    def runner(self, base):
        checkpoint = base/'old.pt'; checkpoint.write_bytes(b'fixture, never loaded')
        metadata = dict(path=str(checkpoint), sha256=hashlib.sha256(checkpoint.read_bytes()).hexdigest(),
                        iteration=224, cursor=323, next_seed=270301, next_swap=1)
        with patch.object(q, 'resume_metadata', return_value=metadata), patch.object(q, 'fingerprint', return_value={}), \
             patch.object(q.subprocess, 'check_output', return_value='fixture-source'):
            return q.QuotaSupervisor(base/'campaign', checkpoint, 670280)

    def test_expired_lease_does_not_start_worker_or_reuse_seed(self):
        with tempfile.TemporaryDirectory() as tmp:
            r=self.runner(Path(tmp)); lease=q.read(r.output/'lease.json')
            lease.update(renewed_utc=q.utc(time.time()-100), expires_utc=q.utc(time.time()-1))
            q.save(r.output/'lease.json', lease)
            with patch.object(r, 'run_segment') as worker:
                r.run()
            worker.assert_not_called()
            self.assertEqual(r.state['status'], 'stopped_lease_expired')
            self.assertEqual(r.state['resume']['cursor'], 323)

    def test_expired_lease_cannot_be_revived(self):
        with tempfile.TemporaryDirectory() as tmp:
            r=self.runner(Path(tmp)); lease=q.read(r.output/'lease.json')
            lease.update(renewed_utc=q.utc(time.time()-100), expires_utc=q.utc(time.time()-1))
            q.save(r.output/'lease.json', lease)
            with self.assertRaisesRegex(RuntimeError, 'expired lease'):
                q.renew(r.output, r.state['pid'], r.state['process_created'])

    def test_renewal_needs_live_inspected_identity_and_preserves_session(self):
        with tempfile.TemporaryDirectory() as tmp:
            r=self.runner(Path(tmp))
            with self.assertRaisesRegex(RuntimeError, 'unexpected supervisor'):
                q.renew(r.output, r.state['pid'], r.state['process_created']+1)
            with patch.object(q, 'fingerprint', return_value={}):
                lease=q.renew(r.output, r.state['pid'], r.state['process_created'])
            self.assertEqual(lease['session_id'], r.state['session_id'])
            self.assertAlmostEqual(q.timestamp(lease['expires_utc'])-q.timestamp(lease['renewed_utc']), 5400)

    def test_wrong_session_overlong_and_future_leases_are_rejected(self):
        with tempfile.TemporaryDirectory() as tmp:
            r=self.runner(Path(tmp)); original=q.read(r.output/'lease.json'); now=time.time()
            for change in [dict(session_id='other'),dict(expires_utc=q.utc(now+6000)),
                           dict(renewed_utc=q.utc(now+30),expires_utc=q.utc(now+60))]:
                with self.assertRaises(ValueError):
                    q.lease_expiry(dict(original,**change),r.state,now)

    def test_stop_and_failure_never_renew(self):
        with tempfile.TemporaryDirectory() as tmp:
            r=self.runner(Path(tmp)); (r.output/'STOP').touch()
            with self.assertRaisesRegex(RuntimeError,'STOP'):
                q.renew(r.output,r.state['pid'],r.state['process_created'])
            r.publish(status='failed_preserved')
            with self.assertRaisesRegex(RuntimeError,'stopped/draining'):
                q.renew(r.output,r.state['pid'],r.state['process_created'])

    def test_corrupt_lease_is_preserved_as_failure(self):
        with tempfile.TemporaryDirectory() as tmp:
            r=self.runner(Path(tmp)); (r.output/'lease.json').write_text('{bad')
            with self.assertRaises(ValueError):r.run()
            self.assertEqual(q.read(r.output/'status.json')['status'],'failed_preserved')
            self.assertTrue((r.output/'failure.json').exists())

    def test_changed_frozen_source_refuses_renewal(self):
        with tempfile.TemporaryDirectory() as tmp:
            r=self.runner(Path(tmp))
            with patch.object(q,'fingerprint',return_value={'changed':'hash'}), self.assertRaisesRegex(RuntimeError,'source changed'):
                q.renew(r.output,r.state['pid'],r.state['process_created'])

    def test_continuation_uses_next_checkpoint_and_predeclared_holdout(self):
        with tempfile.TemporaryDirectory() as tmp:
            r=self.runner(Path(tmp)); calls=[]; new=Path(tmp)/'next.pt';new.write_bytes(b'next fixture')
            def segment(directory,seed):
                calls.append((r.latest,seed)); directory.mkdir()
                q.save(directory/'status.json',dict(status='completed_budget',latest_checkpoint=str(new),
                     batches=[{}],holdout_seeds=[seed,seed+1],final_evaluations=[{'summary':{'pipeline_complete':True}}]*2))
                if len(calls)==2:(r.output/'STOP').touch()
                return True
            with patch.object(r,'check_sources'),patch.object(r,'check_disk'),patch.object(r,'run_segment',side_effect=segment), \
                 patch.object(q,'resume_metadata',return_value={'iteration':225,'cursor':324}):r.run()
            self.assertEqual([x[1] for x in calls],[670280,670282])
            self.assertEqual(calls[1][0],new)
            self.assertEqual(r.state['status'],'stopped_by_request')

    def test_segment_failure_is_not_retried(self):
        with tempfile.TemporaryDirectory() as tmp:
            r=self.runner(Path(tmp))
            with patch.object(r,'check_sources'),patch.object(r,'check_disk'), \
                 patch.object(r,'run_segment',side_effect=RuntimeError('retained failure')) as worker:
                with self.assertRaisesRegex(RuntimeError,'retained failure'):r.run()
            self.assertEqual(worker.call_count,1)
            self.assertEqual(r.state['status'],'failed_preserved')

    def test_pid_reuse_refuses_to_kill(self):
        with patch.object(q.psutil,'Process') as constructor:
            constructor.return_value.create_time.return_value=100
            with self.assertRaisesRegex(RuntimeError,'PID reused'):q.stop_owned_tree(1,99)
            constructor.return_value.kill.assert_not_called()
            constructor.return_value.children.assert_not_called()

    def test_real_worker_stops_at_boundary_when_lease_lost(self):
        with tempfile.TemporaryDirectory() as tmp:
            base=Path(tmp);r=self.runner(base);directory=r.output/'segment-001'
            worker=base/'worker.py';worker.write_text('import pathlib,sys,time\np=pathlib.Path(sys.argv[1]);p.mkdir()\nwhile not (p/"STOP").exists(): time.sleep(.01)\nprint("drained")\n')
            original_popen=subprocess.Popen; original_sleep=time.sleep
            def popen(command,**kwargs):return original_popen([sys.executable,str(worker),str(directory)],**kwargs)
            with patch.object(q.subprocess,'Popen',side_effect=popen),patch.object(r,'check_sources'),patch.object(r,'check_disk'), \
                 patch.object(r,'stop_reason',side_effect=lambda:'lease_expired' if directory.exists() else None), \
                 patch.object(q.time,'sleep',side_effect=lambda _:original_sleep(.01)),patch.object(q,'DRAIN_SECONDS',2):
                self.assertTrue(r.run_segment(directory,670280))
            self.assertTrue((directory/'STOP').exists())
            self.assertIn('drained',(r.output/'segment-001.stdout.log').read_text())
            self.assertEqual(r.state['last_returncode'],0)

    def test_real_unresponsive_worker_is_stopped_after_drain_limit(self):
        with tempfile.TemporaryDirectory() as tmp:
            base=Path(tmp);r=self.runner(base);directory=r.output/'segment-001'
            worker=base/'worker.py';worker.write_text('import pathlib,sys,time\np=pathlib.Path(sys.argv[1]);p.mkdir()\ntime.sleep(120)\n')
            original_popen=subprocess.Popen; original_sleep=time.sleep
            def popen(command,**kwargs):return original_popen([sys.executable,str(worker),str(directory)],**kwargs)
            with patch.object(q.subprocess,'Popen',side_effect=popen),patch.object(r,'check_sources'),patch.object(r,'check_disk'), \
                 patch.object(r,'stop_reason',side_effect=lambda:'lease_expired' if directory.exists() else None), \
                 patch.object(q.time,'sleep',side_effect=lambda _:original_sleep(.01)),patch.object(q,'DRAIN_SECONDS',.05):
                with self.assertRaisesRegex(TimeoutError,'failed to drain'):r.run_segment(directory,670280)
            identity=q.read(r.output/'segment-001.process.json')
            self.assertFalse(psutil.pid_exists(identity['pid']))


if __name__=='__main__':unittest.main()
