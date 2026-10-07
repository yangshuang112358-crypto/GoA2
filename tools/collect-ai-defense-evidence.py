"""Archive the bounded defense experiment; preserve all failed/truncated trajectories too."""
import gzip
import hashlib
import json
from pathlib import Path
import shutil

root = Path(__file__).resolve().parents[1]
target = root / "docs/verification/ai-stage4-20261007"
target.mkdir(parents=True, exist_ok=False)
for name in ("defense-data-01", "model-visited-data-01", "defense-finetune-01", "defense-replay", "unity-defense-replay", "defense-video"):
    source = root / "artifacts/ai-training" / name
    if not source.exists():
        raise FileNotFoundError(source)
    for f in sorted(source.rglob("*")):
        if not f.is_file():
            continue
        # The raw window frames and rejected static GDI attempt remain in local artifacts.
        if any(p.endswith("-frames") for p in f.relative_to(source).parts[:-1]) or f.name in ("parent-raw.mp4", "parent-frame.png"):
            continue
        dest = target / name / f.relative_to(source)
        dest.parent.mkdir(parents=True, exist_ok=True)
        if f.suffix == ".jsonl" or f.name in ("commands.json", "final-save.json") or f.name.endswith(("-source.json", ".save.json")):
            dest.with_suffix(dest.suffix + ".gz").write_bytes(gzip.compress(f.read_bytes(), mtime=0))
        else:
            shutil.copy2(f, dest)
shutil.copy2(root / "ai/Goa2.Ai.Tests/TestResults/ai-stage4.trx", target / "ai-stage4.trx")
files = {str(f.relative_to(target)).replace("\\", "/"): hashlib.sha256(f.read_bytes()).hexdigest()
         for f in sorted(target.rglob("*")) if f.is_file()}
(target / "sha256.json").write_text(json.dumps(files, indent=2), encoding="utf-8")
print(len(files), "files", sum(f.stat().st_size for f in target.rglob("*") if f.is_file())/2**20, "MiB")
