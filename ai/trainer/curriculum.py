"""Bounded public-input imitation warmup, then PPO; teaching and formal scores stay separate."""
import argparse
import json
import os
from pathlib import Path
import subprocess
import time
import traceback
import torch
from bridge import Bridge
from policy import CandidateNetwork, Encoder, update
from train import checkpoint, configure, load_checkpoint, run_episode, sha, write
from resources import ResourceMonitor, require_interactive_memory


def examples(path, encoder):
    rows = [json.loads(line) for line in path.read_text(encoding="utf-8").splitlines()]
    groups = {}
    hashes = {}
    result = {"train": [], "holdout": []}
    for row in rows:
        split = row["Split"]
        if split not in result:
            raise ValueError("unknown teaching split")
        for registry, key in ((groups, row["Group"]), (hashes, row["SourceHash"])):
            if registry.setdefault(key, split) != split:
                raise ValueError("teaching source leakage across splits")
        d = row["Decision"]
        s, a = encoder.encode(d)
        ids = [x["Id"] for x in d["Actions"]]
        labels = row["Preferred"]
        if not labels or len(set(labels)) != len(labels) or not set(labels) <= set(ids):
            raise ValueError("invalid teacher labels")
        result[split].append((s, a, torch.tensor([ids.index(x) for x in labels]), d["Observation"]["Decision"]))
    if not all(result.values()):
        raise ValueError("both grouped splits required")
    return result


def imitation_loss(model, example):
    s, a, labels, _ = example
    dist, _ = model(s, a)
    # Any tied teacher preference is acceptable; never punish another equally good label.
    return -torch.logsumexp(dist.logits[labels], dim=0)


@torch.no_grad()
def teaching_score(model, rows):
    groups = {}
    for s, a, labels, family in rows:
        dist, _ = model(s, a)
        item = groups.setdefault(family, dict(count=0, correct=0, probability=0.))
        item["count"] += 1
        item["correct"] += int(dist.probs.argmax().item() in labels.tolist())
        item["probability"] += dist.probs[labels].sum().item()
    for item in groups.values():
        item["accuracy"] = item["correct"] / item["count"]
        item["probability"] /= item["count"]
    return dict(count=len(rows), accuracy=sum(x["correct"] for x in groups.values()) / len(rows),
                macro_accuracy=sum(x["accuracy"] for x in groups.values()) / len(groups), families=groups)


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[2])
    p.add_argument("--dotnet", type=Path, default=Path(os.environ["LOCALAPPDATA"]) / "Goa2V1Toolchain/dotnet/dotnet.exe")
    p.add_argument("--data", type=Path, required=True)
    p.add_argument("--output", type=Path, required=True)
    p.add_argument("--max-minutes", type=int, default=15)
    p.add_argument("--warmup-updates", type=int, default=200)
    p.add_argument("--eval-seed", type=int, default=11001)
    p.add_argument("--eval-pairs", type=int, default=2)
    args = p.parse_args()
    if not 1 <= args.max_minutes <= 30 or not 1 <= args.warmup_updates <= 500 or not 1 <= args.eval_pairs <= 2:
        raise ValueError("short experiment bounds exceeded")
    args.output.mkdir(parents=True, exist_ok=False)
    started = time.perf_counter(); monitor = ResourceMonitor()
    config = dict(seed=1701, device="cpu", limit=900, rollout=128, lr=.0003, threads=2,
                  source_commit=subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=args.root, text=True).strip(),
                  source_status=subprocess.check_output(["git", "status", "--porcelain"], cwd=args.root, text=True),
                  python_files={f.name: sha(f) for f in Path(__file__).parent.glob("*.py")},
                  data_sha256=sha(args.data / "policy.jsonl"), sources_sha256=sha(args.data / "sources.json"),
                  teacher="simple-v1 public only; equally preferred labels", sampling="uniform decision family then uniform row",
                  opponent_pool=["simple-v1"], reward="team-terminal-only-v1", gamma=.99, gae_lambda=.95,
                  warmup_updates=args.warmup_updates, eval_seeds=[args.eval_seed+i for i in range(args.eval_pairs)],
                  max_minutes=args.max_minutes, torch_version=str(torch.__version__))
    write(args.output / "manifest.json", config)
    evaluations, teaching, episodes = [], {}, []
    def boundary():
        require_interactive_memory()
        if time.perf_counter() - started > args.max_minutes * 60:
            raise TimeoutError("short experiment boundary time limit reached; preserve partial evidence")
    try:
        boundary(); device = configure("cpu"); torch.manual_seed(config["seed"])
        with Bridge(args.root, args.dotnet, args.output / "authority") as host:
            description = host.call(op="describe"); encoder = Encoder(description)
            if json.loads((args.data / "contract.json").read_text()) != encoder.contract:
                raise ValueError("teaching contract incompatible")
            write(args.output / "public-catalog.json", description)
            data = examples(args.data / "policy.jsonl", encoder)
            s, a = data["train"][0][:2]
            model = CandidateNetwork(len(s), a.shape[1]); optimizer = torch.optim.Adam(model.parameters(), lr=.0003, eps=1e-5)
            def evaluate(label, cursor, metrics):
                boundary()
                target = args.output / (label + ".pt")
                checkpoint(target, model, optimizer, encoder, config, cursor, int(label == "ppo"), metrics)
                restored, _ = load_checkpoint(target, encoder, device)
                with torch.no_grad():
                    if not torch.equal(model(s, a)[0].logits, restored(s, a)[0].logits):
                        raise ValueError("checkpoint inference differs")
                teaching[label] = {split: teaching_score(model, rows) for split, rows in data.items()}
                write(args.output / "teaching.json", teaching)
                for seed in config["eval_seeds"]:
                    for swap in (0, 1):
                        boundary()
                        r = run_episode(host, model, encoder, device, seed, swap, config["limit"])
                        r["checkpoint"] = label; evaluations.append(r)
                        write(args.output / "evaluation.json", evaluations); print(json.dumps(r), flush=True)
                write(args.output / (label + ".pt.evaluation.json"), dict(checkpoint_sha256=sha(target), contract=encoder.contract,
                      results=[r for r in evaluations if r["checkpoint"] == label]))
            evaluate("initial", 0, [])
            families = {}
            for row in data["train"]:
                families.setdefault(row[3], []).append(row)
            pools = [families[k] for k in sorted(families)]
            losses = []
            for i in range(args.warmup_updates):
                if i % 20 == 0:
                    boundary()
                batch = []
                for _ in range(32):
                    pool = pools[torch.randint(len(pools), ()).item()]
                    batch.append(pool[torch.randint(len(pool), ()).item()])
                loss = torch.stack([imitation_loss(model, row) for row in batch]).mean()
                optimizer.zero_grad(); loss.backward()
                torch.nn.utils.clip_grad_norm_(model.parameters(), .5, error_if_nonfinite=True)
                optimizer.step(); losses.append(loss.item())
            write(args.output / "imitation-loss.json", losses)
            evaluate("warmup", 0, losses)
            # Reset optimization moments when changing objective; the critic was not taught victory by imitation.
            optimizer = torch.optim.Adam(model.parameters(), lr=.0003, eps=1e-5)
            records, cursor = [], 0
            while len(records) < config["rollout"]:
                boundary()
                r = run_episode(host, model, encoder, device, config["seed"]+cursor//2, cursor % 2, config["limit"], records)
                episodes.append(r); cursor += 1
                write(args.output / "episodes.json", episodes); print(json.dumps(r), flush=True)
            metrics = update(model, optimizer, records, device)
            if not metrics:
                raise ValueError("no PPO update executed")
            write(args.output / "learning.json", dict(samples=len(records), optimizer_steps=len(metrics), metrics=metrics))
            evaluate("ppo", cursor, metrics)
            write(args.output / "summary.json", dict(seconds=time.perf_counter()-started, teaching=teaching,
                  training_episodes=episodes, evaluation=evaluations, ppo_samples=len(records), optimizer_steps=len(metrics),
                  conclusion="Teaching is imitation of simple-v1. Formal held-out games alone measure team outcomes."))
    except Exception:
        write(args.output / "failure.json", dict(error=traceback.format_exc(), seconds=time.perf_counter()-started))
        raise
    finally:
        write(args.output / "resources.json", monitor.report())


if __name__ == "__main__":
    main()
