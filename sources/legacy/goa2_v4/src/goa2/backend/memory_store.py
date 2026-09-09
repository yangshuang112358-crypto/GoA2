"""Thread-safe in-memory room storage."""

from __future__ import annotations

from threading import RLock

from goa2.application.ports import RoomMutation
from goa2.application.room import RoomSnapshot, record_transaction


class InMemoryRoomStore:
    def __init__(self) -> None:
        self._rooms: dict[str, RoomSnapshot] = {}
        self._lock = RLock()

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
            self._rooms[room_id] = updated
            return updated
