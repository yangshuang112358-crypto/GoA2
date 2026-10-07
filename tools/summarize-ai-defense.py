import collections
import gzip
import json
from pathlib import Path
import statistics
import sys

run = Path(sys.argv[1])
summary = json.loads((run / "summary.json").read_text())
games = summary["evaluation"]
paths = sorted((run / "authority").glob("*/result.json"))
assert len(paths) == len(games)
assert not list(run.rglob("failure.json"))
stages, seconds = {}, []
for game, path in zip(games, paths):
    config = json.loads((path.parent / "config.json").read_text())
    result = json.loads(path.read_text())
    assert (config["seed"], config["swap"]) == (game["seed"], game["swap"])
    assert result["RestoreVerified"] and result["EnvironmentSteps"] == game["environment_steps"]
    stage = stages.setdefault(game["checkpoint"], collections.Counter())
    stage["wins"] += game["reward"] == 1; stage["losses"] += game["reward"] == -1
    stage["truncated"] += game["truncated"]; stage["terminated"] += game["terminated"]
    if game["terminated"]:
        seconds.append(result["Seconds"])
    with gzip.open(path.parent / "policy.jsonl.gz", "rt", encoding="utf-8-sig") as trace:
        for line in trace:
            row = json.loads(line); actions = row["Input"]["Actions"]
            chosen = next(a for a in actions if a["Id"] == row["Selected"])
            if any(a["SuccessfulDefense"] for a in actions):
                stage["defense_opportunities"] += 1
                stage["successful_defense_selected"] += chosen["SuccessfulDefense"]
            if any(a["Kind"] == "BeginPrimary" and not a["ImmediateSkip"] for a in actions):
                stage["primary_available"] += 1; stage["pass_with_primary"] += chosen["Kind"] == "Pass"
steps = sum(g["environment_steps"] for g in games)
result = dict(seconds=summary["seconds"], environment_steps=steps, steps_per_second=steps/summary["seconds"],
              games=len(games), illegal_commands=0, exceptions=0, restore_verified=len(games),
              completed_seconds=dict(min=min(seconds), median=statistics.median(seconds), max=max(seconds)) if seconds else None,
              stages=stages, resources=json.loads((run / "resources.json").read_text()))
(run / "benchmark.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
print(json.dumps(result, indent=2))
