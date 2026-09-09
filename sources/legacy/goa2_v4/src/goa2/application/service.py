"""Room use cases and public/private response projection."""

from __future__ import annotations

from dataclasses import asdict, is_dataclass
from enum import Enum
from hashlib import sha256
from hmac import compare_digest
from secrets import token_urlsafe
from typing import Any, Callable, Mapping

from .catalog import CatalogBundle
from .ports import EnginePort, RoomStore
from .room import RoomSnapshot


class RevisionConflict(ValueError):
    pass


class RoomApplication:
    def __init__(
        self,
        catalog: CatalogBundle,
        engine: EnginePort,
        rooms: RoomStore,
    ) -> None:
        self._catalog = catalog
        self._engine = engine
        self._rooms = rooms

    def catalog(self) -> dict[str, Any]:
        return self._catalog.to_dto()

    def join(
        self,
        seat: int,
        room_id: str = "default",
        token: str | None = None,
    ) -> dict[str, Any]:
        self._validate_seat(seat)
        if token is not None and not isinstance(token, str):
            raise ValueError("reconnect token is invalid")
        generated_token = token_urlsafe(24) if token is None else None

        def claim(room: RoomSnapshot | None) -> RoomSnapshot:
            current = room or self._new_room(room_id)
            stored_hash = current.seat_token_hashes[seat]
            if token is not None:
                if stored_hash is None or not compare_digest(
                    stored_hash, _token_hash(token)
                ):
                    raise ValueError("reconnect token is invalid")
                return current
            if stored_hash is not None:
                raise ValueError("seat is already claimed")
            hashes = list(current.seat_token_hashes)
            hashes[seat] = _token_hash(generated_token)
            return RoomSnapshot(
                room_id=current.room_id,
                revision=current.revision,
                controlled_seat=current.controlled_seat,
                state=current.state,
                seat_token_hashes=tuple(hashes),
                events=current.events,
                history=current.history,
            )

        room = self._rooms.transact(room_id, claim)
        player_token = token if token is not None else generated_token
        return {
            "room_id": room_id,
            "seat": seat,
            "token": player_token,
            "state": self._project(room, seat),
        }

    def actor_seat(
        self,
        room_id: str,
        token: Any,
        legacy_seat: Any = None,
    ) -> int:
        room = self._get_or_create(room_id)
        has_claims = any(room.seat_token_hashes)
        if isinstance(token, str):
            candidate = _token_hash(token)
            for seat, stored_hash in enumerate(room.seat_token_hashes):
                if stored_hash is not None and compare_digest(stored_hash, candidate):
                    return seat
        if has_claims:
            raise ValueError("a valid player token is required")
        self._validate_seat(legacy_seat)
        return legacy_seat

    def authorize_room_tool(self, room_id: str, token: Any) -> None:
        room = self._get_or_create(room_id)
        if not any(room.seat_token_hashes):
            return
        if isinstance(token, str):
            candidate = _token_hash(token)
            if any(
                stored_hash is not None
                and compare_digest(stored_hash, candidate)
                for stored_hash in room.seat_token_hashes
            ):
                return
        raise ValueError("a valid player token is required")

    def state(
        self,
        room_id: str = "default",
        token: str | None = None,
    ) -> dict[str, Any]:
        room = self._get_or_create(room_id)
        private_seat = None
        if token is not None:
            private_seat = self.actor_seat(room_id, token)
        elif not any(room.seat_token_hashes):
            private_seat = room.controlled_seat
        return self._project(room, private_seat)

    def control(
        self,
        seat: int,
        room_id: str = "default",
        expected_revision: int | None = None,
    ) -> dict[str, Any]:
        if isinstance(seat, bool) or not isinstance(seat, int) or not 0 <= seat < 4:
            raise ValueError("seat must be an integer from 0 to 3")

        return self._mutate(
            room_id,
            expected_revision,
            lambda current: self._engine.control(current.state, seat),
        )

    def reset(
        self,
        room_id: str = "default",
        expected_revision: int | None = None,
    ) -> dict[str, Any]:
        return self._mutate(
            room_id,
            expected_revision,
            lambda current: self._engine.reset(current.state),
        )

    def configure_room(
        self,
        starting_crystal_life: Any,
        frontline_victory_marks: Any,
        hero_ids: Any,
        room_id: str = "default",
        expected_revision: int | None = None,
    ) -> dict[str, Any]:
        if not isinstance(hero_ids, (list, tuple)) or len(hero_ids) != 4:
            raise ValueError("hero_ids must contain four heroes")
        selected = tuple(hero_ids)

        return self._mutate(
            room_id,
            expected_revision,
            lambda current: self._engine.configure_room(
                current.state,
                starting_crystal_life=starting_crystal_life,
                frontline_victory_marks=frontline_victory_marks,
                hero_ids=selected,
            ),
        )

    def select_card(
        self,
        seat: int,
        card_id: Any,
        room_id: str = "default",
        expected_revision: int | None = None,
    ) -> dict[str, Any]:
        self._validate_seat(seat)

        def select(current: RoomSnapshot) -> Any:
            self._require_controlled_seat(current, seat)
            seat_state = current.state.seats[seat]
            if card_id not in seat_state.active_card_ids:
                raise ValueError("card is not in the seat's active hand")
            card = self._catalog.card(card_id)
            if card["id"] in {
                *seat_state.used_card_ids,
                *seat_state.discarded_card_ids,
            }:
                raise ValueError("card is unavailable this round")
            return self._engine.select_card(current.state, seat, card["id"])

        return self._mutate(room_id, expected_revision, select)

    def confirm_selection(
        self,
        seat: int,
        room_id: str = "default",
        expected_revision: int | None = None,
    ) -> dict[str, Any]:
        self._validate_seat(seat)

        def confirm(current: RoomSnapshot) -> Any:
            self._require_controlled_seat(current, seat)
            confirmed = self._engine.confirm_selection(current.state, seat)
            if confirmed.phase.value != "card_reveal":
                return confirmed
            return self._reveal_confirmed_state(confirmed)

        return self._mutate(room_id, expected_revision, confirm)

    def reveal_cards(
        self,
        room_id: str = "default",
        expected_revision: int | None = None,
    ) -> dict[str, Any]:
        def reveal(current: RoomSnapshot) -> Any:
            if current.state.phase.value != "card_reveal":
                raise ValueError("cards reveal automatically after all seats confirm")
            return self._reveal_confirmed_state(current.state)

        return self._mutate(room_id, expected_revision, reveal)

    def force_confirm(
        self,
        room_id: str = "default",
        expected_revision: int | None = None,
    ) -> dict[str, Any]:
        current = self._get_or_create(room_id)
        defaults = {}
        for seat in range(4):
            used = set(current.state.seats[seat].used_card_ids)
            defaults[seat] = next(
                card_id
                for card_id in current.state.seats[seat].active_card_ids
                if card_id not in used
            )
        return self._mutate(
            room_id,
            expected_revision,
            lambda current: self._reveal_confirmed_state(
                self._engine.force_confirm(current.state, defaults)
            ),
        )

    def _reveal_confirmed_state(self, state: Any) -> Any:
        selected = self._engine.selected_cards(state)
        if set(selected) != set(range(4)) or any(
            card_id is None for card_id in selected.values()
        ):
            raise ValueError("all four seats must have a selected card")
        if not all(seat.confirmed for seat in state.seats):
            raise ValueError("all four seats must confirm before reveal")
        from goa2.engine import modified_value

        initiatives = {
            card_id: modified_value(
                state.seats[seat],
                "initiative",
                self._catalog.initiative(card_id),
            )
            for seat, card_id in selected.items()
            if card_id is not None
        }
        profiles = {
            card_id: self._catalog.action_profile(card_id)
            for card_id in selected.values()
            if card_id is not None
        }
        return self._engine.reveal_cards(state, initiatives, profiles)

    def skip_action(
        self,
        seat: int,
        room_id: str = "default",
        expected_revision: int | None = None,
    ) -> dict[str, Any]:
        self._validate_seat(seat)

        def skip(current: RoomSnapshot) -> Any:
            self._require_controlled_seat(current, seat)
            return self._engine.skip_action(current.state, seat)

        return self._mutate(room_id, expected_revision, skip)

    def choose_action(
        self,
        seat: int,
        kind: Any,
        source_slot: Any = None,
        room_id: str = "default",
        expected_revision: int | None = None,
    ) -> dict[str, Any]:
        self._validate_seat(seat)
        if not isinstance(kind, str):
            raise ValueError("kind must be a string")
        if source_slot is not None and not isinstance(source_slot, str):
            raise ValueError("source_slot must be a string or null")

        def choose(current: RoomSnapshot) -> Any:
            self._require_controlled_seat(current, seat)
            return self._engine.choose_action(current.state, seat, kind, source_slot)

        return self._mutate(room_id, expected_revision, choose)

    def cancel_action(
        self,
        seat: int,
        room_id: str = "default",
        expected_revision: int | None = None,
    ) -> dict[str, Any]:
        self._validate_seat(seat)

        def cancel(current: RoomSnapshot) -> Any:
            self._require_controlled_seat(current, seat)
            return self._engine.cancel_action(current.state, seat)

        return self._mutate(room_id, expected_revision, cancel)

    def move(
        self,
        seat: int,
        x: Any,
        y: Any,
        room_id: str = "default",
        expected_revision: int | None = None,
    ) -> dict[str, Any]:
        self._validate_seat(seat)
        if any(isinstance(value, bool) or not isinstance(value, int) for value in (x, y)):
            raise ValueError("x and y must be integers")

        def move(current: RoomSnapshot) -> Any:
            self._require_controlled_seat(current, seat)
            return self._engine.move(current.state, seat, x, y)

        return self._mutate(room_id, expected_revision, move)

    def begin_round_end(
        self,
        room_id: str = "default",
        expected_revision: int | None = None,
    ) -> dict[str, Any]:
        return self._mutate(
            room_id,
            expected_revision,
            lambda current: self._engine.begin_round_end(current.state),
        )

    def declare_basic_attack(
        self,
        seat: int,
        target_id: Any,
        room_id: str = "default",
        expected_revision: int | None = None,
    ) -> dict[str, Any]:
        self._validate_seat(seat)
        if not isinstance(target_id, str) or not target_id:
            raise ValueError("target_id must be a non-empty string")

        def declare(current: RoomSnapshot) -> Any:
            self._require_controlled_seat(current, seat)
            return self._engine.declare_basic_attack(
                current.state, seat, target_id
            )

        return self._mutate(room_id, expected_revision, declare)

    def resolve_basic_defense(
        self,
        seat: int,
        card_id: Any,
        room_id: str = "default",
        expected_revision: int | None = None,
    ) -> dict[str, Any]:
        self._validate_seat(seat)
        if card_id is not None and not isinstance(card_id, str):
            raise ValueError("card_id must be a string or null")

        def resolve(current: RoomSnapshot) -> Any:
            self._require_controlled_seat(current, seat)
            if card_id is None:
                profile = {
                    "card_id": None,
                    "defense_value": None,
                    "exclamation": False,
                }
            else:
                if card_id not in current.state.seats[seat].active_card_ids:
                    raise ValueError("card is not in the seat's active hand")
                profile = self._catalog.defense_profile(card_id)
            return self._engine.resolve_basic_defense(
                current.state,
                seat,
                profile["card_id"],
                profile["defense_value"],
                profile["exclamation"],
            )

        return self._mutate(room_id, expected_revision, resolve)

    def resolve_captain_choice(
        self,
        seat: int,
        choice_id: Any,
        candidate_id: Any,
        room_id: str = "default",
        expected_revision: int | None = None,
    ) -> dict[str, Any]:
        self._validate_seat(seat)
        if not isinstance(choice_id, str) or not choice_id:
            raise ValueError("choice_id must be a non-empty string")
        if not isinstance(candidate_id, str) or not candidate_id:
            raise ValueError("candidate_id must be a non-empty string")

        def resolve(current: RoomSnapshot) -> Any:
            self._require_controlled_seat(current, seat)
            return self._engine.resolve_captain_choice(
                current.state, seat, choice_id, candidate_id
            )

        return self._mutate(room_id, expected_revision, resolve)

    def resolve_initiative_choice(
        self,
        seat: int,
        choice_id: Any,
        chosen_seat: Any,
        room_id: str = "default",
        expected_revision: int | None = None,
    ) -> dict[str, Any]:
        self._validate_seat(seat)
        self._validate_seat(chosen_seat)
        if not isinstance(choice_id, str) or not choice_id:
            raise ValueError("choice_id must be a non-empty string")

        def resolve(current: RoomSnapshot) -> Any:
            self._require_controlled_seat(current, seat)
            return self._engine.resolve_initiative_choice(
                current.state, seat, choice_id, chosen_seat
            )

        return self._mutate(room_id, expected_revision, resolve)

    def resolve_respawn(
        self,
        seat: int,
        x: Any,
        y: Any,
        room_id: str = "default",
        expected_revision: int | None = None,
    ) -> dict[str, Any]:
        self._validate_seat(seat)
        if any(isinstance(value, bool) or not isinstance(value, int) for value in (x, y)):
            raise ValueError("respawn coordinates must be integers")

        def resolve(current: RoomSnapshot) -> Any:
            self._require_controlled_seat(current, seat)
            return self._engine.resolve_respawn(current.state, seat, x, y)

        return self._mutate(room_id, expected_revision, resolve)

    def choose_upgrade(
        self,
        seat: int,
        color: Any,
        card_id: Any,
        room_id: str = "default",
        expected_revision: int | None = None,
    ) -> dict[str, Any]:
        self._validate_seat(seat)
        if color not in {"red", "green", "blue"}:
            raise ValueError("color must be red, green, or blue")
        if not isinstance(card_id, str) or not card_id:
            raise ValueError("card_id must be a non-empty string")

        def choose(current: RoomSnapshot) -> Any:
            self._require_controlled_seat(current, seat)
            seat_state = current.state.seats[seat]
            color_index = {"red": 0, "green": 1, "blue": 2}[color]
            next_level = seat_state.skill_levels[color_index] + 1
            candidates = self._catalog.upgrade_candidates(seat, color, next_level)
            if len(candidates) != 2 or card_id not in {
                card["id"] for card in candidates
            }:
                raise ValueError("card_id is not a legal upgrade candidate")
            chosen_card = next(card for card in candidates if card["id"] == card_id)
            unchosen = next(card for card in candidates if card["id"] != card_id)
            current_card = next(
                self._catalog.card(active_id)
                for active_id in seat_state.active_card_ids
                if self._catalog.card(active_id)["color_key"] == color
            )
            passive_type = unchosen.get("passive_bonus", {}).get("type")
            from goa2.engine import passive_type_index

            passive_index = passive_type_index(passive_type)
            if passive_index is None:
                raise ValueError("unchosen upgrade card has no supported passive")
            return self._engine.choose_upgrade(
                current.state,
                seat,
                color,
                current_card["id"],
                chosen_card["id"],
                unchosen["id"],
                passive_index,
            )

        return self._mutate(room_id, expected_revision, choose)

    def complete_action(
        self,
        seat: int,
        room_id: str = "default",
        expected_revision: int | None = None,
    ) -> dict[str, Any]:
        self._validate_seat(seat)

        def complete(current: RoomSnapshot) -> Any:
            self._require_controlled_seat(current, seat)
            return self._engine.complete_action(current.state, seat)

        return self._mutate(room_id, expected_revision, complete)

    def debug_teleport(
        self,
        seat: int,
        x: Any,
        y: Any,
        room_id: str = "default",
        expected_revision: int | None = None,
    ) -> dict[str, Any]:
        self._validate_seat(seat)
        if any(isinstance(value, bool) or not isinstance(value, int) for value in (x, y)):
            raise ValueError("x and y must be integers")

        def teleport(current: RoomSnapshot) -> Any:
            self._require_controlled_seat(current, seat)
            return self._engine.debug_teleport(current.state, seat, x, y)

        return self._mutate(room_id, expected_revision, teleport)

    def debug_enter_round_end(
        self,
        room_id: str = "default",
        expected_revision: int | None = None,
    ) -> dict[str, Any]:
        return self._mutate(
            room_id,
            expected_revision,
            lambda current: self._engine.debug_enter_round_end(current.state),
        )

    def debug_remove_minion(
        self,
        seat: int,
        minion_id: Any,
        room_id: str = "default",
        expected_revision: int | None = None,
    ) -> dict[str, Any]:
        self._validate_seat(seat)
        if not isinstance(minion_id, str) or not minion_id:
            raise ValueError("minion_id must be a non-empty string")

        def remove(current: RoomSnapshot) -> Any:
            self._require_controlled_seat(current, seat)
            return self._engine.debug_remove_minion(
                current.state, seat, minion_id
            )

        return self._mutate(room_id, expected_revision, remove)

    def debug_skip_current(
        self,
        room_id: str = "default",
        expected_revision: int | None = None,
    ) -> dict[str, Any]:
        def skip(current: RoomSnapshot) -> Any:
            state = current.state
            if state.phase.value != "card_resolution" or state.active_seat is None:
                raise ValueError("there is no active card to skip")
            return self._engine.skip_action(state, int(state.active_seat))

        return self._mutate(room_id, expected_revision, skip)

    def debug_skip_all(
        self,
        room_id: str = "default",
        expected_revision: int | None = None,
    ) -> dict[str, Any]:
        def skip_all(current: RoomSnapshot) -> Any:
            state = current.state
            if state.phase.value != "card_resolution":
                raise ValueError("remaining cards can only be skipped during resolution")
            while state.phase.value == "card_resolution":
                pending_choice = state.pending_initiative_choice
                if pending_choice is not None:
                    state = self._engine.resolve_initiative_choice(
                        state,
                        int(pending_choice.chooser),
                        pending_choice.choice_id,
                        int(pending_choice.candidate_seats[0]),
                    )
                    continue
                if state.active_seat is None:
                    raise ValueError("card resolution has no active seat")
                state = self._engine.skip_action(state, int(state.active_seat))
            return state

        return self._mutate(room_id, expected_revision, skip_all)

    def debug_set_seat_coins(
        self,
        seat: int,
        coins: Any,
        room_id: str = "default",
        expected_revision: int | None = None,
    ) -> dict[str, Any]:
        self._validate_seat(seat)
        return self._mutate(
            room_id,
            expected_revision,
            lambda current: self._engine.debug_set_seat_coins(
                current.state, seat, coins
            ),
        )

    def debug_set_crystal_life(
        self,
        team: Any,
        crystal_life: Any,
        room_id: str = "default",
        expected_revision: int | None = None,
    ) -> dict[str, Any]:
        if team not in {"blue", "red"}:
            raise ValueError("team must be blue or red")
        return self._mutate(
            room_id,
            expected_revision,
            lambda current: self._engine.debug_set_crystal_life(
                current.state, team, crystal_life
            ),
        )

    def debug_set_frontline_marks(
        self,
        team: Any,
        frontline_marks: Any,
        room_id: str = "default",
        expected_revision: int | None = None,
    ) -> dict[str, Any]:
        if team not in {"blue", "red"}:
            raise ValueError("team must be blue or red")
        return self._mutate(
            room_id,
            expected_revision,
            lambda current: self._engine.debug_set_frontline_marks(
                current.state, team, frontline_marks
            ),
        )

    def debug_defeat_hero(
        self,
        seat: int,
        room_id: str = "default",
        expected_revision: int | None = None,
    ) -> dict[str, Any]:
        self._validate_seat(seat)
        return self._mutate(
            room_id,
            expected_revision,
            lambda current: self._engine.debug_defeat_hero(current.state, seat),
        )

    def debug_reset_minions(
        self,
        room_id: str = "default",
        expected_revision: int | None = None,
    ) -> dict[str, Any]:
        return self._mutate(
            room_id,
            expected_revision,
            lambda current: self._engine.debug_reset_minions(current.state),
        )

    def debug_advance_frontline(
        self,
        team: Any,
        room_id: str = "default",
        expected_revision: int | None = None,
    ) -> dict[str, Any]:
        if team not in {"blue", "red"}:
            raise ValueError("team must be blue or red")
        return self._mutate(
            room_id,
            expected_revision,
            lambda current: self._engine.debug_advance_frontline(
                current.state, team
            ),
        )

    def _get_or_create(self, room_id: str) -> RoomSnapshot:
        room = self._rooms.read(room_id)
        if room is not None:
            return room
        return self._rooms.transact(
            room_id, lambda current: current or self._new_room(room_id)
        )

    def _new_room(self, room_id: str) -> RoomSnapshot:
        if not room_id or len(room_id) > 64:
            raise ValueError("room_id must contain 1 to 64 characters")
        state = self._engine.create_state(self._catalog)
        return RoomSnapshot(
            room_id=room_id,
            revision=self._engine.revision(state),
            controlled_seat=self._engine.controlled_seat(state),
            state=state,
        )

    def _check_revision(
        self, room: RoomSnapshot, expected_revision: int | None
    ) -> None:
        if expected_revision is None:
            raise ValueError("expected_revision is required")
        if (
            isinstance(expected_revision, bool)
            or not isinstance(expected_revision, int)
            or expected_revision < 0
        ):
            raise ValueError("expected_revision must be a non-negative integer")
        if expected_revision != room.revision:
            raise RevisionConflict(
                f"revision mismatch: expected {expected_revision}, "
                f"current {room.revision}"
            )

    def _mutate(
        self,
        room_id: str,
        expected_revision: int | None,
        operation: Callable[[RoomSnapshot], Any],
    ) -> dict[str, Any]:
        def mutate(room: RoomSnapshot | None) -> RoomSnapshot:
            current = room or self._new_room(room_id)
            self._check_revision(current, expected_revision)
            state = operation(current)
            return RoomSnapshot(
                room_id=current.room_id,
                revision=self._engine.revision(state),
                controlled_seat=self._engine.controlled_seat(state),
                state=state,
                seat_token_hashes=current.seat_token_hashes,
            )

        return self._project(self._rooms.transact(room_id, mutate))

    @staticmethod
    def _validate_seat(seat: int) -> None:
        if isinstance(seat, bool) or not isinstance(seat, int) or not 0 <= seat < 4:
            raise ValueError("seat must be an integer from 0 to 3")

    @staticmethod
    def _require_controlled_seat(room: RoomSnapshot, seat: int) -> None:
        # HTTP player tokens own authorization in multiplayer rooms.
        return None

    def _project(
        self,
        room: RoomSnapshot,
        private_seat: int | None = None,
    ) -> dict[str, Any]:
        if private_seat is None and not any(room.seat_token_hashes):
            private_seat = room.controlled_seat
        public = _json_value(self._engine.public_view(room.state))
        events = _json_value(room.events)
        public["log"] = events
        state = {
            **public,
            "revision": room.revision,
            "controlled_seat": private_seat,
        }
        private = (
            _json_value(self._engine.private_view(room.state, private_seat))
            if private_seat is not None
            else {"legal_actions": []}
        )
        return {
            "room_id": room.room_id,
            "revision": room.revision,
            "controlled_seat": private_seat,
            "events": events,
            "state": state,
            "public": public,
            "private": private,
            "private_view": private,
        }


def _json_value(value: Any) -> Any:
    if is_dataclass(value):
        return _json_value(asdict(value))
    if isinstance(value, Enum):
        return _json_value(value.value)
    if isinstance(value, Mapping):
        return {str(key): _json_value(item) for key, item in value.items()}
    if isinstance(value, (list, tuple)):
        return [_json_value(item) for item in value]
    if isinstance(value, (set, frozenset)):
        return sorted((_json_value(item) for item in value), key=repr)
    if value is None or isinstance(value, (str, int, float, bool)):
        return value
    raise TypeError(f"view contains non-serializable value: {type(value).__name__}")


def _token_hash(token: str | None) -> str:
    if token is None:
        raise ValueError("player token is required")
    return sha256(token.encode("utf-8")).hexdigest()
