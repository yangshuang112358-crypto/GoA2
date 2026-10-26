"""Bounded C# replay verification with durable logs; never changes game rules."""
import hashlib
import json
import os
from pathlib import Path
import subprocess
import time

import psutil


def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def write(path, value):
    temporary = path.with_suffix(path.suffix + '.partial')
    temporary.write_text(json.dumps(value, ensure_ascii=False, indent=2), encoding='utf-8')
    os.replace(temporary, path)


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def run_bounded(command, log, timeout, guard):
    """Keep stdout even on timeout, monitor RAM/deadline, kill only our child tree."""
    guard()
    started = time.monotonic()
    with log.open('xb') as stream:
        child = subprocess.Popen(command, stdout=stream, stderr=subprocess.STDOUT,
            env=dict(os.environ, DOTNET_PROCESSOR_COUNT='2', OMP_NUM_THREADS='2', MKL_NUM_THREADS='2'),
            creationflags=getattr(subprocess, 'CREATE_NO_WINDOW', 0))
        process = psutil.Process(child.pid)
        write(log.with_suffix('.process.json'), dict(pid=child.pid, created=process.create_time()))
        try:
            if os.name == 'nt':
                process.nice(psutil.BELOW_NORMAL_PRIORITY_CLASS)
            while child.poll() is None:
                guard()
                if time.monotonic() - started >= timeout:
                    raise TimeoutError(f'replay verification exceeded {timeout:g} seconds; logs preserved')
                time.sleep(.25)
            if child.returncode:
                raise RuntimeError(f'replay verification exit {child.returncode}; see {log.name}')
        finally:
            if child.poll() is None and process.is_running():
                for owned in process.children(recursive=True) + [process]:
                    try:
                        owned.kill()  # psutil checks process identity, including PID reuse.
                    except psutil.NoSuchProcess:
                        pass
                child.wait(timeout=10)
    return time.monotonic() - started


def export_replay(root, dotnet, authority, destination, timeout, guard):
    """Strictly verify the unchanged authority log into a new destination."""
    if not 1 <= timeout <= 300:
        raise ValueError('replay timeout must be bounded to 1..300 seconds')
    destination.parent.mkdir(parents=True, exist_ok=True)
    attempt = destination.parent / (destination.name + '.attempt.json')
    log = destination.parent / (destination.name + '.log')
    if destination.exists() or attempt.exists() or log.exists():
        raise FileExistsError('replay output must be new; failed evidence is immutable')
    info = dict(status='checking', authority=str(authority), destination=str(destination),
                timeout_seconds=timeout, unity_player_verified=False)
    started = time.monotonic()
    write(attempt, info)
    try:
        guard()
        names = ['commands.json', 'final-save.json', 'contract.json']
        info['input_sha256'] = {name: sha(authority/name) for name in names}
        if (authority/'commands.json').stat().st_size > 2*1024*1024:
            raise ValueError('existing Unity scenario capacity exceeded; full log retained, not truncated')
        commands = read(authority/'commands.json')
        info['commands'] = len(commands['Steps'])
        if info['commands'] > 1000:
            raise ValueError('existing Unity scenario capacity exceeded; full log retained, not truncated')
        if read(authority/'result.json')['Stop'] != 'terminated':
            raise ValueError('formal replay export requires a truly terminated game')
        command = [str(dotnet), str(root/'ai/Goa2.Ai.Cli/bin/Release/net10.0/Goa2.Ai.Cli.dll'),
                   'export-replay', str(authority.resolve()), str(destination.resolve())]
        info.update(status='verifying', command=command)
        write(attempt, info)
        run_bounded(command, log, timeout, guard)
        result, report = read(destination/'result.json'), read(destination/'scenario-report.json')
        saved = (authority/'final-save.json').read_text(encoding='utf-8-sig')
        expected_hash = hashlib.sha256(saved.encode('utf-8')).hexdigest()
        if (result.get('ScenarioVerified') is not True or result.get('Stop') != 'terminated'
                or result.get('StateHash') != expected_hash or not report.get('Passed')
                or not report.get('Complete') or report.get('TotalSteps') != info['commands']
                or read(destination/'scenario.json') != commands
                or (destination/'final-save.json').read_text(encoding='utf-8-sig') != saved
                or read(destination/'contract.json') != read(authority/'contract.json')):
            raise ValueError('C# exported replay evidence mismatch')
        if info['input_sha256'] != {name: sha(authority/name) for name in names}:
            raise ValueError('authority source changed during replay export')
        info.update(status='verified', state_hash=expected_hash, seconds=time.monotonic()-started)
        write(attempt, info)
        return info
    except Exception as error:
        info.update(status='failed', error=f'{type(error).__name__}: {error}', seconds=time.monotonic()-started)
        write(attempt, info)
        raise


def evaluation_summary(results, expected_games, seconds, replays, replay_status,
                       game_exceptions=0, replay_exceptions=0, gpu_allocated_mib=0.):
    total = len(results)
    terminated = sum(r['terminated'] for r in results)
    truncated = sum(r['truncated'] for r in results)
    complete = total == expected_games and game_exceptions == 0
    return dict(games=total, expected_games=expected_games, terminated=terminated, truncated=truncated,
        team_wins=sum(r['terminated'] and r['reward'] > 0 for r in results),
        completion_rate=terminated/total if total else 0., truncation_rate=truncated/total if total else 0.,
        illegal_commands=0, exceptions=game_exceptions, replay_exceptions=replay_exceptions,
        environment_steps=sum(r['environment_steps'] for r in results),
        learner_decisions=sum(r['learner_decisions'] for r in results), seconds=seconds,
        gameplay_complete=complete, replay_status=replay_status, replays=list(replays),
        pipeline_complete=complete and replay_status in ('not_requested', 'complete'),
        unity_player_verified=False, gpu_allocated_mib=gpu_allocated_mib,
        conclusion='Small compatibility sample; not evidence of a strength improvement.')
