"""Continue bounded training segments while an external Codex inspector renews a lease.

No account credentials/API calls, game rules or model changes. Quota is NOT
inferred from local training. Missing inspections stop local work at a batch
boundary, with a bounded identity-checked cleanup if the worker fails to exit.
"""
import argparse
from datetime import datetime, timezone
import hashlib
import math
import os
from pathlib import Path
import subprocess
import sys
import time
import traceback
import uuid

import psutil
from ai_unattended import ROOT, GIB, TRAIN_MINUTES, exclusive_lock, fingerprint, read, resume_metadata, save, stamp

LEASE_SECONDS = 90 * 60
DRAIN_SECONDS = TRAIN_MINUTES * 60 + 120
SEGMENT_HOURS = 4
TERMINAL = {'failed_preserved', 'stopped_lease_expired', 'stopped_by_request'}


def utc(seconds):
    return datetime.fromtimestamp(seconds, timezone.utc).isoformat()


def timestamp(value):
    parsed = datetime.fromisoformat(value)
    if parsed.tzinfo is None:
        raise ValueError('lease timestamp must include timezone')
    result = parsed.timestamp()
    if not math.isfinite(result):
        raise ValueError('nonfinite lease timestamp')
    return result


def verify_identity(pid, created):
    try:
        process = psutil.Process(pid)
        if abs(process.create_time() - created) > .01:
            raise RuntimeError('PID reused; process identity mismatch')
        return process
    except psutil.NoSuchProcess as exc:
        raise RuntimeError('recorded process no longer exists') from exc


def stop_owned_tree(pid, created):
    """Only kill identities obtained below a verified, still-live owned root."""
    process = verify_identity(pid, created)
    identities = []
    for descendant in process.children(recursive=True):
        try:
            identities.append((descendant.pid, descendant.create_time()))
        except psutil.NoSuchProcess:
            pass
    for owned_pid, owned_created in list(reversed(identities)) + [(pid, created)]:
        try:
            owned = verify_identity(owned_pid, owned_created)
            owned.kill()
        except RuntimeError:
            continue  # exited/reused process must not be touched


def lease_expiry(lease, state, now):
    if (lease['session_id'], lease['pid'], lease['process_created']) != (
            state['session_id'], state['pid'], state['process_created']):
        raise ValueError('lease belongs to a different supervisor identity')
    renewed, expires = timestamp(lease['renewed_utc']), timestamp(lease['expires_utc'])
    if renewed > now + 5 or not 0 < expires - renewed <= LEASE_SECONDS + .01:
        raise ValueError('lease timestamp or duration invalid')
    return expires


def renew(output, expected_pid, expected_created):
    """Called only after an external inspection, never by the local supervisor."""
    state = read(output / 'status.json')
    if state['status'] in TERMINAL or state.get('stop_reason'):
        raise RuntimeError('stopped/draining run cannot be renewed or restarted')
    if state['pid'] != expected_pid or abs(state['process_created'] - expected_created) > .01:
        raise RuntimeError('unexpected supervisor identity; inspect before renewal')
    verify_identity(expected_pid, expected_created)
    now = time.time()
    if now >= lease_expiry(read(output / 'lease.json'), state, now):
        raise RuntimeError('expired lease cannot be revived; preserve and stop')
    if (output / 'STOP').exists():
        raise RuntimeError('STOP requested; renewal refused')
    if fingerprint(ROOT) != read(output / 'source-hashes.json'):
        raise RuntimeError('frozen source changed; renewal refused')
    lease = dict(session_id=state['session_id'], pid=state['pid'], process_created=state['process_created'],
                 renewed_utc=utc(now), expires_utc=utc(now + LEASE_SECONDS))
    save(output / 'lease.json', lease)
    return lease


class QuotaSupervisor:
    def __init__(self, output, resume, holdout_seed):
        provenance = resume_metadata(ROOT, resume)
        output.mkdir(parents=True, exist_ok=False)
        self.output, self.latest, self.holdout_seed = output, resume.resolve(), holdout_seed
        self.sources = fingerprint(ROOT)
        self.disk_checked_at = 0
        self.started = time.time()
        self.state = dict(format=1, status='starting', session_id=str(uuid.uuid4()),
                          pid=os.getpid(), process_created=psutil.Process().create_time(), started_utc=stamp(),
                          source_commit=subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip(),
                          resume=provenance, latest_checkpoint=str(self.latest), segments=[],
                          segment_hours=SEGMENT_HOURS, total_time_limit=None, lease_seconds=LEASE_SECONDS,
                          drain_seconds=DRAIN_SECONDS, first_holdout_seed=holdout_seed,
                          holdout_seed_rule='first_holdout_seed + 2 * (segment_number - 1), paired with next integer',
                          resource_policy=dict(start_available_gib=6, start_stable_checks=3,
                                               check_interval_seconds=10, in_run_available_gib=4,
                                               minimum_disk_free_gib=25, maximum_campaign_artifacts_gib=10),
                          quota_detection='external inspector; missing lease is supervision loss, not proof of exhausted quota',
                          child_pid=None, child_created=None, stop_reason=None)
        save(output / 'source-hashes.json', self.sources)
        save(output / 'lease.json', dict(session_id=self.state['session_id'], pid=self.state['pid'],
             process_created=self.state['process_created'], renewed_utc=utc(self.started),
             expires_utc=utc(self.started + LEASE_SECONDS)))
        self.publish()

    def publish(self, **values):
        self.state.update(values)
        self.state['updated_utc'] = stamp()
        self.state['available_memory_gib'] = psutil.virtual_memory().available / GIB
        try:
            self.state['lease_expires_utc'] = read(self.output / 'lease.json')['expires_utc']
        except (OSError, ValueError, KeyError) as exc:
            self.state['lease_expires_utc'] = None
            self.state['lease_read_error'] = str(exc)
        save(self.output / 'status.json', self.state)

    def stop_reason(self):
        if self.state.get('stop_reason'):
            return self.state['stop_reason']
        if (self.output / 'STOP').exists():
            return 'user_stop'
        now = time.time()
        if now >= lease_expiry(read(self.output / 'lease.json'), self.state, now):
            return 'lease_expired'
        return None

    def check_sources(self):
        if fingerprint(ROOT) != self.sources:
            raise RuntimeError('frozen source/authority changed; automatic continuation refused')
        if hashlib.sha256(Path(self.state['resume']['path']).read_bytes()).hexdigest() != self.state['resume']['sha256']:
            raise RuntimeError('original resume checkpoint changed')

    def check_disk(self):
        used = sum(p.stat().st_size for p in self.output.rglob('*') if p.is_file())
        if psutil.disk_usage(str(self.output)).free < 25 * GIB or used > 10 * GIB:
            raise RuntimeError('campaign disk protection reached; preserve all artifacts and stop')
        self.disk_checked_at = time.monotonic()

    def run_segment(self, directory, seed):
        if self.stop_reason():
            return False
        command = [sys.executable, '-u', str(ROOT / 'tools/ai_unattended.py'), '--output', str(directory),
                   '--hours', str(SEGMENT_HOURS), '--resume', str(self.latest), '--holdout-seed', str(seed)]
        save(self.output / (directory.name + '.command.json'), command)
        with open(self.output / (directory.name + '.stdout.log'), 'x', encoding='utf-8') as stdout, \
             open(self.output / (directory.name + '.stderr.log'), 'x', encoding='utf-8') as stderr:
            child = subprocess.Popen(command, cwd=ROOT, stdout=stdout, stderr=stderr,
                                     creationflags=getattr(subprocess, 'CREATE_NO_WINDOW', 0))
            process = psutil.Process(child.pid)
            created = process.create_time()
            if os.name == 'nt':
                process.nice(psutil.BELOW_NORMAL_PRIORITY_CLASS)
            save(self.output / (directory.name + '.process.json'), dict(pid=child.pid, created=created))
            self.publish(status='segment_running', current_segment=str(directory), child_pid=child.pid, child_created=created)
            stop_requested_at = None
            hard_deadline = time.time() + SEGMENT_HOURS * 3600 + 30
            try:
                while child.poll() is None:
                    reason = self.stop_reason()
                    if reason and stop_requested_at is None:
                        stop_requested_at = time.time()
                        self.publish(status='draining', stop_reason=reason)
                    if stop_requested_at is not None:
                        if directory.exists():
                            (directory / 'STOP').touch(exist_ok=True)
                        if time.time() - stop_requested_at >= DRAIN_SECONDS:
                            raise TimeoutError('worker failed to drain after supervision stopped; partial games preserved')
                    if time.time() >= hard_deadline:
                        raise TimeoutError('segment supervisor exceeded its own hard deadline')
                    self.check_sources()
                    if time.monotonic() - self.disk_checked_at >= 60:
                        self.check_disk()
                    self.publish()
                    time.sleep(5)
            finally:
                if child.poll() is None:
                    stop_owned_tree(child.pid, created)
                    child.wait(timeout=15)
            self.publish(child_pid=None, child_created=None, last_returncode=child.returncode)
            if child.returncode != 0:
                raise RuntimeError('segment failed; preserve exact failure and seed, no automatic retry')
        return True

    def run(self):
        try:
            segment = 0
            while not self.stop_reason():
                self.check_sources()
                self.check_disk()
                segment += 1
                seed = self.holdout_seed + 2 * (segment - 1)
                if seed + 1 >= 2147483647:
                    raise ValueError('predeclared holdout seed range exhausted')
                directory = self.output / f'segment-{segment:03}'
                if not self.run_segment(directory, seed):
                    break
                result = read(directory / 'status.json')
                self.state['segments'].append(dict(segment=segment, directory=str(directory),
                     status=result['status'], finished_utc=result.get('finished_utc'),
                     latest_checkpoint=result.get('latest_checkpoint'), holdout_seeds=result['holdout_seeds']))
                if result.get('latest_checkpoint'):
                    self.latest = Path(result['latest_checkpoint'])
                    provenance = resume_metadata(ROOT, self.latest)
                    self.publish(latest_checkpoint=str(self.latest), latest_provenance=provenance)
                if self.stop_reason():
                    break
                if result['status'] != 'completed_budget' or not result['batches']:
                    raise RuntimeError('segment did not complete training and evaluation; no automatic retry')
                if len(result['final_evaluations']) != 2 or not all(
                        e['summary']['pipeline_complete'] for e in result['final_evaluations']):
                    raise RuntimeError('segment evaluation/replay evidence incomplete')
                self.publish(status='segment_complete')
            reason = self.stop_reason() or 'user_stop'
            self.publish(status='stopped_lease_expired' if reason == 'lease_expired' else 'stopped_by_request',
                         stop_reason=reason, finished_utc=stamp())
        except Exception:
            failure = dict(error=traceback.format_exc(), at_utc=stamp())
            save(self.output / 'failure.json', failure)
            self.publish(status='failed_preserved', failure=failure, finished_utc=stamp())
            raise


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('operation', choices=['start', 'renew'])
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--resume', type=Path)
    parser.add_argument('--holdout-seed', type=int, default=670280)
    parser.add_argument('--expected-pid', type=int)
    parser.add_argument('--expected-created', type=float)
    args = parser.parse_args()
    if subprocess.check_output(['git', 'branch', '--show-current'], cwd=ROOT, text=True).strip() != 'feature/ai-training':
        raise ValueError('run only in the independent AI branch')
    if args.operation == 'renew':
        if args.expected_pid is None or args.expected_created is None:
            raise ValueError('renewal requires inspected PID and creation time')
        print(json.dumps(renew(args.output.resolve(), args.expected_pid, args.expected_created), ensure_ascii=True))
        return
    if args.resume is None or not 0 <= args.holdout_seed < 2147483646:
        raise ValueError('start requires a proven checkpoint and valid holdout seed')
    artifacts = ROOT / 'artifacts/ai-training'
    artifacts.mkdir(parents=True, exist_ok=True)
    with exclusive_lock(artifacts / 'quota-supervisor.lock'):
        with exclusive_lock(artifacts / 'unattended.lock'):
            pass  # old workers must have exited; each segment owns this lock while running
        QuotaSupervisor(args.output.resolve(), args.resume.resolve(), args.holdout_seed).run()


if __name__ == '__main__':
    main()
