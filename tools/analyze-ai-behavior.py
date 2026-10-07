"""Descriptive counters from allowed policy traces, not an alternative game score."""
import collections
import gzip
import json
from pathlib import Path
import sys

root = Path(sys.argv[1])
groups = {
    "initial": [root / "cpu-smoke-01/authority" / f"train-000{i+1}-9001-{i}" for i in range(2)],
    "ppo2": [root / "cpu-smoke-01/authority" / f"train-000{i+7}-9001-{i}" for i in range(2)],
    "ppo3": [root / "cpu-resume-01/authority" / f"train-000{i+4}-9001-{i}" for i in range(2)],
}
result = {}
for name, paths in groups.items():
    c = collections.Counter()
    for path in paths:
        with gzip.open(path / "policy.jsonl.gz", "rt", encoding="utf-8-sig") as trace:
            for line in trace:
                row = json.loads(line)
                actions = row["Input"]["Actions"]
                chosen = next(a for a in actions if a["Id"] == row["Selected"])
                c["decisions"] += 1
                if any(a["SuccessfulDefense"] for a in actions):
                    c["successful_defense_available"] += 1
                    c["successful_defense_chosen"] += chosen["SuccessfulDefense"]
                if any(a["Kind"] == "BeginPrimary" and not a["ImmediateSkip"] for a in actions):
                    c["primary_available"] += 1
                    c["pass_with_primary_available"] += chosen["Kind"] == "Pass"
    result[name] = dict(c)
(root / "behavior.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
print(json.dumps(result, indent=2))
