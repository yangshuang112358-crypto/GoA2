"""Application-owned room envelope around authoritative engine state."""

from __future__ import annotations

from dataclasses import dataclass, replace
from threading import RLock
from typing import Any, Mapping


EVENT_LOG_LIMIT = 100
SNAPSHOT_HISTORY_LIMIT = 10


@dataclass(frozen=True, slots=True)
class RoomSnapshot:
    room_id: str
    revision: int
    controlled_seat: int
    state: Any
    seat_token_hashes: tuple[str | None, ...] = (None, None, None, None)
    events: tuple[dict[str, Any], ...] = ()
    history: tuple[dict[str, Any], ...] = ()

    def to_dict(self) -> dict[str, Any]:
        to_dict = getattr(self.state, "to_dict", None)
        if not callable(to_dict):
            raise TypeError("room state must expose to_dict()")
        return {
            "room_id": self.room_id,
            "revision": self.revision,
            "controlled_seat": self.controlled_seat,
            "state": to_dict(),
            "seat_token_hashes": list(self.seat_token_hashes),
            "events": [dict(event) for event in self.events],
            "history": [dict(snapshot) for snapshot in self.history],
        }

    @classmethod
    def from_dict(cls, value: Mapping[str, Any]) -> "RoomSnapshot":
        from goa2.domain import RoomState

        required = {
            "room_id",
            "revision",
            "controlled_seat",
            "state",
            "events",
            "history",
        }
        allowed = {*required, "seat_token_hashes"}
        if not required.issubset(value) or not set(value).issubset(allowed):
            raise ValueError("room snapshot has unexpected fields")
        state = RoomState.from_dict(dict(value["state"]))
        seat_token_hashes = _token_hash_tuple(
            value.get("seat_token_hashes", [None, None, None, None])
        )
        snapshot = cls(
            room_id=value["room_id"],
            revision=value["revision"],
            controlled_seat=value["controlled_seat"],
            state=state,
            seat_token_hashes=seat_token_hashes,
            events=_mapping_tuple(value["events"], "events"),
            history=_mapping_tuple(value["history"], "history"),
        )
        if snapshot.revision != state.revision:
            raise ValueError("snapshot revision does not match room state")
        if snapshot.controlled_seat != int(state.controlled_seat):
            raise ValueError("snapshot controlled seat does not match room state")
        return snapshot


def record_transaction(
    previous: RoomSnapshot | None,
    updated: RoomSnapshot,
) -> RoomSnapshot:
    """Attach bounded persistence metadata at the store transaction boundary."""

    source = previous or updated
    sequence = source.events[-1].get("sequence", 0) + 1 if source.events else 1
    event = {
        "sequence": sequence,
        "kind": "room_created" if previous is None else "room_updated",
        "previous_revision": previous.revision if previous is not None else None,
        "revision": updated.revision,
    }
    state_to_dict = getattr(updated.state, "to_dict", None)
    if not callable(state_to_dict):
        raise TypeError("room state must expose to_dict()")
    history_entry = {
        "revision": updated.revision,
        "controlled_seat": updated.controlled_seat,
        "state": state_to_dict(),
    }
    return replace(
        updated,
        events=(*source.events, event)[-EVENT_LOG_LIMIT:],
        history=(*source.history, history_entry)[-SNAPSHOT_HISTORY_LIMIT:],
    )


def _mapping_tuple(value: Any, name: str) -> tuple[dict[str, Any], ...]:
    if not isinstance(value, list) or not all(
        isinstance(item, Mapping) for item in value
    ):
        raise ValueError(f"{name} must be a list of objects")
    return tuple(dict(item) for item in value)


def _token_hash_tuple(value: Any) -> tuple[str | None, ...]:
    if (
        not isinstance(value, list)
        or len(value) != 4
        or any(item is not None and not isinstance(item, str) for item in value)
    ):
        raise ValueError("seat_token_hashes must contain four strings or nulls")
    return tuple(value)


class RoomService:
    """Single-room compatibility facade over the authoritative reducer."""

    def __init__(self) -> None:
        from goa2.domain import create_initial_room_state

        self._state = create_initial_room_state()
        self._lock = RLock()

    def get_state(self) -> dict[str, Any]:
        with self._lock:
            return self._state.to_dict()

    def switch_controlled_seat(
        self, *, seat: int, expected_revision: int
    ) -> dict[str, Any]:
        from goa2.domain import Seat, SwitchControlledSeat
        from goa2.engine import reduce_room

        if isinstance(seat, bool) or not isinstance(seat, int):
            raise ValueError("seat must be an integer")
        command = SwitchControlledSeat(
            expected_revision=expected_revision,
            target_seat=Seat(seat),
        )
        with self._lock:
            self._state = reduce_room(self._state, command).state
            return self._state.to_dict()

    def reset(self, *, expected_revision: int) -> dict[str, Any]:
        from goa2.domain import ResetRoom
        from goa2.engine import reduce_room

        command = ResetRoom(expected_revision=expected_revision)
        with self._lock:
            self._state = reduce_room(self._state, command).state
            return self._state.to_dict()
