"""Faults only this loopback test connection; no firewall or system network changes."""
import json
import socket
import struct
import threading

from player import Player


class FaultProxy:
    def __init__(self, ticket, mode):
        self.ticket = ticket
        self.mode = mode
        self.listener = socket.socket()
        self.listener.bind(("127.0.0.1", 0))
        self.listener.listen(1)
        self.port = self.listener.getsockname()[1]
        self.sockets = []
        self.failed = None
        self.closed = False
        self.worker = threading.Thread(target=self.run, daemon=True)
        self.worker.start()

    @staticmethod
    def frame(sock):
        size = struct.unpack("!I", Player.exact(sock, 4))[0]
        if not 0 < size <= 8 * 1024 * 1024:
            raise ValueError("invalid test frame")
        return Player.exact(sock, size)

    @staticmethod
    def send(sock, body):
        sock.sendall(struct.pack("!I", len(body)) + body)

    def run(self):
        while not self.closed:
            self.serve_once()

    def serve_once(self):
        try:
            client, _ = self.listener.accept()
            server = socket.create_connection((self.ticket["Host"], self.ticket["Port"]), timeout=5)
            server.settimeout(None)
            self.sockets = [client, server]
            def upstream():
                try:
                    while True:
                        self.send(server, self.frame(client))
                except (OSError, EOFError):
                    try:
                        server.shutdown(socket.SHUT_RDWR)
                    except OSError:
                        pass
            threading.Thread(target=upstream, daemon=True).start()
            delayed = None
            old_snapshot = None
            while True:
                body = self.frame(server)
                message = json.loads(body)
                if message["Type"] == "Result" and self.mode != "pass":
                    if self.mode == "drop":
                        self.mode = "pass"
                        break
                    delayed = (body, message["Snapshot"]["Revision"])
                    continue
                self.send(client, body)
                if delayed and message["Type"] == "Snapshot":
                    if message["Snapshot"]["Revision"] <= delayed[1]:
                        old_snapshot = body
                    else:
                        self.send(client, delayed[0])
                        if old_snapshot:
                            self.send(client, old_snapshot)
                        delayed = None
                        self.mode = "pass"
        except (OSError, EOFError):
            pass
        except Exception as error:
            self.failed = str(error)
        finally:
            self.close_sockets()

    def close_sockets(self):
        for sock in self.sockets:
            try:
                sock.shutdown(socket.SHUT_RDWR)
            except OSError:
                pass
            sock.close()

    def close(self):
        self.closed = True
        self.close_sockets()
        self.listener.close()
