"""Bounded local PPO batches. No game rules, model API, or cloud dependencies."""
import argparse
from contextlib import contextmanager
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import time
import traceback

import psutil

ROOT = Path(__file__).resolve().parents[1]
GIB = 2 ** 30
TRAIN_MINUTES = 20
FINAL_RESERVE = 35 * 60
START_AVAILABLE_GIB = 6
START_STABLE_CHECKS = 3


def stamp():
    return datetime.now(timezone.utc).isoformat()


def save(path, value):
    temporary = path.with_suffix(path.suffix + '.partial')
    temporary.write_text(json.dumps(value, ensure_ascii=False, indent=2), encoding='utf-8')
    os.replace(temporary, path)


def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def fingerprint(root):
    names = subprocess.check_output(['git', 'ls-files', '-z', 'ai', 'src', 'core/com.goa2.core/Runtime', 'content/canonical'], cwd=root).decode().split('\0')
    paths = [root / n for n in names if n and Path(n).suffix in ('.py', '.cs', '.json', '.csproj', '.props')]
    paths += list((root / 'ai/Goa2.Ai.Cli/bin/Release/net10.0').glob('Goa2.*.dll'))
    paths += [root / 'tools/evaluate-ai-checkpoint.py', root / 'tools/ai_unattended.py', root / 'tools/ai_replay_export.py']
    paths += [p for p in [root / 'tools/ai_quota_watch.py'] if p.exists()]
    return {str(p.relative_to(root)): hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(set(paths))}


@contextmanager
def exclusive_lock(path):
    """OS releases this lock on crash; an existing filename is not a stale lock."""
    import msvcrt
    with open(path, 'a+b') as handle:
        handle.seek(0)
        if not handle.read(1):
            handle.write(b'0'); handle.flush()
        handle.seek(0)
        msvcrt.locking(handle.fileno(), msvcrt.LK_NBLCK, 1)
        try:
            yield
        finally:
            handle.seek(0)
            msvcrt.locking(handle.fileno(), msvcrt.LK_UNLCK, 1)


def may_start_batch(now, deadline):
    return deadline - now > TRAIN_MINUTES * 60 + FINAL_RESERVE + 120


def training_command(root, output, previous=None):
    command = [sys.executable, '-u', str(root / 'ai/trainer/train.py'), '--root', str(root),
               '--output', str(output), '--device', 'cpu', '--architecture', 'entity',
               '--hidden', '96', '--latents', '32', '--memory-dim', '128',
               '--sequence-length', '4', '--batch-size', '4', '--epochs', '2',
               '--iterations', '1', '--rollout', '128', '--limit', '1200', '--eval-limit', '1200',
               '--seed', '270140', '--eval-seed', '570140', '--eval-pairs', '1',
               '--life', '7', '--marks', '3', '--max-minutes', str(TRAIN_MINUTES)]
    if previous is not None:
        command += ['--resume', str(previous)]
    return command


def completed_checkpoint(directory):
    """Only promote a fully evaluated batch; retain partial checkpoints separately."""
    if (directory / 'failure.json').exists():
        raise RuntimeError('batch preserved a failure; automatic continuation refused')
    summary = read(directory / 'summary.json')
    if not summary['checkpoint_inference_equal'] or summary['learner_samples'] <= 0:
        raise RuntimeError('batch did not validate learning and checkpoint restore')
    points = sorted(directory.glob('checkpoint-*.pt'))
    if not points:
        raise RuntimeError('missing completed checkpoint')
    checkpoint = points[-1]
    evaluation = read(directory / (checkpoint.name + '.evaluation.json'))
    if hashlib.sha256(checkpoint.read_bytes()).hexdigest() != evaluation['checkpoint_sha256']:
        raise RuntimeError('checkpoint/evaluation hash mismatch')
    if len(evaluation['results']) != 2:
        raise RuntimeError('incomplete paired evaluation')
    return checkpoint, summary


def resume_metadata(root, checkpoint):
    """Inspect proven provenance without importing torch during a low-memory wait.

    The trainer still loads and validates the actual model, optimizer and RNG.
    This sidecar preflight does not replace that authoritative checkpoint check.
    """
    checkpoint = checkpoint.resolve()
    latest, _ = completed_checkpoint(checkpoint.parent)
    if latest.resolve() != checkpoint:
        raise ValueError('resume must use the latest fully evaluated checkpoint in its batch')
    metadata = read(Path(str(checkpoint) + '.json'))
    if metadata['format'] != 2 or metadata['boundary'] != 'episode':
        raise ValueError('resume requires a version-2 episode-boundary checkpoint')
    config = metadata['config']
    for key, directory, pattern in [('python_files', root / 'ai/trainer', '*.py'),
                                    ('authority_assemblies', root / 'ai/Goa2.Ai.Cli/bin/Release/net10.0', 'Goa2.*.dll')]:
        current = {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in directory.glob(pattern)}
        if not current or current != config[key]:
            raise ValueError('resume provenance mismatch: ' + key)
    return dict(path=str(checkpoint), sha256=hashlib.sha256(checkpoint.read_bytes()).hexdigest(),
                iteration=metadata['iteration'], cursor=metadata['cursor'],
                next_seed=config['seed'] + metadata['cursor'] // 2, next_swap=metadata['cursor'] % 2)


class Runner:
    def __init__(self, output, hours, resume=None, holdout_seed=670140):
        self.resume = resume.resolve() if resume is not None else None
        self.holdout_seed = holdout_seed
        provenance = resume_metadata(ROOT, self.resume) if self.resume is not None else None
        self.output = output
        output.mkdir(parents=True, exist_ok=False)
        self.started = time.time()
        self.deadline = self.started + hours * 3600
        self.sources = fingerprint(ROOT)
        self.state = dict(format=1, status='starting', started_utc=stamp(),
                          deadline_utc=datetime.fromtimestamp(self.deadline, timezone.utc).isoformat(),
                          pid=os.getpid(), process_created=psutil.Process().create_time(),
                          source_commit=subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip(),
                          hours=hours, architecture='entity', device='cpu', threads=2, batches=[], final_evaluations=[],
                          resume=provenance, holdout_seeds=[holdout_seed, holdout_seed+1],
                          resource_policy={'start_available_gib': START_AVAILABLE_GIB,
                                           'start_stable_checks': START_STABLE_CHECKS, 'check_interval_seconds': 10,
                                           'in_run_available_gib': 4,
                                           'minimum_disk_free_gib': 25, 'maximum_artifacts_gib': 10},
                          authority='existing C# engine', opponent_pool=['simple-v1'],
                          conclusion='No strength improvement is established by starting this run.')
        save(output / 'source-hashes.json', self.sources)
        self.publish()

    def publish(self, **values):
        self.state.update(values)
        self.state['updated_utc'] = stamp()
        self.state['available_memory_gib'] = psutil.virtual_memory().available / GIB
        save(self.output / 'status.json', self.state)

    def check_source_and_disk(self):
        if fingerprint(ROOT) != self.sources:
            raise RuntimeError('source/authority changed; stop rather than mix rule or trainer versions')
        if self.resume is not None and hashlib.sha256(self.resume.read_bytes()).hexdigest() != self.state['resume']['sha256']:
            raise RuntimeError('resume checkpoint changed after preflight')
        used = sum(p.stat().st_size for p in self.output.rglob('*') if p.is_file())
        if shutil.disk_usage(self.output).free < 25 * GIB or used > 10 * GIB:
            raise RuntimeError('disk budget reached; all artifacts retained')

    def wait_resources(self, until):
        stable_checks = 0
        while time.time() < until:
            if (self.output / 'STOP').exists():
                return False
            if psutil.virtual_memory().available >= START_AVAILABLE_GIB * GIB:
                stable_checks += 1
                if stable_checks >= START_STABLE_CHECKS:
                    self.publish(status='resources_ready', reason=None)
                    return True
            else:
                stable_checks = 0
            self.publish(status='waiting_memory', memory_stable_checks=stable_checks,
                         reason=f'Need {START_AVAILABLE_GIB} GiB free for {START_STABLE_CHECKS} consecutive checks; no seed consumed.')
            time.sleep(10)
        return False

    def child(self, command, label, maximum_seconds):
        self.check_source_and_disk()
        save(self.output / (label + '.command.json'), command)
        with open(self.output / (label + '.stdout.log'), 'x', encoding='utf-8') as stdout, \
             open(self.output / (label + '.stderr.log'), 'x', encoding='utf-8') as stderr:
            environment = dict(os.environ, OMP_NUM_THREADS='2', MKL_NUM_THREADS='2',
                               DOTNET_PROCESSOR_COUNT='2', PYTHONUNBUFFERED='1')
            child = subprocess.Popen(command, cwd=ROOT, stdout=stdout, stderr=stderr,
                                     env=environment, creationflags=getattr(subprocess, 'CREATE_NO_WINDOW', 0))
            process = psutil.Process(child.pid)
            try:
                if os.name == 'nt':
                    process.nice(psutil.BELOW_NORMAL_PRIORITY_CLASS)
                self.publish(status=label, reason=None, child_pid=child.pid, child_created=process.create_time())
                stop_at = min(time.time() + maximum_seconds, self.deadline)
                while child.poll() is None:
                    if time.time() >= stop_at:
                        raise TimeoutError('owned child hard deadline; raw accepted-command logs retained, not a completed game')
                    self.publish()
                    time.sleep(5)
            finally:
                if child.poll() is None:
                    # Normal trainer guard closes the host first; only terminate our owned tree on supervisor failure.
                    for owned in process.children(recursive=True) + [process]:
                        try:
                            owned.kill()
                        except psutil.NoSuchProcess:
                            pass
                    child.wait(timeout=10)
            self.publish(child_pid=None, child_created=None, last_returncode=child.returncode)
            if child.returncode != 0:
                raise RuntimeError(f'{label} failed (exit {child.returncode}); preserve its exact seed and stop, no retry')

    def run(self):
        latest = self.resume
        try:
            batch = 0
            while may_start_batch(time.time(), self.deadline) and batch < 300:
                if not self.wait_resources(self.deadline - FINAL_RESERVE - TRAIN_MINUTES * 60 - 120):
                    break
                if not may_start_batch(time.time(), self.deadline):
                    break
                batch += 1
                directory = self.output / f'batch-{batch:03}'
                self.child(training_command(ROOT, directory, latest), f'batch-{batch:03}', TRAIN_MINUTES * 60 + 100)
                latest, summary = completed_checkpoint(directory)
                self.state['batches'].append(dict(batch=batch, checkpoint=str(latest),
                    checkpoint_sha256=hashlib.sha256(latest.read_bytes()).hexdigest(),
                    seconds=summary['seconds'], learner_samples=summary['learner_samples'],
                    training_episodes=summary['training_episodes'], evaluation=summary['evaluation']))
                self.publish(status='batch_complete', latest_checkpoint=str(latest))
                if (self.output / 'STOP').exists():
                    break
            if self.state['batches'] and not (self.output / 'STOP').exists():
                # New fixed holdout seeds never used by training or per-batch validation.
                for label, checkpoint in [('holdout-initial', self.output / 'batch-001/initial.pt'),
                                          ('holdout-final', latest)]:
                    if not self.wait_resources(self.deadline - 16 * 60):
                        self.publish(status='stopped_without_final_evaluation', reason='resource or deadline limit')
                        return
                    command = [sys.executable, '-u', str(ROOT / 'tools/evaluate-ai-checkpoint.py'),
                               '--checkpoint', str(checkpoint), '--output', str(self.output / label),
                               '--seed', str(self.holdout_seed), '--pairs', '2', '--limit', '1200', '--max-minutes', '15']
                    if label == 'holdout-final':
                        command += ['--export-replays']
                    try:
                        self.child(command, label, 15 * 60 + 100)
                    finally:
                        summary_path = self.output / label / 'summary.json'
                        if summary_path.exists():
                            self.state['final_evaluations'].append(dict(label=label, summary=read(summary_path)))
                    if not read(summary_path)['pipeline_complete']:
                        raise RuntimeError('evaluation or replay pipeline incomplete; preserve and stop')
            self.publish(status='stopped_by_request' if (self.output / 'STOP').exists() else
                         ('completed_budget' if self.state['batches'] else 'no_training_resource_window'), finished_utc=stamp())
        except Exception:
            failure = dict(error=traceback.format_exc(), at_utc=stamp())
            save(self.output / 'failure.json', failure)
            self.publish(status='failed_preserved', failure=failure, finished_utc=stamp())
            raise


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--hours', type=float, default=8)
    parser.add_argument('--resume', type=Path, help='Explicit, fully evaluated checkpoint from an earlier run')
    parser.add_argument('--holdout-seed', type=int, default=670140)
    args = parser.parse_args()
    if not 1 <= args.hours <= 8:
        raise ValueError('authorized workflow is bounded to 1..8 hours')
    if not 0 <= args.holdout_seed < 2147483647:
        raise ValueError('holdout seed out of range')
    branch = subprocess.check_output(['git', 'branch', '--show-current'], cwd=ROOT, text=True).strip()
    if branch != 'feature/ai-training':
        raise ValueError('run only in the independent AI branch')
    artifacts = ROOT / 'artifacts/ai-training'
    artifacts.mkdir(parents=True, exist_ok=True)
    with exclusive_lock(artifacts / 'unattended.lock'):
        Runner(args.output.resolve(), args.hours, args.resume, args.holdout_seed).run()


if __name__ == '__main__':
    main()
