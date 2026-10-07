"""Bounded PPO experiment over the C# environment; no Python game rules.

One learner seat; all other seats are frozen simple baselines. Checkpoints are
written at episode boundaries and contain optimizer/RNG/seed-cursor state.
"""
import argparse
import copy
import hashlib
import json
import os
from pathlib import Path
import subprocess
import time
import traceback
import torch
from bridge import Bridge
from policy import CandidateNetwork, Encoder, ENCODER_VERSION, update
from resources import ResourceMonitor, require_interactive_memory


def write(path, value):
    Path(path).write_text(json.dumps(value, ensure_ascii=False, indent=2), encoding="utf-8")


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def configure(device):
    torch.set_num_threads(2)
    torch.set_num_interop_threads(1)
    torch.use_deterministic_algorithms(True)
    if device == "cuda":
        if not torch.cuda.is_available():
            raise RuntimeError("CUDA unavailable; use --device cpu explicitly and preserve the failure")
        torch.cuda.set_per_process_memory_fraction(.5)
    return torch.device(device)


def checkpoint(path, model, optimizer, encoder, config, cursor, iteration, metrics):
    payload = dict(format=1, encoder_version=ENCODER_VERSION, encoder_signature=encoder.signature,
                   contract=encoder.contract, shape=model.shape, model=model.state_dict(), optimizer=optimizer.state_dict(),
                   torch_rng=torch.get_rng_state(), cuda_rng=torch.cuda.get_rng_state_all() if next(model.parameters()).is_cuda else [],
                   config=config, cursor=cursor, iteration=iteration, metrics=metrics)
    torch.save(payload, path)
    write(str(path) + ".json", {k: v for k, v in payload.items() if k not in ("model", "optimizer", "torch_rng", "cuda_rng")})


def load_checkpoint(path, encoder, device):
    data = torch.load(path, map_location="cpu", weights_only=True)
    if data["format"] != 1 or data["encoder_version"] != ENCODER_VERSION or data["encoder_signature"] != encoder.signature or data["contract"] != encoder.contract:
        raise ValueError("incompatible_checkpoint: migrate explicitly and evaluate again")
    model = CandidateNetwork(**data["shape"]).to(device)
    model.load_state_dict(data["model"])
    return model, data


def run_episode(host, model, encoder, device, seed, swap, limit, records=None, life=7, marks=3):
    reply = host.call(op="reset", seed=seed, learner=swap, swap=swap, limit=limit, opponent="simple", life=life, marks=marks)
    count = 0
    started = time.perf_counter()
    while not (reply["Terminated"] or reply["Truncated"]):
        d = reply["Decision"]
        if d["Observation"]["Seat"] != swap:
            raise ValueError("foreign-seat observation reached learner")
        s, a = encoder.encode(d)
        with torch.no_grad():
            dist, v = model(s.to(device), a.to(device))
            chosen = dist.sample() if records is not None else dist.probs.argmax()
            old_log = dist.log_prob(chosen).item()
        following = host.call(op="step", revision=d["Revision"], action=d["Actions"][chosen.item()]["Id"])
        with torch.no_grad():
            if following["Terminated"]:
                nv = 0.
            else:
                ns, na = encoder.encode(following["Decision"])
                _, next_value = model(ns.to(device), na.to(device))
                nv = next_value.item()
        if records is not None:
            records.append((s, a, chosen.item(), old_log, following["Reward"], v.item(), nv,
                            following["Terminated"] or following["Truncated"]))
        reply = following
        count += 1
    return dict(seed=seed, swap=swap, learner_decisions=count, environment_steps=reply["EnvironmentSteps"],
                terminated=reply["Terminated"], truncated=reply["Truncated"], reward=reply["Reward"], seconds=time.perf_counter() - started)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[2])
    parser.add_argument("--dotnet", type=Path, default=Path(os.environ["LOCALAPPDATA"]) / "Goa2V1Toolchain/dotnet/dotnet.exe")
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--device", choices=["cpu", "cuda"], default="cpu")
    parser.add_argument("--iterations", type=int, default=2)
    parser.add_argument("--rollout", type=int, default=128)
    parser.add_argument("--limit", type=int, default=900)
    parser.add_argument("--seed", type=int, default=701)
    parser.add_argument("--eval-pairs", type=int, default=1)
    parser.add_argument("--max-minutes", type=int, default=15)
    parser.add_argument("--resume", type=Path)
    args = parser.parse_args()
    if not (1 <= args.iterations <= 10 and 16 <= args.rollout <= 512 and 1 <= args.eval_pairs <= 4 and 1 <= args.max_minutes <= 60 and 1 <= args.limit <= 4000):
        raise ValueError("short-run bounds exceeded; long training requires a separate explicit workflow")
    args.output.mkdir(parents=True, exist_ok=False)
    started = time.perf_counter()
    monitor = ResourceMonitor()
    os.environ["CUBLAS_WORKSPACE_CONFIG"] = ":4096:8"
    try:
        require_interactive_memory()
        device = configure(args.device)
    except Exception:
        write(args.output / "failure.json", dict(error=traceback.format_exc(), seconds=time.perf_counter()-started))
        write(args.output / "resources.json", monitor.report())
        raise
    torch.manual_seed(args.seed)
    config = {k: str(v) if isinstance(v, Path) else v for k, v in vars(args).items()}
    config.update(source_commit=subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=args.root, text=True).strip(),
                  source_status=subprocess.check_output(["git", "status", "--porcelain"], cwd=args.root, text=True),
                  torch_version=str(torch.__version__), python_files={p.name: sha(p) for p in Path(__file__).parent.glob("*.py")},
                  opponent_pool=["simple-v1"], gamma=.99, gae_lambda=.95, clip=.2, entropy=.01, lr=.0003,
                  reward="team-terminal-only-v1", discount_clock="learner_decisions", threads=2, seat="wasp swapped 0/1",
                  curriculum="fixed-roster-formal-genesis", evaluation_seeds=[9001+i for i in range(args.eval_pairs)])
    write(args.output / "manifest.json", config)
    episodes, evaluations, learning = [], [], []
    try:
        with Bridge(args.root, args.dotnet, args.output / "authority") as host:
            description = host.call(op="describe")
            encoder = Encoder(description)
            write(args.output / "public-catalog.json", description)
            # One bounded transport/feature probe, reported separately from evaluations.
            probe = host.call(op="reset", seed=61, learner=0, swap=0, limit=1, opponent="simple")
            s, a = encoder.encode(probe["Decision"])
            if not probe["Truncated"]:
                raise RuntimeError("probe expected a learner-boundary truncation")
            cursor, iteration = 0, 0
            model = CandidateNetwork(len(s), a.shape[1]).to(device)
            optimizer = torch.optim.Adam(model.parameters(), lr=.0003, eps=1e-5)
            if args.resume:
                model, data = load_checkpoint(args.resume, encoder, device)
                # Episode-boundary continuation only; keep original seed/configuration.
                for key in ("seed", "limit", "rollout", "device"):
                    if config[key] != data["config"][key]:
                        raise ValueError("resume configuration mismatch: " + key)
                if config["python_files"] != data["config"]["python_files"]:
                    raise ValueError("trainer source changed; explicit migration required")
                optimizer = torch.optim.Adam(model.parameters(), lr=.0003, eps=1e-5)
                optimizer.load_state_dict(data["optimizer"])
                torch.set_rng_state(data["torch_rng"])
                if device.type == "cuda":
                    torch.cuda.set_rng_state_all(data["cuda_rng"])
                cursor, iteration = data["cursor"], data["iteration"]
            initial = copy.deepcopy(model.state_dict())
            checkpoint(args.output / "initial.pt", model, optimizer, encoder, config, cursor, iteration, [])
            # Greedy held-out paired evaluation; not used by the optimizer.
            def evaluate(label):
                for i in range(args.eval_pairs):
                    for swap in (0, 1):
                        require_interactive_memory()
                        if time.perf_counter() - started > args.max_minutes * 60:
                            return
                        r = run_episode(host, model, encoder, device, 9001+i, swap, args.limit)
                        r["checkpoint"] = label; evaluations.append(r)
                        write(args.output / "evaluation.json", evaluations)
                        print(json.dumps(r), flush=True)
                write(args.output / (label + ".evaluation.json"), dict(checkpoint_sha256=sha(args.output / label),
                      contract=encoder.contract, results=[r for r in evaluations if r["checkpoint"] == label]))
            evaluate("initial.pt")
            for _ in range(args.iterations):
                if time.perf_counter() - started > args.max_minutes * 60:
                    break
                records = []
                while len(records) < args.rollout:
                    require_interactive_memory()
                    r = run_episode(host, model, encoder, device, args.seed+cursor//2, cursor % 2, args.limit, records)
                    cursor += 1; episodes.append(r)
                    write(args.output / "episodes.json", episodes)
                    print(json.dumps(r), flush=True)
                    if r["learner_decisions"] == 0:
                        raise ValueError("step limit exhausted before first learning action; increase limit")
                    if time.perf_counter() - started > args.max_minutes * 60:
                        break
                metrics = update(model, optimizer, records, device)
                if not metrics:
                    raise RuntimeError("no optimizer step executed")
                iteration += 1
                learning.append(dict(iteration=iteration, samples=len(records), optimizer_steps=len(metrics), metrics=metrics))
                checkpoint(args.output / f"checkpoint-{iteration:03}.pt", model, optimizer, encoder, config, cursor, iteration, learning)
                write(args.output / "learning.json", learning)
                print(f"PPO iteration={iteration} samples={len(records)} optimizer_steps={len(metrics)}", flush=True)
            if not learning:
                raise RuntimeError("time budget exhausted before learning; preserved initial evaluation")
            final_path = args.output / f"checkpoint-{iteration:03}.pt"
            restored, _ = load_checkpoint(final_path, encoder, device)
            with torch.no_grad():
                before, bv = model(s.to(device), a.to(device))
                after, av = restored(s.to(device), a.to(device))
                assert torch.equal(before.logits, after.logits) and torch.equal(bv, av), "restored inference differs"
            evaluate(final_path.name)
            delta = sum((model.state_dict()[k].cpu() - initial[k].cpu()).abs().sum().item() for k in initial)
            write(args.output / "summary.json", dict(seconds=time.perf_counter()-started, parameter_l1_change=delta,
                  checkpoint_inference_equal=True, iterations=len(learning), learner_samples=sum(x["samples"] for x in learning),
                  training_episodes=episodes, evaluation=evaluations, gpu_allocated_mib=torch.cuda.max_memory_allocated()/2**20 if device.type=="cuda" else 0,
                  conclusion="Learning pipeline validation only; no demonstrated strength gain."))
    except Exception:
        write(args.output / "failure.json", dict(error=traceback.format_exc(), seconds=time.perf_counter()-started))
        raise
    finally:
        write(args.output / "resources.json", monitor.report())


if __name__ == "__main__":
    main()
