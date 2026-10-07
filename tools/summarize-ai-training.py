"""Summarize captured evidence without importing torch or reading authority saves."""
import gzip
import hashlib
import json
import sys
from pathlib import Path

root = Path(sys.argv[1])
summary = json.loads((root / "summary.json").read_text(encoding="utf-8"))
formal, probes, decisions, multi, violations = [], [], 0, 0, []
for episode in sorted((root / "authority").iterdir()):
    if not episode.is_dir():
        continue
    config = json.loads((episode / "config.json").read_text(encoding="utf-8-sig"))
    result = json.loads((episode / "result.json").read_text(encoding="utf-8-sig"))
    if config["limit"] == 1 and config["seed"] == 61:
        probes.append(result)
        continue
    formal.append(result)
    with gzip.open(episode / "policy.jsonl.gz", "rt", encoding="utf-8-sig") as trace:
        for line in trace:
            row = json.loads(line)
            actions = row["Input"]["Actions"]
            decisions += 1
            multi += len(actions) > 1
            if row["Selected"] not in {a["Id"] for a in actions} or row["Input"]["Observation"]["Seat"] != config["learner"]:
                violations.append(episode.name)
totals = dict(formal_episodes=len(formal), completed=sum(r["Stop"] == "terminated" for r in formal),
              truncated=sum(r["Stop"] == "truncated" for r in formal), environment_steps=sum(r["EnvironmentSteps"] for r in formal),
              commands=sum(r["Commands"] for r in formal), learner_decisions=decisions, multichoice_decisions=multi,
              trace_violations=violations, authority_failures=len(list((root / "authority").glob("*/failure.json"))),
              all_restore_verified=all(r["RestoreVerified"] for r in formal), excluded_transport_probes=len(probes),
              elapsed_seconds=summary["seconds"], raw_steps_per_pipeline_second=sum(r["EnvironmentSteps"] for r in formal)/summary["seconds"],
              learner_steps_per_pipeline_second=decisions/summary["seconds"],
              resources=json.loads((root / "resources.json").read_text(encoding="utf-8")),
              checkpoints={p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in root.glob("*.pt")})
(root / "aggregate.json").write_text(json.dumps(totals, indent=2), encoding="utf-8")
print(json.dumps(totals, indent=2))
