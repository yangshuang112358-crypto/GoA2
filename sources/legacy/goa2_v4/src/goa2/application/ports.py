"""Narrow ports at the application boundary."""

from __future__ import annotations

from typing import Any, Callable, Mapping, Protocol

from .catalog import CatalogBundle
from .room import RoomSnapshot


class EnginePort(Protocol):
    def create_state(self, catalog: CatalogBundle) -> Any: ...

    def configure_room(
        self,
        state: Any,
        *,
        starting_crystal_life: int,
        frontline_victory_marks: int,
        hero_ids: tuple[str, str, str, str],
    ) -> Any: ...

    def control(self, state: Any, seat: int) -> Any: ...

    def reset(self, state: Any) -> Any: ...

    def select_card(self, state: Any, seat: int, card_id: str) -> Any: ...

    def confirm_selection(self, state: Any, seat: int) -> Any: ...

    def reveal_cards(
        self,
        state: Any,
        initiatives: Mapping[str, int],
        profiles: Mapping[str, Mapping[str, Any]],
    ) -> Any: ...

    def choose_action(
        self, state: Any, seat: int, kind: str, source_slot: str | None
    ) -> Any: ...

    def cancel_action(self, state: Any, seat: int) -> Any: ...

    def reachable(self, state: Any, seat: int) -> Any: ...

    def move(self, state: Any, seat: int, x: int, y: int) -> Any: ...

    def declare_basic_attack(
        self, state: Any, seat: int, target_id: str
    ) -> Any: ...

    def resolve_basic_defense(
        self,
        state: Any,
        seat: int,
        card_id: str | None,
        defense_value: int | None,
        exclamation: bool,
    ) -> Any: ...

    def begin_round_end(self, state: Any) -> Any: ...

    def resolve_captain_choice(
        self, state: Any, seat: int, choice_id: str, candidate_id: str
    ) -> Any: ...

    def resolve_initiative_choice(
        self, state: Any, seat: int, choice_id: str, chosen_seat: int
    ) -> Any: ...

    def resolve_respawn(
        self, state: Any, seat: int, x: int, y: int
    ) -> Any: ...

    def choose_upgrade(
        self,
        state: Any,
        seat: int,
        color: str,
        current_card_id: str,
        chosen_card_id: str,
        unchosen_card_id: str,
        passive_index: int,
    ) -> Any: ...

    def complete_action(self, state: Any, seat: int) -> Any: ...

    def debug_teleport(self, state: Any, seat: int, x: int, y: int) -> Any: ...

    def debug_enter_round_end(self, state: Any) -> Any: ...

    def debug_remove_minion(self, state: Any, seat: int, minion_id: str) -> Any: ...

    def debug_set_seat_coins(self, state: Any, seat: int, coins: int) -> Any: ...

    def debug_set_crystal_life(
        self, state: Any, team: str, crystal_life: int
    ) -> Any: ...

    def debug_set_frontline_marks(
        self, state: Any, team: str, frontline_marks: int
    ) -> Any: ...

    def debug_defeat_hero(self, state: Any, seat: int) -> Any: ...

    def debug_reset_minions(self, state: Any) -> Any: ...

    def debug_advance_frontline(self, state: Any, team: str) -> Any: ...

    def force_confirm(
        self, state: Any, default_cards: Mapping[int, str]
    ) -> Any: ...

    def skip_action(self, state: Any, seat: int) -> Any: ...

    def selected_cards(self, state: Any) -> Mapping[int, str | None]: ...

    def revision(self, state: Any) -> int: ...

    def controlled_seat(self, state: Any) -> int: ...

    def public_view(self, state: Any) -> Any: ...

    def private_view(self, state: Any, seat: int) -> Any: ...


RoomMutation = Callable[[RoomSnapshot | None], RoomSnapshot]


class RoomStore(Protocol):
    def read(self, room_id: str) -> RoomSnapshot | None: ...

    def transact(self, room_id: str, mutation: RoomMutation) -> RoomSnapshot: ...
