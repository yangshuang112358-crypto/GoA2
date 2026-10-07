"""Freeze bounded curriculum evidence; authority and policy inputs remain separate."""
import gzip
import hashlib
import json
from pathlib import Path
import shutil

root = Path(__file__).resolve().parents[1]
target = root / "docs/verification/ai-stage3-20261007"
target.mkdir(parents=True, exist_ok=False)


def copy(source, dest, compress=False):
    dest.parent.mkdir(parents=True, exist_ok=True)
    if compress:
        dest = dest.with_suffix(dest.suffix + ".gz")
        dest.write_bytes(gzip.compress(source.read_bytes(), mtime=0))
    else:
        shutil.copy2(source, dest)


for name in ("teaching-v3-01", "curriculum-v3-01", "curriculum-replay", "unity-curriculum-replay"):
    source = root / "artifacts/ai-training" / name
    if not source.exists():
        raise FileNotFoundError(source)
    for f in sorted(source.rglob("*")):
        if f.is_file():
            copy(f, target / name / f.relative_to(source), f.suffix in ("jsonl",) or f.name in ("commands.json", "final-save.json") or f.name.endswith("-source.json"))
copy(root / "ai/Goa2.Ai.Tests/TestResults/ai-stage3.trx", target / "ai-stage3.trx")
files = {str(f.relative_to(target)).replace("\\", "/"): hashlib.sha256(f.read_bytes()).hexdigest()
         for f in sorted(target.rglob("*")) if f.is_file()}
(target / "sha256.json").write_text(json.dumps(files, indent=2), encoding="utf-8")
print(len(files), "files", sum(f.stat().st_size for f in target.rglob("*") if f.is_file()) / 2**20, "MiB")
