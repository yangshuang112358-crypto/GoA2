"""Bounded, paired formal evaluation, separate from learning and teaching fixtures."""
import argparse
import json
import os
from pathlib import Path
import subprocess
import sys
import time
import traceback

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT/'ai/trainer'))
import torch
from bridge import Bridge
from entities import Encoder
from train import configure, load_checkpoint, run_episode, sha, write
from resources import ResourceMonitor, require_interactive_memory


def main():
    p = argparse.ArgumentParser()
    p.add_argument('--checkpoint', type=Path, required=True)
    p.add_argument('--output', type=Path, required=True)
    p.add_argument('--seed', type=int, default=62014)
    p.add_argument('--pairs', type=int, default=1)
    p.add_argument('--limit', type=int, default=1200)
    p.add_argument('--max-minutes', type=int, default=10)
    p.add_argument('--life', type=int, default=7)
    p.add_argument('--marks', type=int, default=3)
    p.add_argument('--device', choices=['cpu', 'cuda'], default='cpu')
    p.add_argument('--export-replays', action='store_true')
    args = p.parse_args()
    if not (1 <= args.pairs <= 4 and 1 <= args.limit <= 4000 and 1 <= args.max_minutes <= 30):
        raise ValueError('bounded evaluation limits exceeded')
    args.output.mkdir(parents=True, exist_ok=False)
    monitor = ResourceMonitor(); started = time.perf_counter(); host = None
    config = {k: str(v) if isinstance(v, Path) else v for k, v in vars(args).items()}
    config.update(source_commit=subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip(),
                  source_status=subprocess.check_output(['git', 'status', '--porcelain'], cwd=ROOT, text=True),
                  checkpoint_sha256=sha(args.checkpoint), tool_sha256=sha(__file__),
                  python_files={f.name: sha(f) for f in (ROOT/'ai/trainer').glob('*.py')},
                  opponent_pool=['simple-v1'], learner='one wasp seat; remaining three seats frozen simple',
                  policy='greedy', formal_genesis=True, training=False, purpose='bounded formal compatibility evaluation')
    write(args.output/'manifest.json', config)
    results = []
    def guard():
        require_interactive_memory()
        if time.perf_counter()-started > args.max_minutes*60:
            raise TimeoutError('evaluation deadline; preserve interrupted authority and policy input')
    try:
        guard(); os.environ['CUBLAS_WORKSPACE_CONFIG'] = ':4096:8'
        device = configure(args.device)
        dotnet = Path(os.environ['LOCALAPPDATA'])/'Goa2V1Toolchain/dotnet/dotnet.exe'
        with Bridge(ROOT, dotnet, args.output/'authority') as host:
            host.call(op='reset', seed=61, learner=0, swap=0, limit=1, opponent='simple', life=args.life, marks=args.marks)
            description = host.call(op='describe'); encoder = Encoder(description)
            write(args.output/'public-catalog.json', description)
            model, data = load_checkpoint(args.checkpoint, encoder, device)
            model.eval()
            write(args.output/'model.json', dict(shape=model.shape, parameters=sum(p.numel() for p in model.parameters()),
                  training_source=data['config'].get('source_commit'), contract=encoder.contract))
            for i in range(args.pairs):
                for swap in (0, 1):
                    guard()
                    result = run_episode(host, model, encoder, device, args.seed+i, swap, args.limit,
                                         life=args.life, marks=args.marks, guard=guard)
                    results.append(result)
                    write(args.output/'results.json', results)
                    print(json.dumps(result), flush=True)
        replays = []
        if args.export_replays:
            for directory in sorted((args.output/'authority').glob('train-*')):
                result = json.loads((directory/'result.json').read_text(encoding='utf-8-sig'))
                if result['Stop'] != 'terminated':
                    continue
                guard()
                dest = args.output/'replays'/directory.name
                dest.parent.mkdir(exist_ok=True)
                command = [str(dotnet), str(ROOT/'ai/Goa2.Ai.Cli/bin/Release/net10.0/Goa2.Ai.Cli.dll'),
                           'export-replay', str(directory.resolve()), str(dest.resolve())]
                done = subprocess.run(command, capture_output=True, text=True, encoding='utf-8', timeout=60,
                                      creationflags=getattr(subprocess, 'CREATE_NO_WINDOW', 0))
                (dest.parent/(directory.name+'.log')).write_text(done.stdout+done.stderr, encoding='utf-8')
                if done.returncode:
                    raise RuntimeError('authoritative replay export failed: '+directory.name)
                replays.append(str(dest.relative_to(args.output)))
        total = len(results); terminated = sum(r['terminated'] for r in results)
        write(args.output/'summary.json', dict(games=total, terminated=terminated,
              truncated=sum(r['truncated'] for r in results), team_wins=sum(r['terminated'] and r['reward'] > 0 for r in results),
              completion_rate=terminated/total, truncation_rate=sum(r['truncated'] for r in results)/total,
              illegal_commands=0, exceptions=0, environment_steps=sum(r['environment_steps'] for r in results),
              learner_decisions=sum(r['learner_decisions'] for r in results), seconds=time.perf_counter()-started,
              replays=replays, unity_player_verified=False,
              gpu_allocated_mib=torch.cuda.max_memory_allocated()/2**20 if device.type == 'cuda' else 0.,
              conclusion='Small compatibility sample; not evidence of a strength improvement.'))
    except Exception:
        write(args.output/'failure.json', dict(error=traceback.format_exc(), completed_results=results))
        if host is not None and host.last_reply is not None:
            write(args.output/'failure-policy-input.json', host.last_reply)
        raise
    finally:
        write(args.output/'resources.json', monitor.report())


if __name__ == '__main__':
    main()
