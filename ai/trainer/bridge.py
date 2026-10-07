"""Local JSON-lines transport. No card rules or authority-state parsing live here."""
import json
import queue
import subprocess
import threading
from pathlib import Path


class Bridge:
    def __init__(self, root, dotnet, audit, timeout=90):
        self.timeout = timeout
        self.messages = queue.Queue()
        self.stderr = open(str(audit) + "-stderr.log", "x", encoding="utf-8")
        self.process = subprocess.Popen(
            [str(dotnet), str(Path(root) / "ai/Goa2.Ai.Cli/bin/Release/net10.0/Goa2.Ai.Cli.dll"),
             "serve", str(root), str(audit)], stdin=subprocess.PIPE, stdout=subprocess.PIPE,
            stderr=self.stderr, text=True, encoding="utf-8", bufsize=1,
            creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
        def read():
            for line in self.process.stdout:
                self.messages.put(line)
            self.messages.put(None)
        self.reader = threading.Thread(target=read, daemon=True)
        self.reader.start()

    def call(self, **request):
        self.process.stdin.write(json.dumps(request, separators=(",", ":")) + "\n")
        self.process.stdin.flush()
        try:
            line = self.messages.get(timeout=self.timeout)
        except queue.Empty:
            raise TimeoutError("C# environment response timed out; no fallback action submitted")
        if line is None:
            raise RuntimeError(f"C# environment exited: {self.process.poll()}")
        value = json.loads(line)
        if value.get("Protocol") != 1 or "Error" in value:
            raise RuntimeError(f"Environment protocol failure: {value}")
        return value

    def close(self):
        if self.process.poll() is None:
            try:
                self.process.stdin.write('{"op":"close"}\n')
                self.process.stdin.flush()
                self.process.wait(timeout=self.timeout)
            except (BrokenPipeError, subprocess.TimeoutExpired):
                self.process.kill()  # Only this child. Host streams flush each accepted decision.
                self.process.wait()
        self.process.stdin.close()
        self.process.stdout.close()
        self.stderr.close()

    def __enter__(self):
        return self

    def __exit__(self, *args):
        self.close()
