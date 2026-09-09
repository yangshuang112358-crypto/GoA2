"""Atomic JSON-backed room storage for the local server."""

from __future__ import annotations

import json
import os
from pathlib import Path
from tempfile import NamedTemporaryFile
from threading import RLock
from time import time_ns
from typing import Any

from goa2.application.ports import RoomMutation
from goa2.application.room import RoomSnapshot, record_transaction


_SCHEMA_VERSION = 1


class JsonRoomStore:
    def __init__(self, path: Path) -> None:
        self.path = Path(path)
        self._lock = RLock()
        self._rooms = self._load()

    def read(self, room_id: str) -> RoomSnapshot | None:
        with self._lock:
            return self._rooms.get(room_id)

    def transact(self, room_id: str, mutation: RoomMutation) -> RoomSnapshot:
        with self._lock:
            previous = self._rooms.get(room_id)
            updated = mutation(previous)
            if updated.room_id != room_id:
                raise ValueError("room mutation changed room_id")
            updated = record_transaction(previous, updated)
            rooms = {**self._rooms, room_id: updated}
            self._write(rooms)
            self._rooms = rooms
            return updated

    def _load(self) -> dict[str, RoomSnapshot]:
        if not self.path.exists():
            return {}
        try:
            payload = json.loads(self.path.read_text(encoding="utf-8"))
            if not isinstance(payload, dict) or set(payload) != {
                "schema_version",
                "rooms",
            }:
                raise ValueError("room store has unexpected fields")
            if payload["schema_version"] != _SCHEMA_VERSION:
                raise ValueError("unsupported room store schema")
            raw_rooms = payload["rooms"]
            if not isinstance(raw_rooms, dict):
                raise ValueError("rooms must be an object")
            rooms = {
                room_id: RoomSnapshot.from_dict(snapshot)
                for room_id, snapshot in raw_rooms.items()
            }
            if any(room.room_id != room_id for room_id, room in rooms.items()):
                raise ValueError("stored room id does not match its key")
            return rooms
        except (OSError, TypeError, ValueError, json.JSONDecodeError):
            self._quarantine()
            return {}

    def _write(self, rooms: dict[str, RoomSnapshot]) -> None:
        self.path.parent.mkdir(parents=True, exist_ok=True)
        payload: dict[str, Any] = {
            "schema_version": _SCHEMA_VERSION,
            "rooms": {
                room_id: snapshot.to_dict()
                for room_id, snapshot in sorted(rooms.items())
            },
        }
        temporary_path: Path | None = None
        try:
            with NamedTemporaryFile(
                "w",
                encoding="utf-8",
                dir=self.path.parent,
                prefix=f".{self.path.name}.",
                suffix=".tmp",
                delete=False,
            ) as temporary:
                temporary_path = Path(temporary.name)
                json.dump(
                    payload,
                    temporary,
                    ensure_ascii=False,
                    indent=2,
                    sort_keys=True,
                )
                temporary.write("\n")
                temporary.flush()
                os.fsync(temporary.fileno())
            os.replace(temporary_path, self.path)
        finally:
            if temporary_path is not None and temporary_path.exists():
                temporary_path.unlink()

    def _quarantine(self) -> None:
        quarantine = self.path.with_name(
            f"{self.path.name}.corrupt-{time_ns()}"
        )
        try:
            os.replace(self.path, quarantine)
        except OSError:
            pass
