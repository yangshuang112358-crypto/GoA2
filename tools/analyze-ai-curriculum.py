"""Compare exactly the same held-out public decisions, separately from game outcomes."""
import gzip
import json
from pathlib import Path
import sys
import torch

root = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(root / "ai/trainer"))
from policy import Encoder
from train import load_checkpoint, write

data, run = map(Path, sys.argv[1:3])
torch.set_num_threads(2)
encoder = Encoder(json.loads((run / "public-catalog.json").read_text()))
rows = [json.loads(line) for line in (data / "policy.jsonl").read_text(encoding="utf-8").splitlines()]
heldout = [row for row in rows if row["Split"] == "holdout"]
result = {}
for label in ("initial", "warmup", "ppo"):
    model, _ = load_checkpoint(run / (label + ".pt"), encoder, "cpu")
    c = dict(primary_available=0, pass_with_primary=0, successful_defense_available=0, successful_defense_chosen=0)
    for row in heldout:
        d = row["Decision"]; s, a = encoder.encode(d)
        with torch.no_grad():
            choice = model(s, a)[0].probs.argmax().item()
        actions = d["Actions"]; chosen = actions[choice]
        if any(a["Kind"] == "BeginPrimary" and not a["ImmediateSkip"] for a in actions):
            c["primary_available"] += 1
            c["pass_with_primary"] += chosen["Kind"] == "Pass"
        if any(a["SuccessfulDefense"] for a in actions):
            c["successful_defense_available"] += 1
            c["successful_defense_chosen"] += chosen["SuccessfulDefense"]
    result[label] = c
write(run / "fixed-heldout-behavior.json", result)
print(json.dumps(result, indent=2))
