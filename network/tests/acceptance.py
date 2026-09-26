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
from concurrent.futures import ThreadPoolExecutor
import threading

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "network/client"))
from player import Player
from fault_proxy import FaultProxy


class Run:
    def __init__(self, fixture=None, steps=0, csharp=False, faults=False):
        self.output = ROOT / "artifacts/network" / (time.strftime("%Y%m%d-%H%M%S-") + uuid.uuid4().hex[:8])
        self.output.mkdir(parents=True)
        self.checks = []
        self.clients = []
        self.logs = []
        self.proxies = []
        self.revision = 0
        dotnet = Path(os.environ["LOCALAPPDATA"]) / "Goa2V1Toolchain/dotnet/dotnet.exe"
        assembly = ROOT / "network/Goa2.Network/bin/Release/net10.0/Goa2.Network.dll"
        self.report = {"source_commit": subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip(),
                       "runner": "TCP: one .NET service and four Python client processes", "ui_tested": False,
                       "core_tests_counted": 0, "checks": self.checks, "assemblies": {
                           p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in assembly.parent.glob("*.dll")}}
        self.report["source_files"] = {str(p.relative_to(ROOT)).replace("\\", "/"): hashlib.sha256(p.read_bytes()).hexdigest()
            for folder in [ROOT / "network", ROOT / "core/com.goa2.core/Runtime"] for p in folder.rglob("*")
            if p.is_file() and p.suffix in [".cs", ".py", ".csproj", ".json"] and not any(
                part in ["bin", "obj", "__pycache__", "evidence"] for part in p.parts)}
        self.report["started_utc"] = time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())
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
        self.report["csharp_adapter"] = csharp
        for _ in range(200):
            if (self.output / "private/ready.json").exists():
                break
            if self.server.poll() is not None:
                raise RuntimeError("server failed; inspect server.log")
            time.sleep(.05)
        for seat in range(4):
            err = open(self.output / f"client-{seat}.stderr", "x", encoding="utf-8")
            self.logs.append(err)
            ticket_path = self.output / f"private/seat-{seat}.private.json"
            if faults and seat in (0, 1):
                ticket = json.loads(ticket_path.read_text())
                proxy = FaultProxy(ticket, "drop" if seat == 0 else "delay")
                self.proxies.append(proxy)
                ticket_path = self.output / f"private/proxy-{seat}.private.json"
                ticket_path.write_text(json.dumps({**ticket, "Port": proxy.port}), encoding="utf-8")
            client_command = [sys.executable, "-u", str(ROOT / "network/client/player.py")]
            if csharp:
                client_command = [str(dotnet), str(ROOT / "network/tests/Goa2.Network.ClientHarness/bin/Release/net10.0/Goa2.Network.ClientHarness.dll")]
            self.clients.append(subprocess.Popen([*client_command,
                str(ticket_path), str(self.output / f"client-{seat}.jsonl")],
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
                     {**base, "Kind": "Move", "MoveMode": 1}, {**base, "Value": "x" * 257},
                     {k: ("ChooseEffectMove" if k == "Kind" else v) for k, v in base.items() if k != "Value"}]
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
        self.check("N11 old connection invalidated by service", self.rpc(1, "state", wait_disconnected=True)["state"] == "Disconnected")
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

    def connect_all(self):
        for seat in range(4):
            self.rpc(seat, "connect")
        self.revision = self.view()["Revision"]

    def setup_heroes(self):
        for seat, hero in enumerate(["wasp", "shargatha", "brogan", "arien"]):
            self.command(seat, "ChooseHero", Value=hero)

    def adapter_pending(self):
        self.connect_all()
        self.reconnect_pending(0, "optional_discard", "OptionalDiscardCards")
        self.command(0, "ChooseOptionalDiscard", Value="brogan-00-猛攻")
        self.command(0, "ChooseAttackTarget", Value="hero:1")
        self.reconnect_pending(1, "defense", "DefenseOptions")
        self.command(1, "Defend", Value="wasp-10-反射屏障")
        self.reconnect_pending(0, "forced_discard", "ForcedDiscardCards")
        self.command(0, "ForcedDiscard", Value="brogan-06-铜墙铁壁")
        self.check("C# adapter resumes after defense and discard", self.view()["ActiveSeat"] == 3)

    def adapter_faults(self):
        self.connect_all()
        result = self.rpc(0, "submit", kind="ChooseHero", args={"Value": "wasp"})
        self.check("C# dropped reply uncertain", result["Type"] == "Uncertain")
        self.rpc(0, "connect")
        retried = self.rpc(0, "retry", command_id=result["CommandId"])
        self.check("C# reconnect original-ID retry", retried["Duplicate"] and retried["CommandId"] == result["CommandId"])
        self.revision = 1
        with ThreadPoolExecutor(1) as pool:
            future = pool.submit(self.rpc, 1, "submit", kind="ChooseHero", args={"Value": "brogan"})
            self.rpc(2, "view", revision=2)
            self.revision = 2
            self.command(2, "ChooseHero", Value="arien")
            response = future.result(timeout=10)
        self.check("C# delayed result resolves without rollback", response["Accepted"] and self.view(1)["Revision"] == 3)
        result = self.rpc(0, "submit", kind="ChooseHero", args={})
        self.check("C# malformed intent returns correlated rejection", result["Code"] == "missing_field" and not result["Accepted"])

    def reconnect_pending(self, seat, kind, candidates):
        before = self.view(seat)
        self.check(kind + " pending before disconnect", before["Pending"]["Kind"] == kind)
        self.rpc(seat, "disconnect")
        self.check("N13 disconnected state " + kind, self.rpc(seat, "state")["state"] == "Disconnected")
        self.rpc(seat, "connect")
        after = self.view(seat)
        self.check("resume " + kind, after["Pending"] == before["Pending"] and after[candidates] == before[candidates] and bool(after[candidates]))
        for other in range(4):
            if other != seat:
                self.check("private candidates " + kind + str(other), not self.view(other)[candidates])

    def lost_result(self, seat, kind, **args):
        # Send through a real socket then close without reading any Result. Another
        # authenticated client observes the commit before original ID is retried.
        ticket = self.ticket(seat)
        self.rpc(seat, "disconnect")
        sock = socket.create_connection((ticket["Host"], ticket["Port"]), timeout=5)
        hello = {"Type": "Resume", "RoomId": ticket["RoomId"], "Credential": ticket["Credential"], **ticket["Capabilities"]}
        data = json.dumps(hello).encode()
        sock.sendall(struct.pack("!I", len(data)) + data)
        size = struct.unpack("!I", Player.exact(sock, 4))[0]
        welcome = json.loads(Player.exact(sock, size))
        original = welcome["Snapshot"]["Revision"]
        identifier = uuid.uuid4().hex
        command = {"Type": "Intent", "CommandId": identifier, "MatchId": welcome["RoomId"],
                   "ExpectedRevision": original, "Kind": kind, **args}
        data = json.dumps(command, ensure_ascii=False).encode()
        sock.sendall(struct.pack("!I", len(data)) + data)
        # No receiver runs on this socket. Wait for another player's public snapshot.
        observed = self.rpc((seat + 1) % 4, "view", revision=original + 1)
        sock.close()
        self.revision = observed["Revision"]
        self.rpc(seat, "connect")
        before_retry = self.view(seat)
        result = self.rpc(seat, "submit", kind=kind, args=args, command_id=identifier, revision=original)
        self.check("N05 lost Result retry " + kind, result["Duplicate"] and result["Snapshot"] == before_retry)
        return result

    def pending_axe(self):
        self.connect_all()
        self.reconnect_pending(0, "optional_discard", "OptionalDiscardCards")
        self.command(1, "ChooseOptionalDiscard", expected="invalid_optional_discard", Value="skip")
        self.lost_result(0, "ChooseOptionalDiscard", Value="brogan-00-猛攻")
        self.command(0, "ChooseAttackTarget", Value="hero:1")
        self.reconnect_pending(1, "defense", "DefenseOptions")
        self.command(0, "Defend", expected="invalid_defender", Value="wasp-10-反射屏障")
        self.command(1, "Defend", Value="wasp-10-反射屏障")
        self.reconnect_pending(0, "forced_discard", "ForcedDiscardCards")
        self.check("N08 optional cost preserved", "brogan-00-猛攻" not in self.view(0)["ForcedDiscardCards"])
        self.lost_result(0, "ForcedDiscard", Value="brogan-06-铜墙铁壁")
        view = self.view(0)
        self.check("N08 two distinct discards once", sum(c["Zone"] == "Discarded" for c in view["OwnCards"]) == 2)
        self.check("N08 parent resumes", view["Phase"] == "Action" and view["ActiveSeat"] == 3)

    def pending_respawn(self):
        self.connect_all()
        self.reconnect_pending(1, "hero_respawn", "RespawnCells")
        self.command(0, "RespawnHero", expected="invalid_respawn", Destination=self.view(1)["RespawnCells"][0])
        self.lost_result(1, "RespawnHero", Destination=self.view(1)["RespawnCells"][0])
        self.check("N10 respawn resumes parent", self.view()["Phase"] == "Action" and self.view()["ActiveSeat"] == 1)

    def pending_spawn(self):
        self.connect_all()
        seat = self.view()["Pending"]["ChooserSeat"]
        self.reconnect_pending(seat, "minion_spawn", "SpawnChoices")
        unit, cells = next(iter(self.view(seat)["SpawnChoices"].items()))
        self.lost_result(seat, "ChooseMinionSpawn", Value=unit, Destination=cells[0])
        self.check("N10 spawn resumes round end", self.view()["RoundEndStage"] == "upgrades")

    def pending_upgrade(self):
        self.connect_all()
        before = self.view(0)["UpgradeOptions"]
        self.rpc(0, "disconnect")
        options = self.view(1)["UpgradeOptions"]
        self.check("N09 both players have own upgrades", bool(before) and bool(options) and before != options)
        # CardId is supplied by the existing rule projection.
        self.command(1, "ChooseUpgrade", Value=options[0]["CardId"])
        self.check("N09 finished chooser clears options", not self.view(1)["UpgradeOptions"])
        self.rpc(0, "connect")
        self.check("N09 offline chooser recovers options", self.view(0)["UpgradeOptions"] == before)
        while self.view(0)["UpgradeOptions"]:
            self.command(0, "ChooseUpgrade", Value=self.view(0)["UpgradeOptions"][0]["CardId"])
        self.check("N09 resumes next round", self.view()["Round"] == 2)

    def pending_skip(self):
        self.connect_all()
        self.reconnect_pending(0, "optional_discard", "OptionalDiscardCards")
        before = self.view(0)
        self.lost_result(0, "ChooseOptionalDiscard", Value="skip")
        after = self.view(0)
        self.check("N08 optional skip preserves cards", after["OwnCards"] != [] and
                   sum(c["Zone"] == "Discarded" for c in before["OwnCards"]) == sum(c["Zone"] == "Discarded" for c in after["OwnCards"]))
        self.check("N08 no target after skip resumes", after["Phase"] == "Action" and after["ActiveSeat"] == 3)

    def race_and_generation(self):
        self.connect_all()
        players = [Player(self.ticket(i)) for i in range(2)]
        for player in players:
            player.connect()
        barrier = threading.Barrier(2)
        def send(index):
            barrier.wait(timeout=5)
            return players[index].submit("ChooseHero", {"Value": ["wasp", "brogan"][index]}, revision=0)
        with ThreadPoolExecutor(2) as pool:
            results = list(pool.map(send, range(2)))
        self.check("N04 simultaneous same revision has one winner", sorted(r["Code"] for r in results) == ["ok", "stale_revision"])
        self.revision = 1
        # Retain the superseded socket reference and attempt an actual stale write.
        old_socket = players[0].sock
        replacement = Player(self.ticket(0))
        replacement.connect()
        players[0].wait(lambda: players[0].state == "Disconnected")
        payload = json.dumps({"Type": "Intent", "CommandId": "old-generation", "ExpectedRevision": 1,
                              "MatchId": replacement.view["MatchId"], "Kind": "ChooseHero", "Value": "arien"}).encode()
        try:
            old_socket.sendall(struct.pack("!I", len(payload)) + payload)
        except OSError:
            pass
        # Barrier through a fresh authenticated connection observes the same revision.
        self.rpc(0, "connect")
        self.check("N11 old socket cannot commit after replacement", self.view()["Revision"] == 1)
        for player in players + [replacement]:
            player.disconnect()

    def gold_skip_syntax(self):
        self.connect_all()
        # In HeroSelection this must reach rules and reject by rule code, rather than
        # transport rejecting the core's documented -1 sentinel for choosing zero gold.
        self.command(0, "ChooseGoldTransfer", expected="invalid_gold_transfer", TargetSeat=-1, Value="0")
        self.check("gold skip sentinel reaches authority", self.view()["Revision"] == self.revision)

    def faults(self):
        self.connect_all()
        ticket = self.ticket(0)
        proxy = FaultProxy(ticket, "drop")
        player = Player({**ticket, "Port": proxy.port})
        try:
            player.connect()
            result = player.submit("ChooseHero", {"Value": "wasp"})
            identifier = result["CommandId"]
            self.check("N05 lost result remains uncertain and pending", result["Type"] == "Uncertain" and identifier in player.pending)
            player.ticket = ticket
            player.connect()
            before = player.view
            result = player.retry(identifier)
            self.check("N05 reconnect retry preserves ID and revision", result["Duplicate"] and player.view == before and not player.pending)
            self.revision = player.view["Revision"]
        finally:
            player.disconnect()
            proxy.close()
            proxy.worker.join(3)
        ticket = self.ticket(1)
        proxy = FaultProxy(ticket, "delay")
        player = Player({**ticket, "Port": proxy.port})
        try:
            player.connect()
            with ThreadPoolExecutor(1) as pool:
                future = pool.submit(player.submit, "ChooseHero", {"Value": "brogan"})
                player.wait(lambda: player.view["Revision"] == 2)
                self.revision = 2
                self.command(2, "ChooseHero", Value="arien")
                result = future.result(timeout=10)
            self.check("N12 delayed old Result acknowledged without rollback", result["Snapshot"]["Revision"] == 2 and player.view["Revision"] == 3)
            self.check("N12 delayed old Snapshot cannot rewind", player.view["Players"][2]["HeroId"] == "arien")
            self.check("fault proxy succeeded", proxy.failed is None)
        finally:
            player.disconnect()
            proxy.close()
            proxy.worker.join(3)

    def finish(self):
        if all(p.poll() is None for p in self.clients):
            self.report["max_response_bytes"] = max(self.rpc(i, "metrics")["max_snapshot_bytes"] for i in range(4))
        for process in self.clients:
            process.stdin.close()
            try:
                process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait()
        for proxy in self.proxies:
            proxy.close()
            proxy.worker.join(3)
        self.server.stdin.write("stop\n")
        self.server.stdin.flush()
        try:
            self.server.wait(timeout=30)
        except subprocess.TimeoutExpired:
            self.server.kill()
            self.server.wait()
        self.report["server_exit"] = self.server.returncode
        self.report["restore_verified"] = (self.output / "private/restore-check.json").exists()
        self.report["finished_utc"] = time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())
        for log in self.logs:
            log.close()
        (self.output / "report.json").write_text(json.dumps(self.report, indent=2, ensure_ascii=False), encoding="utf-8")
        print(self.output)


if __name__ == "__main__":
    cases = [(None, 0, ["boundaries", "full_round"]), (None, 0, ["race_and_generation", "gold_skip_syntax"]),
             (None, 0, ["faults"])]
    if "--pending" in sys.argv:
        cases = [("tests/scenarios/throwing-axe-reflection.json", 11, ["pending_axe"]),
                 ("tests/scenarios/combat-defense.json", 12, ["pending_respawn"]),
                 ("tests/scenarios/round-frontline.json", 13, ["pending_spawn"]),
                 ("tests/scenarios/round-upgrades.json", 5, ["pending_upgrade"]),
                 ("tests/scenarios/throwing-axe-skip.json", 11, ["pending_skip"])]
    if "--csharp" in sys.argv:
        cases = [(None, 0, ["connect_all", "setup_heroes", "full_round"]),
                 ("tests/scenarios/throwing-axe-reflection.json", 11, ["adapter_pending"]),
                 (None, 0, ["adapter_faults"])]
    for fixture, steps, methods in cases:
        run = Run(fixture, steps, csharp="--csharp" in sys.argv, faults="adapter_faults" in methods)
        try:
            for method in methods:
                getattr(run, method)()
            run.report["passed"] = True
        except Exception:
            run.report["passed"] = False
            run.report["failure"] = traceback.format_exc()
            raise
        finally:
            run.finish()
