"""Exercise the external renewal CLI in a real subprocess, including stdout."""
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import time
import unittest

import psutil
import ai_quota_watch as q


class RenewalCliTests(unittest.TestCase):
    def invoke(self, output, identity):
        return subprocess.run([
            sys.executable, str(q.ROOT / 'tools/renew-ai-supervision.py'),
            '--output', str(output), '--expected-pid', str(identity['pid']),
            '--expected-created', str(identity['process_created']),
        ], cwd=q.ROOT, capture_output=True, text=True, timeout=15)

    def prepare(self, output, expired=False):
        process = psutil.Process()
        identity = dict(session_id='external-cli-test', pid=process.pid,
                        process_created=process.create_time())
        now = time.time()
        q.save(output / 'status.json', dict(identity, status='segment_running'))
        q.save(output / 'source-hashes.json', q.fingerprint(q.ROOT))
        lease = dict(identity, renewed_utc=q.utc(now-100),
                     expires_utc=q.utc(now-1 if expired else now+60))
        q.save(output / 'lease.json', lease)
        return identity, lease

    def test_success_stdout_matches_atomic_file_and_retains_identity(self):
        with tempfile.TemporaryDirectory() as tmp:
            output = Path(tmp)
            identity, old = self.prepare(output)
            result = self.invoke(output, identity)
            self.assertEqual(result.returncode, 0, result.stderr)
            renewed = json.loads(result.stdout)
            self.assertEqual(renewed, q.read(output / 'lease.json'))
            self.assertTrue(all(renewed[k] == v for k, v in identity.items()))
            self.assertGreater(q.timestamp(renewed['expires_utc']), q.timestamp(old['expires_utc']))
            self.assertAlmostEqual(q.timestamp(renewed['expires_utc']) - q.timestamp(renewed['renewed_utc']), 5400)

    def test_expired_cli_refuses_without_writing(self):
        with tempfile.TemporaryDirectory() as tmp:
            output = Path(tmp)
            identity, old = self.prepare(output, expired=True)
            before = (output / 'lease.json').read_bytes()
            result = self.invoke(output, identity)
            self.assertNotEqual(result.returncode, 0)
            self.assertIn('expired lease cannot be revived', result.stderr)
            self.assertEqual((output / 'lease.json').read_bytes(), before)


if __name__ == '__main__':
    unittest.main()
