"""Portable four-player distribution. Never packages local room/ticket directories."""
import argparse
import json
from pathlib import Path
import shutil
import subprocess
import zipfile
from prepare_easytier import prepare as prepare_easytier

from player_package import file_digest, verify_build, require, regular_file, safe_relative, strict_json


def records(root):
    return [{"Path": p.relative_to(root).as_posix(), "Bytes": p.stat().st_size,
             "Sha256": file_digest(p)} for p in sorted(root.rglob("*")) if p.is_file()]


def verify(folder):
    folder = Path(folder).resolve()
    manifest = strict_json((folder / "multiplayer-build.json").read_text(encoding="utf-8"))
    require(manifest["SchemaVersion"] == 1, "Unsupported multiplayer package")
    names = {safe_relative(e["Path"]) for e in manifest["Files"]}
    require(len(names) == len(manifest["Files"]), "Duplicate package file")
    actual = {p.relative_to(folder).as_posix() for p in folder.rglob("*") if p.is_file()}
    require(actual == names | {"multiplayer-build.json"}, "Unlisted or missing package files")
    required = {"Start-Multiplayer.cmd", "launcher/Launcher.ps1", "launcher/RoomTools.ps1",
                "host/Goa2.Network.exe", "host/Goa2.Network.runtimeconfig.json", "host/coreclr.dll",
                "host/hostfxr.dll", "content/manifest.json", "player/build-info.json"}
    if manifest.get("AutomaticInviteVersion") == 1:
        required.update({"launcher/AutoLauncher.ps1", "launcher/ManualLauncher.ps1", "launcher/BootstrapTools.ps1",
                         "launcher/BootstrapWorker.ps1", "launcher/OwnedProcessJob.cs", "easytier/easytier-core.exe",
                         "easytier/easytier-cli.exe", "easytier/component.json", "easytier/LICENSE-LGPL-3.0.txt",
                         "easytier/DEPENDENCY-NOTICES.txt", "easytier/Goa2-EasyTier-2.6.4-modified-source.zip"})
    require(required <= names, "Incomplete portable package")
    require(not any(".private." in n or n.endswith(".log") for n in names), "Session data in distribution")
    for entry in manifest["Files"]:
        file = regular_file(folder, entry["Path"])
        require(file.stat().st_size == entry["Bytes"] and file_digest(file) == entry["Sha256"],
                "Package file changed: " + entry["Path"])
    runtime = strict_json((folder / "host/Goa2.Network.runtimeconfig.json").read_text())
    require("includedFrameworks" in runtime["runtimeOptions"] and
            "framework" not in runtime["runtimeOptions"] and "frameworks" not in runtime["runtimeOptions"],
            "Host requires an installed runtime")
    player = verify_build(folder / "player")
    return {"passed": True, "files": len(names), "player": player, "source_commit": manifest["SourceCommit"]}


def create(root, host, destination):
    root, host, destination = (Path(p).resolve() for p in (root, host, destination))
    verify_build(root / "artifacts/player", root)
    require(not destination.exists() and not destination.with_suffix(".zip").exists(), "Choose a new package destination")
    require((host / "Goa2.Network.exe").is_file(), "Publish the host first")
    destination.mkdir(parents=True)
    shutil.copytree(root / "artifacts/player", destination / "player")
    (destination / "host").mkdir()
    for file in host.iterdir():
        if file.is_file() and file.suffix.lower() in (".exe", ".dll", ".json", ".txt"):
            shutil.copy2(file, destination / "host" / file.name)
    for name in ("manifest.json", "canonical/cards.json", "canonical/heroes.json", "canonical/map.json", "canonical/ruleset.json"):
        target = destination / "content" / name
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(root / "content" / name, target)
    (destination / "launcher").mkdir()
    for name in ("RoomTools.ps1", "Launcher.ps1", "AutoLauncher.ps1", "ManualLauncher.ps1", "BootstrapTools.ps1", "BootstrapWorker.ps1", "OwnedProcessJob.cs", "Start-Multiplayer.cmd", "README.txt"):
        target = destination / "launcher" / name if name.endswith((".ps1", ".cs")) else destination / name
        # Windows PowerShell 5.1 requires BOM for Chinese text in scripts.
        text = (root / "network/launcher" / name).read_text(encoding="utf-8-sig")
        target.write_text(text, encoding="utf-8-sig" if name.endswith(".ps1") else "utf-8", newline="\r\n")
    shutil.copytree(prepare_easytier(root), destination / "easytier")
    source_names = subprocess.check_output(["git", "ls-files", "--cached", "--others", "--exclude-standard"], cwd=root, text=True).splitlines()
    source_names = [n for n in source_names if n.startswith(("core/", "network/", "tools/")) or n in ("Directory.Build.props", "global.json")]
    manifest = {"SchemaVersion": 1,
                "AutomaticInviteVersion": 1,
                "SourceCommit": subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=root, text=True).strip(),
                "SourceFiles": [{"Path": n, "Sha256": file_digest(root / n)} for n in sorted(source_names) if (root / n).is_file()],
                "Files": records(destination)}
    (destination / "multiplayer-build.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    result = verify(destination)
    archive = destination.with_suffix(".zip")
    with zipfile.ZipFile(archive, "x", compression=zipfile.ZIP_DEFLATED, compresslevel=6) as output:
        for entry in records(destination):
            output.write(destination / entry["Path"], "Goa2V1-Multiplayer/" + entry["Path"])
    with zipfile.ZipFile(archive) as check:
        require(check.testzip() is None, "Archive CRC failure")
        expected = {"Goa2V1-Multiplayer/" + e["Path"] for e in records(destination)}
        require(set(check.namelist()) == expected, "Archive inventory mismatch")
    checksum = file_digest(archive)
    archive.with_suffix(".zip.sha256").write_text(checksum + "  " + archive.name + "\n", encoding="ascii")
    result.update({"folder": str(destination), "archive": str(archive), "archive_sha256": checksum})
    destination.with_suffix(".report.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
    return result


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    commands = parser.add_subparsers(dest="command", required=True)
    build = commands.add_parser("create")
    for arg in ("root", "host", "destination"):
        build.add_argument("--" + arg, required=True)
    check = commands.add_parser("verify")
    check.add_argument("folder")
    args = parser.parse_args()
    print(json.dumps(create(args.root, args.host, args.destination) if args.command == "create" else verify(args.folder), indent=2))
