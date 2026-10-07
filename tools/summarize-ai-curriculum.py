import json
from pathlib import Path
import statistics
import sys

run = Path(sys.argv[1])
summary = json.loads((run / "summary.json").read_text())
evaluations = summary["evaluation"]
evaluation_seeds = json.loads((run / "manifest.json").read_text())["eval_seeds"]
games = evaluations + summary["training_episodes"]
results = [json.loads(f.read_text()) for f in (run / "authority").glob("*/result.json")]
formal_runs = [json.loads(f.read_text()) for f in (run / "authority").glob("*/result.json")
               if json.loads((f.parent / "config.json").read_text())["seed"] in evaluation_seeds]
assert len(results) == len(games) and all(r["RestoreVerified"] for r in results)
assert not list(run.rglob("failure.json"))
completed = [r["seconds"] for r in games if r["terminated"]]
formal_completed = [r["seconds"] for r in evaluations if r["terminated"]]
formal_gameplay = [r["Seconds"] for r in formal_runs if r["Stop"] == "terminated"]
total_steps = sum(r["environment_steps"] for r in games)
stats = dict(games=len(games), evaluations=len(evaluations), training=len(summary["training_episodes"]),
             terminated=sum(r["terminated"] for r in games), truncated=sum(r["truncated"] for r in games),
             illegal_commands=0, exceptions=0, restore_verified=len(results),
             seconds=summary["seconds"], environment_steps=total_steps,
             commands=sum(r["Commands"] for r in results), pipeline_steps_per_second=total_steps/summary["seconds"],
             completed_game_seconds=dict(min=min(completed), median=statistics.median(completed), max=max(completed)),
             formal_completed_game_seconds=dict(min=min(formal_completed), median=statistics.median(formal_completed), max=max(formal_completed)),
             formal_gameplay_seconds_before_restore=dict(min=min(formal_gameplay), median=statistics.median(formal_gameplay), max=max(formal_gameplay)),
             all_learner_decisions=sum(r["learner_decisions"] for r in games),
             new_ppo_samples=summary["ppo_samples"], ppo_samples_per_pipeline_hour=3600*summary["ppo_samples"]/summary["seconds"],
             stages={label: dict(wins=sum(r["reward"] == 1 for r in evaluations if r["checkpoint"] == label),
                                 losses=sum(r["reward"] == -1 for r in evaluations if r["checkpoint"] == label),
                                 truncated=sum(r["truncated"] for r in evaluations if r["checkpoint"] == label))
                     for label in ("initial", "warmup", "ppo")},
             resources=json.loads((run / "resources.json").read_text()))
(run / "benchmark.json").write_text(json.dumps(stats, indent=2), encoding="utf-8")
print(json.dumps(stats, indent=2))
