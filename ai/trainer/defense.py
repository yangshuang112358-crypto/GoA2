"""Explicit supervised fine-tuning, with legal C# labels and frozen independent evaluations.

No runtime action override. Defense ranking is a training objective, not a game reward.
"""
import argparse
import gzip
import json
import os
from pathlib import Path
import subprocess
import time
import traceback
import torch
from bridge import Bridge
from curriculum import imitation_loss, teaching_score
from policy import Encoder
from resources import ResourceMonitor, require_interactive_memory
from train import checkpoint, configure, load_checkpoint, run_episode, sha, write


def load_data(directories, encoder):
    result = {"train": [], "holdout": []}
    groups, hashes = {}, {}
    for directory in directories:
        if json.loads((directory / "contract.json").read_text()) != encoder.contract:
            raise ValueError("incompatible teaching contract")
        path = directory / "policy.jsonl"
        if path.exists():
            raw = path.read_text(encoding="utf-8")
        else:
            raw = gzip.decompress(path.with_suffix(".jsonl.gz").read_bytes()).decode("utf-8")
        for line in raw.splitlines():
            row = json.loads(line); split = row["Split"]
            if split not in result:
                raise ValueError("unknown teaching split")
            for registry, key in ((groups, row["Group"]), (hashes, row["SourceHash"])):
                if registry.setdefault(key, split) != split:
                    raise ValueError("cross-dataset source leakage")
            d = row["Decision"]; s, a = encoder.encode(d)
            ids = [x["Id"] for x in d["Actions"]]
            labels = row["Preferred"]
            if not labels or len(set(labels)) != len(labels) or not set(labels) <= set(ids):
                raise ValueError("invalid teacher labels")
            good = torch.tensor([x["Kind"] == "Defend" and x["SuccessfulDefense"] for x in d["Actions"]])
            result[split].append((s, a, torch.tensor([ids.index(x) for x in labels]), d["Observation"]["Decision"], good, row["Group"]))
    if not all(result.values()):
        raise ValueError("both grouped splits required")
    return result


def defense_ranking_loss(logits, good, margin=2.):
    if not good.any() or good.all():
        raise ValueError("defense contrast needs both successful and unsuccessful candidates")
    differences = logits[good][:, None] - logits[~good][None, :]
    return torch.nn.functional.softplus(margin - differences).mean()


@torch.no_grad()
def score(model, rows):
    report = teaching_score(model, [r[:4] for r in rows])
    windows = [r for r in rows if r[4].any() and not r[4].all()]
    details = []
    for s, a, _, _, good, group in windows:
        dist, _ = model(s, a)
        details.append(dict(group=group, success=bool(good[dist.probs.argmax()]), probability=dist.probs[good].sum().item()))
    report["defense"] = dict(windows=len(details), correct=sum(r["success"] for r in details),
                             mean_probability=sum(r["probability"] for r in details)/max(1, len(details)), details=details)
    return report


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[2])
    p.add_argument("--dotnet", type=Path, default=Path(os.environ["LOCALAPPDATA"]) / "Goa2V1Toolchain/dotnet/dotnet.exe")
    p.add_argument("--data", type=Path, nargs="+", required=True)
    p.add_argument("--parent", type=Path, required=True)
    p.add_argument("--output", type=Path, required=True)
    p.add_argument("--updates", type=int, default=300)
    p.add_argument("--max-minutes", type=int, default=15)
    p.add_argument("--eval-seed", type=int, default=21001)
    p.add_argument("--eval-pairs", type=int, default=2)
    p.add_argument("--limit", type=int, default=1200)
    args = p.parse_args()
    if not (1 <= args.updates <= 500 and 1 <= args.max_minutes <= 30 and 1 <= args.eval_pairs <= 2 and 1 <= args.limit <= 1500):
        raise ValueError("bounded fine-tuning limits exceeded")
    args.output.mkdir(parents=True, exist_ok=False)
    started = time.perf_counter(); monitor = ResourceMonitor()
    config = dict(source_commit=subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=args.root, text=True).strip(),
                  source_status=subprocess.check_output(["git", "status", "--porcelain"], cwd=args.root, text=True),
                  python_files={f.name: sha(f) for f in Path(__file__).parent.glob("*.py")},
                  data=[dict(path=str(d), files={f.name: sha(f) for f in d.iterdir() if f.name in ("policy.jsonl", "policy.jsonl.gz", "contract.json", "sources.json")}) for d in args.data],
                  parent=str(args.parent), parent_sha256=sha(args.parent), initialization="explicit compatible weight fine-tune; reset optimizer and RNG",
                  seed=2701, updates=args.updates, lr=.001, batch=32, defense_samples_per_batch=16, ranking_margin=2.,
                  objective="half balanced public-teacher imitation + half successful-defense pairwise ranking",
                  device="cpu", threads=2, max_minutes=args.max_minutes, eval_seeds=[args.eval_seed+i for i in range(args.eval_pairs)],
                  limit=args.limit, opponent_pool=["simple-v1"], torch_version=str(torch.__version__),
                  runtime_override=False, ppo_updates=0)
    write(args.output / "manifest.json", config)
    evaluations, teaching = [], {}
    def boundary():
        require_interactive_memory()
        if time.perf_counter()-started > args.max_minutes*60:
            raise TimeoutError("fine-tuning time boundary reached; partial evidence retained")
    try:
        boundary(); device = configure("cpu"); torch.manual_seed(config["seed"])
        with Bridge(args.root, args.dotnet, args.output / "authority") as host:
            description = host.call(op="describe"); encoder = Encoder(description)
            write(args.output / "public-catalog.json", description)
            data = load_data(args.data, encoder)
            model, parent = load_checkpoint(args.parent, encoder, device)
            config["parent_training_config"] = parent["config"]
            write(args.output / "manifest.json", config)
            optimizer = torch.optim.Adam(model.parameters(), lr=config["lr"], eps=1e-5)
            s, a = data["train"][0][:2]
            def evaluate(label, metrics):
                boundary(); path = args.output / (label + ".pt")
                checkpoint(path, model, optimizer, encoder, config, 0, 0, metrics)
                with torch.random.fork_rng(devices=[]):
                    restored, _ = load_checkpoint(path, encoder, device)
                    with torch.no_grad():
                        if not torch.equal(model(s, a)[0].logits, restored(s, a)[0].logits):
                            raise ValueError("restored inference differs")
                teaching[label] = {split: score(model, rows) for split, rows in data.items()}
                write(args.output / "teaching.json", teaching)
                for seed in config["eval_seeds"]:
                    for swap in (0, 1):
                        boundary()
                        r = run_episode(host, model, encoder, device, seed, swap, config["limit"])
                        r["checkpoint"] = label; evaluations.append(r)
                        write(args.output / "evaluation.json", evaluations); print(json.dumps(r), flush=True)
                write(args.output / (label + ".pt.evaluation.json"), dict(checkpoint_sha256=sha(path), contract=encoder.contract,
                      results=[r for r in evaluations if r["checkpoint"] == label]))
            evaluate("parent", [])
            families = {}
            for row in data["train"]:
                families.setdefault(row[3], []).append(row)
            pools = [families[k] for k in sorted(families)]
            defenses = [r for r in data["train"] if r[4].any() and not r[4].all()]
            if not defenses:
                raise ValueError("no defense training contrasts")
            metrics = []
            for i in range(args.updates):
                if i % 20 == 0:
                    boundary()
                losses = []
                for _ in range(16):
                    pool = pools[torch.randint(len(pools), ()).item()]
                    row = pool[torch.randint(len(pool), ()).item()]
                    losses.append(imitation_loss(model, row[:4]))
                    ds, da, _, _, good, _ = defenses[torch.randint(len(defenses), ()).item()]
                    logits = model(ds, da)[0].logits
                    losses.append(defense_ranking_loss(logits, good))
                loss = torch.stack(losses).mean()
                optimizer.zero_grad(); loss.backward()
                torch.nn.utils.clip_grad_norm_(model.parameters(), .5, error_if_nonfinite=True)
                optimizer.step(); metrics.append(loss.item())
            write(args.output / "learning.json", dict(updates=len(metrics), losses=metrics, contrasts=len(defenses)))
            evaluate("defense", metrics)
            write(args.output / "summary.json", dict(seconds=time.perf_counter()-started, teaching=teaching,
                  evaluation=evaluations, updates=len(metrics), training_episodes=[],
                  conclusion="Supervised defense reliability experiment; no additional PPO or runtime action override."))
    except Exception:
        write(args.output / "failure.json", dict(error=traceback.format_exc(), seconds=time.perf_counter()-started))
        raise
    finally:
        write(args.output / "resources.json", monitor.report())


if __name__ == "__main__":
    main()
