"""Archive every game of the bounded navigation comparison, including losses."""
import gzip
import hashlib
import json
from pathlib import Path
import shutil

root = Path(__file__).resolve().parents[1]
target = root / "docs/verification/ai-stage5-20261007"
sources = [root / "artifacts/ai-training" / name for name in
           ("navigation-finetune-01", "navigation-replay", "unity-navigation-replay")]
for source in sources:
    if not source.is_dir():
        raise FileNotFoundError(source)
if not json.loads((sources[2] / "verification.json").read_text(encoding="utf-8-sig"))["passed"]:
    raise ValueError("Unity replay verification incomplete")
target.mkdir(parents=True, exist_ok=False)
for source in sources:
    for f in sorted(source.rglob("*")):
        if not f.is_file():
            continue
        dest = target / source.name / f.relative_to(source)
        dest.parent.mkdir(parents=True, exist_ok=True)
        if f.suffix == ".jsonl" or f.name in ("commands.json", "final-save.json") or f.name.endswith(".save.json"):
            dest.with_suffix(dest.suffix + ".gz").write_bytes(gzip.compress(f.read_bytes(), mtime=0))
        else:
            shutil.copy2(f, dest)
files = {f.relative_to(target).as_posix(): hashlib.sha256(f.read_bytes()).hexdigest()
         for f in sorted(target.rglob("*")) if f.is_file()}
(target / "sha256.json").write_text(json.dumps(files, indent=2), encoding="utf-8")
print(len(files), "files", sum(f.stat().st_size for f in target.rglob("*") if f.is_file()) / 2**20, "MiB")
