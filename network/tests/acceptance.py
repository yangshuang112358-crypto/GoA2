"""Real server + four independent client processes. Reports never contain bearer secrets."""
import hashlib
import json
import os
from pathlib import Path
import socket
import struct
import subprocess
import sys
import time
import traceback
import uuid

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "network/client"))
from player import Player


class Run:
    def __init__(self, fixture=None, steps=0):
        self.output = ROOT / "artifacts/network" / (time.strftime("%Y%m%d-%H%M%S-") + uuid.uuid4().hex[:8])
        self.output.mkdir(parents=True)
        self.checks = []
        self.clients = []
        self.logs = []
        self.revision = 0
        dotnet = Path(os.environ["LOCALAPPDATA"]) / "Goa2V1Toolchain/dotnet/dotnet.exe"
        assembly = ROOT / "network/Goa2.Network/bin/Release/net10.0/Goa2.Network.dll"
        self.report = {"source_commit": subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip(),
                       "runner": "TCP: one .NET service and four Python client processes", "ui_tested": False,
                       "core_tests_counted": 0, "checks": self.checks, "assemblies": {
                           p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in assembly.parent.glob("*.dll")}}
        log = open(self.output / "server.log", "x", encoding="utf-8")
        self.logs.append(log)
        command = [str(dotnet), str(assembly), "serve", str(ROOT), str(self.output / "private")]
        if fixture:
            test_host = ROOT / "network/tests/Goa2.Network.TestHost/bin/Release/net10.0/Goa2.Network.TestHost.dll"
            command = [str(dotnet), str(test_host), str(ROOT), str(self.output / "private"), str(ROOT / fixture), str(steps)]
            self.report["fixture"] = {"source": fixture, "prefix_steps": steps, "sandbox": True,
                                      "sha256": hashlib.sha256((ROOT / fixture).read_bytes()).hexdigest()}
        self.server = subprocess.Popen(command,
                                       cwd=ROOT, stdin=subprocess.PIPE, stdout=log, stderr=log, text=True)
        self.report["server_pid"] = self.server.pid
        for _ in range(200):
            if (self.output / "private/ready.json").exists():
                break
            if self.server.poll() is not None:
                raise RuntimeError("server failed; inspect server.log")
            time.sleep(.05)
        for seat in range(4):
            err = open(self.output / f"client-{seat}.stderr", "x", encoding="utf-8")
            self.logs.append(err)
            self.clients.append(subprocess.Popen([sys.executable, "-u", str(ROOT / "network/client/player.py"),
                str(self.output / f"private/seat-{seat}.private.json"), str(self.output / f"client-{seat}.jsonl")],
                stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=err, text=True, encoding="utf-8",
                env={**os.environ, "PYTHONIOENCODING": "utf-8"}))
        self.report["client_pids"] = [p.pid for p in self.clients]

    def rpc(self, seat, op, **values):
        process = self.clients[seat]
        process.stdin.write(json.dumps({"op": op, **values}) + "\n")
        process.stdin.flush()
        data = json.loads(process.stdout.readline())
        assert data["ok"], data
        return data["result"]

    def check(self, name, predicate):
        assert predicate, name
        self.checks.append({"name": name, "passed": True})

    def view(self, seat=0):
        return self.rpc(seat, "view", revision=self.revision)

    def command(self, seat, kind, expected="ok", **args):
        self.view(seat)
        result = self.rpc(seat, "submit", kind=kind, args=args)
        assert result["Code"] == expected, (kind, result.get("Code"), result.get("Message"))
        self.revision = max(self.revision, result["Snapshot"]["Revision"])
        return result

    def ticket(self, seat=0):
        return json.loads((self.output / f"private/seat-{seat}.private.json").read_text())

    def raw(self, body, authenticated=False, prefix=None):
        ticket = self.ticket()
        with socket.create_connection((ticket["Host"], ticket["Port"]), timeout=5) as sock:
            def send(value):
                data = value if isinstance(value, bytes) else json.dumps(value).encode()
                sock.sendall(struct.pack("!I", len(data)) + data)
            def receive():
                size = struct.unpack("!I", Player.exact(sock, 4))[0]
                return json.loads(Player.exact(sock, size))
            if authenticated:
                send({"Type": "Hello", "RoomId": ticket["RoomId"], "Credential": ticket["Credential"], **ticket["Capabilities"]})
                assert receive()["Type"] == "Welcome"
            if prefix:
                sock.sendall(prefix)
            else:
                send(body)
            return receive()

    def boundaries(self):
        for seat in range(4):
            result = self.rpc(seat, "connect")
            self.check(f"N01 unique seat {seat}", result["seat"] == seat)
        ticket = self.ticket()
        hello = {"Type": "Hello", "RoomId": ticket["RoomId"], "Credential": "invalid", **ticket["Capabilities"]}
        self.check("N01 fifth credential rejected without snapshot", self.raw(hello) == {"Type": "Error", "Code": "invalid_credentials"})
        for field, value in [("WireVersion", 999), ("EngineVersion", 0), ("ProtocolVersion", "other"),
                             ("ContentHash", "other"), ("ContentVersion", "other"), ("RulesVersion", "other")]:
            response = self.raw({**hello, "Credential": ticket["Credential"], field: value})
            self.check("N15 " + field, response == {"Type": "Error", "Code": "version_mismatch"})
        base = {"Type": "Intent", "CommandId": "invalid", "MatchId": ticket["RoomId"], "ExpectedRevision": 0, "Kind": "ChooseHero", "Value": "wasp"}
        malformed = [b"null", b'{"Type":"Intent","Type":"Intent"}', {**base, "ActorSeat": 1},
                     {**base, "Kind": 0}, {**base, "ExpectedRevision": True}, {**base, "Kind": "DebugPrepare"},
                     {**base, "Value": None}, {k: v for k, v in base.items() if k != "Kind"},
                     {**base, "Kind": "SetQuickSelection"}, {**base, "Kind": "UpgradeEngine"},
                     {**base, "Kind": "Move", "MoveMode": 1}, {**base, "Value": "x" * 257}]
        for i, payload in enumerate(malformed):
            response = self.raw(payload, authenticated=True)
            self.check(f"N14 strict input {i}", response["Type"] == "Error" and "Snapshot" not in response)
        self.check("N14 bounded frame", self.raw(None, prefix=struct.pack("!I", 16385))["Code"] == "frame_too_large")
        self.rpc(0, "connect")
        self.check("N14 malformed input did not execute", self.view()["Revision"] == 0)
        self.command(0, "ChooseHero", Value="wasp")
        first = self.revision
        result = self.rpc(1, "submit", kind="ChooseHero", args={"Value": "shargatha"}, revision=0)
        self.check("N04 stale revision rejected", result["Code"] == "stale_revision" and result["Snapshot"]["Revision"] == first)
        result = self.rpc(1, "submit", kind="ChooseHero", args={"Value": "shargatha"}, match="another-room")
        self.check("N02 wrong match", result["Code"] == "wrong_match")
        identifier = uuid.uuid4().hex
        self.view(1)
        result = self.rpc(1, "submit", kind="ChooseHero", args={"Value": "shargatha"}, command_id=identifier)
        self.revision = result["Snapshot"]["Revision"]
        duplicate = self.rpc(1, "submit", kind="ChooseHero", args={"Value": "shargatha"}, command_id=identifier, revision=first)
        self.check("N05 same command duplicate", duplicate["Duplicate"] and duplicate["Snapshot"]["Revision"] == self.revision)
        conflict = self.rpc(1, "submit", kind="ChooseHero", args={"Value": "arien"}, command_id=identifier, revision=first)
        self.check("N06 changed payload conflicts", conflict["Code"] == "command_id_conflict")
        replacement = Player(self.ticket(1))
        replacement.connect()
        self.check("N11 old connection invalidated", self.rpc(1, "disconnect")["state"] == "Disconnected")
        replacement.disconnect()
        self.rpc(1, "connect")
        self.command(2, "ChooseHero", Value="brogan")
        self.command(3, "ChooseHero", Value="arien")

    def full_round(self):
        while self.view()["Phase"] == "Deployment":
            acted = False
            for seat in range(4):
                deployments = self.view(seat)["Deployments"]
                if deployments:
                    target, cells = next(iter(deployments.items()))
                    self.command(seat, "DeployHero", TargetSeat=int(target), Destination=cells[0])
                    acted = True
                    break
            assert acted, "deployment stalled"
        self.check("ordinary match configuration", not self.view()["Sandbox"] and not self.view()["QuickSelection"])
        for turn in range(1, 5):
            for seat in range(4):
                view = self.view(seat)
                card = next(c for c in view["OwnCards"] if c["Zone"] == "InHand")
                self.command(seat, "SelectCard", Value=card["CardId"])
                self.command(seat, "ConfirmCard")
                if seat < 3:
                    self.check(f"N03 turn {turn} waits after confirmation {seat + 1}", self.view()["Phase"] == "Planning")
                    for other in range(4):
                        other_view = self.view(other)
                        self.check(f"privacy turn {turn} seat {other} own cards only", all(
                            c["CardId"].startswith(other_view["Players"][other]["HeroId"] + "-") for c in other_view["OwnCards"]))
            for _ in range(30):
                view = self.view()
                if view["Phase"] == "InitiativeChoice":
                    chooser = view["Pending"]["ChooserSeat"]
                    own = self.view(chooser)
                    self.command(chooser, "ChooseInitiative", TargetSeat=own["Pending"]["CandidateSeats"][0])
                elif view["Phase"] == "Action":
                    self.command(view["ActiveSeat"], "Pass")
                else:
                    break
            self.check(f"complete turn {turn}", self.view()["Phase"] == ("RoundEnd" if turn == 4 else "Planning"))
        self.command(0, "ResolveRoundEnd")
        self.check("one full round returned to planning", self.view()["Round"] == 2 and self.view()["Turn"] == 1)
        public = lambda v: {k: v[k] for k in ["MatchId", "Revision", "Round", "Turn", "Phase", "Players", "Units", "BlueCrystal", "RedCrystal"]}
        self.check("four clients agree public state", all(public(self.view(i)) == public(self.view()) for i in range(4)))
        self.report["max_response_bytes"] = max(self.rpc(i, "metrics")["max_snapshot_bytes"] for i in range(4))

    def finish(self):
        for process in self.clients:
            process.stdin.close()
            try:
                process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait()
        self.server.stdin.write("stop\n")
        self.server.stdin.flush()
        try:
            self.server.wait(timeout=30)
        except subprocess.TimeoutExpired:
            self.server.kill()
            self.server.wait()
        self.report["server_exit"] = self.server.returncode
        self.report["restore_verified"] = (self.output / "private/restore-check.json").exists()
        for log in self.logs:
            log.close()
        (self.output / "report.json").write_text(json.dumps(self.report, indent=2, ensure_ascii=False), encoding="utf-8")
        print(self.output)


if __name__ == "__main__":
    run = Run()
    try:
        run.boundaries()
        run.full_round()
        run.report["passed"] = True
    except Exception:
        run.report["passed"] = False
        run.report["failure"] = traceback.format_exc()
        raise
    finally:
        run.finish()
