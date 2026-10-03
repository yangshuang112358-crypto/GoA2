"""Offline recovery preparation. No game server, network connection or live file edits."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import tempfile


def main():
    p = argparse.ArgumentParser()
    p.add_argument('--dotnet', required=True)
    p.add_argument('--save', required=True, type=Path)
    p.add_argument('--pending', required=True, type=Path)
    p.add_argument('--report', required=True, type=Path)
    a = p.parse_args()
    root = Path(__file__).resolve().parents[2]
    cli = root / 'tools/Goa2.RecoveryInspector/bin/Release/net10.0/Goa2.RecoveryInspector.dll'
    checks = []
    before = {path: hashlib.sha256(path.read_bytes()).hexdigest() for path in (a.save, a.pending)}
    def inspect(path, expected):
        run = subprocess.run([a.dotnet, str(cli), str(root), str(path)], capture_output=True, timeout=90)
        assert run.returncode == expected, run.stderr.decode(errors='replace')
        result = json.loads(run.stdout)
        assert result['passed'] == (expected == 0)
        assert result['roomStarted'] is False and result['sourceModified'] is False
        return result
    normal = inspect(a.save, 0)
    assert normal['Revision'] > 0
    checks.append('accepted-command save validates by replay')
    pending = inspect(a.pending, 0)
    assert pending['pendingDecision'] is True
    checks.append('pending decision survives offline replay')
    with tempfile.TemporaryDirectory() as temporary:
        invalid = Path(temporary) / 'invalid.save.json'
        data = json.loads(a.save.read_text(encoding='utf-8-sig'))
        data['Revision'] += 1
        invalid.write_text(json.dumps(data), encoding='utf-8')
        inspect(invalid, 1)
        checks.append('tampered final state rejected')
        data['Revision'] -= 1
        data['ContentHash'] = 'wrong-content'
        invalid.write_text(json.dumps(data), encoding='utf-8')
        inspect(invalid, 1)
        checks.append('wrong content version rejected')
        invalid.write_bytes(b'{"Revision":')
        inspect(invalid, 1)
        checks.append('truncated write rejected')
        inspect(Path(temporary)/'missing.json', 1)
        checks.append('missing save rejected')
    assert all(hashlib.sha256(path.read_bytes()).hexdigest() == h for path, h in before.items())
    checks.append('original normal and pending saves unchanged')
    a.report.parent.mkdir(parents=True, exist_ok=True)
    a.report.write_text(json.dumps({'passed': True, 'checks': checks,
        'normal_revision': normal['Revision'], 'pending_revision': pending['Revision'],
        'network_recovery_implemented': False}, indent=2), encoding='utf-8')
    print('PASS', len(checks), 'offline recovery checks', a.report)


if __name__ == '__main__':
    main()
