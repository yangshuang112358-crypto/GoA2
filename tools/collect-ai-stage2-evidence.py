"""Copy selected immutable stage-2 evidence, keeping model inputs separate from authority."""
import gzip
import hashlib
import json
from pathlib import Path
import shutil

root = Path(__file__).resolve().parents[1]
source = root / "artifacts/ai-training"
dest = root / "docs/verification/ai-stage2-20261007"
dest.mkdir(exist_ok=False)


def copy(src, target):
    target = dest / target
    target.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(src, target)


for run in ("cpu-smoke-01", "cpu-resume-01"):
    for p in (source / run).glob("*.json"):
        copy(p, Path(run) / p.name)
    for p in (source / run).glob("*.pt"):
        if run == "cpu-resume-01" and p.name == "initial.pt":
            continue  # The same model weights are already checkpoint-002; keep metadata and equality evidence.
        copy(p, Path(run) / p.name)

for run in ("gpu-smoke-01", "gpu-smoke-02"):
    for name in ("failure.json", "resources.json", "manifest.json", "evaluation.json"):
        p = source / run / name
        if p.exists():
            copy(p, Path(run) / name)

for run in ("profile-default", "profile-life10-marks4"):
    for name in ("summary.json", "results.json", "manifest.json", "contract.json"):
        copy(source / run / name, Path(run) / name)
    for p in (source / run / "content").rglob("*.json"):
        copy(p, Path(run) / p.relative_to(source / run))
    for episode in (source / run).glob("ai-*"):
        for name in ("final-save.json", "commands.json"):
            target = dest / run / episode.name / (name + ".gz")
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(gzip.compress((episode / name).read_bytes(), mtime=0))
        copy(episode / "result.json", Path(run) / episode.name / "result.json")

for folder in ("model-replay", "unity-model-replay"):
    for p in (source / folder).iterdir():
        if p.is_file():
            copy(p, Path(folder) / p.name)

for name in ("policy.jsonl.gz", "spectator.jsonl.gz", "config.json"):
    copy(source / "cpu-smoke-01/authority/train-0007-9001-0" / name, Path("model-replay") / name)

for name in ("behavior.json", "resume-consistency.json", "profile-outcomes.json", "profile-model-rejection.json"):
    copy(source / name, name)
for name in ("final.trx", "python-tests.txt", "stable-hashes-after.json", "stable-hashes-confirmed.json", "checkpoint-build-compatible.json"):
    copy(root / "artifacts/ai/stage2-tests" / name, Path("tests") / name)

hashes = {p.relative_to(dest).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(dest.rglob("*")) if p.is_file()}
(dest / "sha256.json").write_text(json.dumps(hashes, indent=2), encoding="utf-8")
print(f"Captured {len(hashes)} files, {sum(p.stat().st_size for p in dest.rglob('*') if p.is_file())/2**20:.2f} MiB")
