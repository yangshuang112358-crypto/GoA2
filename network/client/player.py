"""NET-01 independent TCP client; no rule engine or full game state."""
import json
import socket
import struct
import threading
import time
import uuid


class Player:
    def __init__(self, ticket, log=None):
        self.ticket = ticket
        self.log = log
        self.view = None
        self.seat = None
        self.state = "Disconnected"
        self.generation = 0
        self.epoch = 0
        self.sock = None
        self.pending = {}
        self.results = {}
        self.errors = []
        self.changed = threading.Condition()
        self.send_lock = threading.Lock()
        self.max_snapshot_bytes = 0

    def record(self, direction, message):
        if self.log:
            self.log.write(json.dumps({"direction": direction, "type": message.get("Type"),
                "id": message.get("CommandId"), "code": message.get("Code"),
                "revision": message.get("Snapshot", {}).get("Revision")}) + "\n")
            self.log.flush()

    def connect(self):
        self.disconnect()
        with self.changed:
            self.epoch += 1
            epoch = self.epoch
            self.state = "Connecting"
        sock = socket.create_connection((self.ticket["Host"], self.ticket["Port"]), timeout=10)
        sock.settimeout(None)
        self.sock = sock
        hello = {"Type": "Resume" if self.view else "Hello", "RoomId": self.ticket["RoomId"],
                 "Credential": self.ticket["Credential"], **self.ticket["Capabilities"]}
        if self.view:
            hello["LastRevision"] = self.view["Revision"]
        self.send(hello)
        threading.Thread(target=self.read_loop, args=(sock, epoch), daemon=True).start()
        self.wait(lambda: self.state != "Connecting")
        if self.state != "Connected":
            raise RuntimeError("authentication failed")

    def disconnect(self):
        with self.changed:
            self.epoch += 1
            self.state = "Disconnected"
            if self.sock:
                try:
                    self.sock.shutdown(socket.SHUT_RDWR)
                except OSError:
                    pass
                self.sock.close()
                self.sock = None
            self.changed.notify_all()

    def send(self, message):
        data = json.dumps(message, ensure_ascii=False, separators=(",", ":")).encode()
        with self.send_lock:
            if not self.sock:
                raise RuntimeError("disconnected")
            self.sock.sendall(struct.pack("!I", len(data)) + data)
        self.record("send", message)

    @staticmethod
    def exact(sock, n):
        data = bytearray()
        while len(data) < n:
            part = sock.recv(n - len(data))
            if not part:
                raise EOFError()
            data.extend(part)
        return bytes(data)

    def read_loop(self, sock, epoch):
        try:
            while True:
                size = struct.unpack("!I", self.exact(sock, 4))[0]
                if not 0 < size <= 8 * 1024 * 1024:
                    raise ValueError("response size")
                message = json.loads(self.exact(sock, size))
                self.max_snapshot_bytes = max(size, self.max_snapshot_bytes)
                with self.changed:
                    if epoch != self.epoch:
                        return
                    self.accept(message)
                    self.changed.notify_all()
        except (OSError, EOFError, ValueError):
            with self.changed:
                if epoch == self.epoch:
                    self.state = "Disconnected"
                    self.changed.notify_all()

    def accept(self, message):
        self.record("receive", message)
        kind = message["Type"]
        if kind == "Welcome":
            if self.seat is not None and self.seat != message["Seat"]:
                raise ValueError("identity changed")
            self.seat = message["Seat"]
            self.generation = message["Generation"]
            self.state = "Connected"
        elif "Generation" in message and message["Generation"] != self.generation:
            return
        view = message.get("Snapshot")
        if view and (self.view is None or view["MatchId"] == self.view["MatchId"] and view["Revision"] > self.view["Revision"]):
            self.view = view
        if kind == "Result":
            self.results[message["CommandId"]] = message
            self.pending.pop(message["CommandId"], None)
        elif kind == "Error":
            self.errors.append(message["Code"])

    def wait(self, predicate, seconds=15):
        deadline = time.monotonic() + seconds
        with self.changed:
            while not predicate():
                remaining = deadline - time.monotonic()
                if remaining <= 0:
                    raise TimeoutError("client wait timed out")
                self.changed.wait(remaining)

    def submit(self, kind, args=None, command_id=None, revision=None, match=None):
        with self.changed:
            if self.state != "Connected":
                raise RuntimeError("disconnected: submission disabled")
            message = {"Type": "Intent", "CommandId": command_id or uuid.uuid4().hex,
                       "MatchId": match or self.view["MatchId"],
                       "ExpectedRevision": self.view["Revision"] if revision is None else revision,
                       "Kind": kind, **(args or {})}
            identifier = message["CommandId"]
            if identifier in self.pending and self.pending[identifier] != message:
                raise ValueError("cannot change pending intent")
            self.pending[identifier] = message
            self.results.pop(identifier, None)
        self.send(message)
        self.wait(lambda: identifier in self.results or self.state == "Disconnected")
        return self.results.get(identifier, {"Type": "Uncertain", "CommandId": identifier})

    def retry(self, identifier):
        if self.state != "Connected":
            raise RuntimeError("disconnected")
        message = self.pending[identifier]
        self.send(message)
        self.wait(lambda: identifier in self.results or self.state == "Disconnected")
        return self.results.get(identifier, {"Type": "Uncertain", "CommandId": identifier})


def rpc_main():
    import sys
    with open(sys.argv[1], encoding="utf-8-sig") as f:
        ticket = json.load(f)
    with open(sys.argv[2], "x", encoding="utf-8") as log:
        player = Player(ticket, log)
        for line in sys.stdin:
            try:
                request = json.loads(line)
                op = request.pop("op")
                if op == "connect":
                    player.connect()
                    result = {"seat": player.seat, "generation": player.generation}
                elif op == "view":
                    player.wait(lambda: player.view is not None and player.view["Revision"] >= request.get("revision", 0))
                    result = player.view
                elif op == "submit":
                    result = player.submit(**request)
                elif op == "disconnect":
                    player.disconnect()
                    result = {"state": player.state}
                elif op == "metrics":
                    result = {"max_snapshot_bytes": player.max_snapshot_bytes}
                else:
                    raise ValueError("unknown RPC operation")
                print(json.dumps({"ok": True, "result": result}, ensure_ascii=False), flush=True)
            except Exception as error:
                print(json.dumps({"ok": False, "error": str(error)}), flush=True)
        player.disconnect()


if __name__ == "__main__":
    rpc_main()
