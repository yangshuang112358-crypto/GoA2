"""Adapter from application use cases to the domain room reducer."""

from __future__ import annotations

from collections.abc import Mapping
from dataclasses import replace
from typing import Any

from goa2.domain import Phase, ResetRoom, RoomState, Seat, SwitchControlledSeat
from goa2.engine import modified_value, reduce_room

from .catalog import CatalogBundle


class DomainEngineAdapter:
    def __init__(self) -> None:
        self._catalog: CatalogBundle | None = None

    def create_state(
        self,
        catalog: CatalogBundle,
        *,
        starting_crystal_life: int = 7,
        frontline_victory_marks: int = 3,
        hero_ids: tuple[str, str, str, str] | None = None,
    ) -> RoomState:
        self._catalog = catalog
        from goa2.domain import BoardCell, Seat, Team, UnitState, create_initial_room_state

        selected_heroes = self._validated_hero_ids(hero_ids)
        state = create_initial_room_state(
            starting_crystal_life=starting_crystal_life,
            frontline_victory_marks=frontline_victory_marks,
            hero_ids=selected_heroes,
        )
        board = tuple(
            BoardCell(
                x=cell["x"],
                y=cell["y"],
                obstacle=cell["obstacle"],
                region=cell["region"],
                state=cell["state"],
            )
            for cell in catalog.map_cells
        )
        blue = sorted(
            (cell for cell in catalog.map_cells if cell["state"] == "blueHeroSpawn"),
            key=lambda cell: (cell["y"], cell["x"]),
        )
        red = sorted(
            (cell for cell in catalog.map_cells if cell["state"] == "redHeroSpawn"),
            key=lambda cell: (cell["y"], cell["x"]),
        )
        spawn_by_seat = {0: blue[0], 2: blue[1], 1: red[0], 3: red[1]}
        units = tuple(
            UnitState(
                unit_id=f"hero-{seat}",
                seat=Seat(seat),
                team=Team.BLUE if seat % 2 == 0 else Team.RED,
                x=spawn_by_seat[seat]["x"],
                y=spawn_by_seat[seat]["y"],
                spawn_x=spawn_by_seat[seat]["x"],
                spawn_y=spawn_by_seat[seat]["y"],
            )
            for seat in range(4)
        )
        from goa2.engine import spawn_minions_for_region

        return replace(
            state,
            board=board,
            units=units,
            minions=spawn_minions_for_region(board, "mid"),
            seats=tuple(
                replace(
                    seat_state,
                    active_card_ids=catalog.initial_hand_card_ids_for_hero(
                        selected_heroes[int(seat_state.seat)]
                    ),
                )
                for seat_state in state.seats
            ),
        )

    def configure_room(
        self,
        state: RoomState,
        *,
        starting_crystal_life: int,
        frontline_victory_marks: int,
        hero_ids: tuple[str, str, str, str],
    ) -> RoomState:
        from goa2.domain import Phase
        from goa2.engine import spawn_minions_for_region

        if self._catalog is None:
            raise RuntimeError("catalog must be loaded before configuring room state")
        if not self._is_unstarted_state(state):
            raise ValueError("room can only be configured before play begins")
        if (
            isinstance(starting_crystal_life, bool)
            or not isinstance(starting_crystal_life, int)
            or starting_crystal_life <= 0
        ):
            raise ValueError("starting_crystal_life must be a positive integer")
        if (
            isinstance(frontline_victory_marks, bool)
            or not isinstance(frontline_victory_marks, int)
            or frontline_victory_marks <= 0
        ):
            raise ValueError("frontline_victory_marks must be a positive integer")
        selected_heroes = self._validated_hero_ids(hero_ids)
        if state.phase is not Phase.CARD_SELECTION:
            raise ValueError("room configuration requires card selection phase")
        if state.minions != spawn_minions_for_region(state.board, "mid"):
            raise ValueError("room minions have already changed")

        teams = tuple(
            replace(team, crystal_life=starting_crystal_life, frontline_marks=0)
            for team in state.teams
        )
        seats = tuple(
            replace(
                seat_state,
                active_card_ids=self._catalog.initial_hand_card_ids_for_hero(
                    selected_heroes[int(seat_state.seat)]
                ),
            )
            for seat_state in state.seats
        )
        return replace(
            state,
            revision=state.revision + 1,
            hero_ids=selected_heroes,
            starting_crystal_life=starting_crystal_life,
            frontline_victory_marks=frontline_victory_marks,
            teams=teams,
            seats=seats,
        )

    def control(self, state: RoomState, seat: int) -> RoomState:
        command = SwitchControlledSeat(
            expected_revision=state.revision,
            target_seat=Seat(seat),
        )
        return reduce_room(state, command).state

    def reset(self, state: RoomState) -> RoomState:
        if self._catalog is None:
            raise RuntimeError("catalog must be loaded before resetting room state")
        return replace(
            self.create_state(
                self._catalog,
                starting_crystal_life=state.starting_crystal_life,
                frontline_victory_marks=state.frontline_victory_marks,
                hero_ids=state.hero_ids,
            ),
            revision=state.revision + 1,
        )

    def select_card(self, state: RoomState, seat: int, card_id: str) -> RoomState:
        from goa2.domain import SelectCard

        command = SelectCard(
            expected_revision=state.revision,
            seat=Seat(seat),
            card_id=card_id,
        )
        return reduce_room(state, command).state

    def confirm_selection(self, state: RoomState, seat: int) -> RoomState:
        from goa2.domain import ConfirmSelection

        command = ConfirmSelection(
            expected_revision=state.revision,
            seat=Seat(seat),
        )
        return reduce_room(state, command).state

    def reveal_cards(
        self,
        state: RoomState,
        initiatives: Mapping[str, int],
        profiles: Mapping[str, Mapping[str, Any]],
    ) -> RoomState:
        from goa2.domain import ActionProfile, RevealCards

        cards = tuple(
            (
                Seat(seat),
                card_id,
                initiatives[card_id],
            )
            for seat, card_id in self.selected_cards(state).items()
            if card_id is not None
        )
        command = RevealCards(
            expected_revision=state.revision,
            cards=cards,
            profiles=tuple(
                (
                    Seat(seat),
                    ActionProfile.from_dict(dict(profiles[card_id])),
                )
                for seat, card_id in self.selected_cards(state).items()
                if card_id is not None
            ),
        )
        return reduce_room(state, command).state

    def choose_action(
        self, state: RoomState, seat: int, kind: str, source_slot: str | None
    ) -> RoomState:
        from goa2.domain import ActionChoice, ChooseResolutionAction

        return reduce_room(
            state,
            ChooseResolutionAction(
                expected_revision=state.revision,
                seat=Seat(seat),
                choice=ActionChoice(kind=kind, source_slot=source_slot),
            ),
        ).state

    def cancel_action(self, state: RoomState, seat: int) -> RoomState:
        from goa2.domain import CancelResolutionAction

        return reduce_room(
            state,
            CancelResolutionAction(
                expected_revision=state.revision,
                seat=Seat(seat),
            ),
        ).state

    def reachable(self, state: RoomState, seat: int) -> list[dict[str, int]]:
        from goa2.engine import reachable_cells

        if state.pending_movement is None:
            return []
        costs, _ = reachable_cells(state, Seat(seat), state.pending_movement)
        return [
            {"x": x, "y": y, "cost": cost}
            for (x, y), cost in sorted(costs.items(), key=lambda item: (item[1], item[0]))
        ]

    def move(self, state: RoomState, seat: int, x: int, y: int) -> RoomState:
        from goa2.domain import MoveHero

        return reduce_room(
            state,
            MoveHero(
                expected_revision=state.revision,
                seat=Seat(seat),
                x=x,
                y=y,
            ),
        ).state

    def declare_basic_attack(
        self, state: RoomState, seat: int, target_id: str
    ) -> RoomState:
        from goa2.domain import DeclareBasicAttack

        return reduce_room(
            state,
            DeclareBasicAttack(
                expected_revision=state.revision,
                seat=Seat(seat),
                target_id=target_id,
            ),
        ).state

    def resolve_basic_defense(
        self,
        state: RoomState,
        seat: int,
        card_id: str | None,
        defense_value: int | None,
        exclamation: bool,
    ) -> RoomState:
        from goa2.domain import ResolveBasicDefense

        return reduce_room(
            state,
            ResolveBasicDefense(
                expected_revision=state.revision,
                seat=Seat(seat),
                card_id=card_id,
                defense_value=defense_value,
                exclamation=exclamation,
            ),
        ).state

    def begin_round_end(self, state: RoomState) -> RoomState:
        from goa2.domain import BeginRoundEnd

        return reduce_room(
            state,
            BeginRoundEnd(expected_revision=state.revision),
        ).state

    def resolve_captain_choice(
        self,
        state: RoomState,
        seat: int,
        choice_id: str,
        candidate_id: str,
    ) -> RoomState:
        from goa2.domain import ResolveCaptainChoice

        return reduce_room(
            state,
            ResolveCaptainChoice(
                expected_revision=state.revision,
                seat=Seat(seat),
                choice_id=choice_id,
                candidate_id=candidate_id,
            ),
        ).state

    def resolve_initiative_choice(
        self,
        state: RoomState,
        seat: int,
        choice_id: str,
        chosen_seat: int,
    ) -> RoomState:
        from goa2.domain import ResolveInitiativeChoice

        return reduce_room(
            state,
            ResolveInitiativeChoice(
                expected_revision=state.revision,
                seat=Seat(seat),
                choice_id=choice_id,
                chosen_seat=Seat(chosen_seat),
            ),
        ).state

    def resolve_respawn(
        self, state: RoomState, seat: int, x: int, y: int
    ) -> RoomState:
        from goa2.domain import ResolveRespawn

        return reduce_room(
            state,
            ResolveRespawn(
                expected_revision=state.revision,
                seat=Seat(seat),
                x=x,
                y=y,
            ),
        ).state

    def complete_action(self, state: RoomState, seat: int) -> RoomState:
        from goa2.domain import CompletePendingAction

        return reduce_room(
            state,
            CompletePendingAction(
                expected_revision=state.revision,
                seat=Seat(seat),
            ),
        ).state

    def debug_teleport(
        self, state: RoomState, seat: int, x: int, y: int
    ) -> RoomState:
        from goa2.domain import DebugTeleportHero

        return reduce_room(
            state,
            DebugTeleportHero(
                expected_revision=state.revision,
                seat=Seat(seat),
                x=x,
                y=y,
            ),
        ).state

    def debug_enter_round_end(self, state: RoomState) -> RoomState:
        from goa2.domain import DebugEnterRoundEnd

        return reduce_room(
            state,
            DebugEnterRoundEnd(expected_revision=state.revision),
        ).state

    def debug_remove_minion(
        self, state: RoomState, seat: int, minion_id: str
    ) -> RoomState:
        from goa2.domain import DebugRemoveMinion

        return reduce_room(
            state,
            DebugRemoveMinion(
                expected_revision=state.revision,
                seat=Seat(seat),
                minion_id=minion_id,
            ),
        ).state

    def debug_set_seat_coins(
        self, state: RoomState, seat: int, coins: int
    ) -> RoomState:
        from goa2.domain import DebugSetSeatCoins

        return reduce_room(
            state,
            DebugSetSeatCoins(
                expected_revision=state.revision,
                seat=Seat(seat),
                coins=coins,
            ),
        ).state

    def debug_set_crystal_life(
        self, state: RoomState, team: str, crystal_life: int
    ) -> RoomState:
        from goa2.domain import DebugSetCrystalLife, Team

        return reduce_room(
            state,
            DebugSetCrystalLife(
                expected_revision=state.revision,
                team=Team(team),
                crystal_life=crystal_life,
            ),
        ).state

    def debug_set_frontline_marks(
        self, state: RoomState, team: str, frontline_marks: int
    ) -> RoomState:
        from goa2.domain import DebugSetFrontlineMarks, Team

        return reduce_room(
            state,
            DebugSetFrontlineMarks(
                expected_revision=state.revision,
                team=Team(team),
                frontline_marks=frontline_marks,
            ),
        ).state

    def debug_defeat_hero(self, state: RoomState, seat: int) -> RoomState:
        from goa2.domain import DebugDefeatHero

        return reduce_room(
            state,
            DebugDefeatHero(
                expected_revision=state.revision,
                seat=Seat(seat),
            ),
        ).state

    def debug_reset_minions(self, state: RoomState) -> RoomState:
        from goa2.domain import DebugResetMinions

        return reduce_room(
            state,
            DebugResetMinions(expected_revision=state.revision),
        ).state

    def debug_advance_frontline(self, state: RoomState, team: str) -> RoomState:
        from goa2.domain import DebugAdvanceFrontline, Team

        return reduce_room(
            state,
            DebugAdvanceFrontline(
                expected_revision=state.revision,
                team=Team(team),
            ),
        ).state

    def force_confirm(
        self, state: RoomState, default_cards: Mapping[int, str]
    ) -> RoomState:
        from goa2.domain import ForceConfirmSelections

        command = ForceConfirmSelections(
            expected_revision=state.revision,
            default_cards=default_cards,
        )
        return reduce_room(state, command).state

    def choose_upgrade(
        self,
        state: RoomState,
        seat: int,
        color: str,
        current_card_id: str,
        chosen_card_id: str,
        unchosen_card_id: str,
        passive_index: int,
    ) -> RoomState:
        from goa2.domain import ChooseCardUpgrade

        return reduce_room(
            state,
            ChooseCardUpgrade(
                expected_revision=state.revision,
                seat=Seat(seat),
                color=color,
                current_card_id=current_card_id,
                chosen_card_id=chosen_card_id,
                unchosen_card_id=unchosen_card_id,
                passive_index=passive_index,
            ),
        ).state

    def skip_action(self, state: RoomState, seat: int) -> RoomState:
        from goa2.domain import SkipAction

        command = SkipAction(
            expected_revision=state.revision,
            seat=Seat(seat),
        )
        return reduce_room(state, command).state

    def selected_cards(self, state: RoomState) -> dict[int, str | None]:
        return {
            index: _field(seat, "selected_card_id")
            for index, seat in enumerate(state.seats)
        }

    def revision(self, state: RoomState) -> int:
        return state.revision

    def controlled_seat(self, state: RoomState) -> int:
        return int(state.controlled_seat)

    def public_view(self, state: RoomState) -> dict[str, Any]:
        phase = state.phase.value
        revealed = phase in {"card_resolution", "round_end", "game_over"}
        seats = []
        revealed_cards = []
        for index, seat in enumerate(state.seats):
            hero = self._seat_heroes(state)[index]
            selected_card_id = _field(seat, "selected_card_id")
            revealed_card_id = _field(seat, "revealed_card_id")
            seat_view = {
                "seat": int(_field(seat, "seat", index)),
                "team": _enum_value(_field(seat, "team")),
                "hero_id": hero["hero_id"],
                "hero": {"hero_id": hero["hero_id"], "name": hero["name"]},
                "selected": selected_card_id is not None,
                "confirmed": bool(_field(seat, "confirmed", False)),
                "defeated": bool(_field(seat, "defeated", False)),
                "coins": seat.coins,
                "level": seat.hero_level,
                "passive_bonuses": {
                    key: seat.passive_bonuses[index]
                    for index, key in enumerate(
                        ("attack", "defense", "movement", "initiative", "range", "ranged")
                    )
                },
                "card_color_states": self._card_color_states(state, index),
            }
            if revealed and revealed_card_id is not None:
                seat_view["revealed_card_id"] = revealed_card_id
                revealed_cards.append(
                    {
                        "seat": index,
                        "card_id": revealed_card_id,
                        "initiative": (
                            state.revealed_initiatives[index]
                            if index < len(state.revealed_initiatives)
                            else modified_value(
                                seat,
                                "initiative",
                                self._catalog.initiative(revealed_card_id),
                            )
                        ),
                    }
                )
            seats.append(seat_view)

        view = {
            "phase": state.phase.value,
            "seats": seats,
            "units": [unit.to_dict() for unit in state.units],
            "board": [cell.to_dict() for cell in state.board],
            "log": [],
            "legal_actions": [],
            "debug_actions": (
                [
                    "debug_teleport",
                    "force_confirm",
                    "enter_round_end",
                    "remove_minion",
                    "set_coins",
                    "set_crystal",
                    "set_frontline",
                    "defeat_hero",
                    "reset_minions",
                    "advance_frontline",
                ]
                if phase == "card_selection"
                else [
                    "debug_teleport",
                    "begin_round_end",
                    "remove_minion",
                    "set_coins",
                    "set_crystal",
                    "set_frontline",
                    "defeat_hero",
                    "reset_minions",
                    "advance_frontline",
                ]
                if phase == "round_end"
                else [
                    "debug_teleport",
                    "enter_round_end",
                    "remove_minion",
                    "skip_current",
                    "skip_all",
                    "set_coins",
                    "set_crystal",
                    "set_frontline",
                    "defeat_hero",
                    "reset_minions",
                    "advance_frontline",
                ]
                if phase == "card_resolution"
                else [
                    "debug_teleport",
                    "enter_round_end",
                    "remove_minion",
                    "set_coins",
                    "set_crystal",
                    "set_frontline",
                    "defeat_hero",
                    "reset_minions",
                    "advance_frontline",
                ]
                if phase != "game_over"
                else []
            ),
            "card_effects_enabled": False,
            "room_config": {
                "starting_crystal_life": state.starting_crystal_life,
                "frontline_victory_marks": state.frontline_victory_marks,
                "hero_ids": list(state.hero_ids),
                "configurable": self._is_unstarted_state(state),
            },
            "decision_coin": state.decision_coin.value,
            "round": state.round_number,
            "turn": state.turn_number,
            "minions": [minion.to_dict() for minion in state.minions],
            "teams": [team.to_dict() for team in state.teams],
            "combat_region": state.combat_region,
            "round_end_step": state.round_end_step.value,
            "pending_captain_choice": (
                state.pending_captain_choice.to_dict()
                if state.pending_captain_choice is not None
                else None
            ),
            "pending_upgrade": (
                state.pending_upgrade.to_dict()
                if state.pending_upgrade is not None
                else None
            ),
            "pending_upgrades": [
                {
                    "seat": index,
                    "remaining_choices": seat.pending_upgrade_choices,
                }
                for index, seat in enumerate(state.seats)
                if seat.pending_upgrade_choices > 0
            ],
            "pending_attack": (
                state.pending_attack.to_dict()
                if state.pending_attack is not None
                else None
            ),
            "pending_initiative_choice": (
                state.pending_initiative_choice.to_dict()
                if state.pending_initiative_choice is not None
                else None
            ),
            "pending_respawn": (
                state.pending_respawn.to_dict()
                if state.pending_respawn is not None
                else None
            ),
            "winner": state.winner.value if state.winner is not None else None,
        }
        if revealed:
            view["revealed_cards"] = revealed_cards
            view["initiative_order"] = _field(state, "initiative_order", ())
            view["resolved_seats"] = _field(state, "resolved_seats", ())
            if state.active_seat is not None:
                view["active_seat"] = state.active_seat
        return view

    def private_view(self, state: RoomState, seat: int) -> dict[str, Any]:
        hero = self._seat_heroes(state)[seat]
        seat_state = state.seats[seat]
        occupied_positions = {
            (unit.x, unit.y) for unit in state.units
        } | {
            (minion.x, minion.y) for minion in state.minions
        }
        selected_card_id = _field(seat_state, "selected_card_id")
        confirmed = bool(_field(seat_state, "confirmed", False))
        legal_actions = []
        if state.phase.value == "card_selection" and not confirmed:
            legal_actions.append("select_card")
            if selected_card_id is not None:
                legal_actions.append("confirm_selection")
        if (
            state.phase.value == "card_resolution"
            and state.active_seat == Seat(seat)
            and state.pending_respawn is None
        ):
            if state.pending_action is None:
                legal_actions.append("skip_action")
            profile = state.seats[seat].action_profile
            if profile is not None and state.pending_attack is None:
                action_options = []
                if (
                    profile.primary_family != "attack"
                    or self.basic_attack_targets(state, seat)
                ):
                    action_options.append({"kind": "primary", "source_slot": None})
                if profile.secondary_movement:
                    action_options.append(
                        {"kind": "secondary_movement", "source_slot": None}
                    )
                action_options.extend(
                    {"kind": "fast_move", "source_slot": source}
                    for source in profile.fast_move_sources
                )
                if action_options:
                    legal_actions.append("choose_action")
            else:
                action_options = []
            if (
                state.pending_action is not None
                and state.pending_action.kind == "primary"
                and profile is not None
                and not profile.primary_is_movement
                and profile.implementation_status == "data_only"
                and profile.primary_family != "attack"
            ):
                legal_actions.append("complete_action")
            if (
                state.pending_action is not None
                and state.pending_action.kind == "primary"
                and profile is not None
                and profile.primary_family == "attack"
                and state.pending_attack is None
            ):
                legal_actions.append("declare_basic_attack")
            if state.pending_action is not None and state.pending_attack is None:
                legal_actions.append("cancel_action")
        else:
            action_options = []
        if (
            state.phase.value == "card_resolution"
            and state.pending_respawn is not None
            and state.pending_respawn.seat == Seat(seat)
        ):
            legal_actions.append("resolve_respawn")
        if (
            state.phase.value == "card_resolution"
            and state.pending_attack is not None
            and state.pending_attack.target_kind == "hero"
            and state.pending_attack.target_id == state.units[seat].unit_id
        ):
            legal_actions.append("resolve_defense")
        if (
            state.phase.value == "card_resolution"
            and state.pending_initiative_choice is not None
            and state.pending_initiative_choice.chooser == Seat(seat)
        ):
            legal_actions.append("resolve_initiative_choice")
        if (
            state.pending_captain_choice is not None
            and state.pending_captain_choice.chooser == Seat(seat)
        ):
            legal_actions.append("resolve_captain_choice")
        if (
            state.phase.value == "upgrade"
            and seat_state.pending_upgrade_choices > 0
        ):
            legal_actions.append("choose_upgrade")
        return {
            "seat": seat,
            "hero_id": hero["hero_id"],
            "hand": list(seat_state.active_card_ids),
            "discard": list(seat_state.discarded_card_ids),
            "used_card_ids": list(seat_state.used_card_ids),
            "selected_card_id": selected_card_id,
            "confirmed": confirmed,
            "legal_actions": legal_actions,
            "action_options": action_options,
            "pending_action": (
                state.pending_action.to_dict()
                if state.active_seat == Seat(seat) and state.pending_action is not None
                else None
            ),
            "reachable": (
                self.reachable(state, seat)
                if state.active_seat == Seat(seat) and state.pending_movement is not None
                else []
            ),
            "fast_move_targets": (
                [
                    {"x": x, "y": y}
                    for x, y in self.fast_move_targets(state, seat)
                ]
                if state.active_seat == Seat(seat)
                and state.pending_action is not None
                and state.pending_action.kind == "fast_move"
                else []
            ),
            "attack_targets": (
                self._attack_target_views(state, seat)
                if "declare_basic_attack" in legal_actions
                else []
            ),
            "eligible_defense_cards": (
                self._eligible_defense_cards(state, seat)
                if "resolve_defense" in legal_actions
                else []
            ),
            "pending_initiative_choice": (
                state.pending_initiative_choice.to_dict()
                if "resolve_initiative_choice" in legal_actions
                else None
            ),
            "respawn_targets": (
                [
                    {"x": x, "y": y}
                    for x, y in state.pending_respawn.candidate_cells
                ]
                if "resolve_respawn" in legal_actions
                else []
            ),
            "upgrade_options": (
                self._upgrade_options(state, seat)
                if "choose_upgrade" in legal_actions
                else []
            ),
            "debug_teleport_targets": [
                {"x": cell.x, "y": cell.y}
                for cell in state.board
                if not cell.obstacle
                and (cell.x, cell.y) not in occupied_positions
            ],
        }

    def _seat_heroes(self, state: RoomState) -> list[dict[str, Any]]:
        if self._catalog is None:
            raise RuntimeError("catalog must be loaded before projecting room state")
        return [self._catalog.hero(hero_id) for hero_id in state.hero_ids]

    def _card_color_states(
        self, state: RoomState, seat: int
    ) -> list[dict[str, Any]]:
        seat_state = state.seats[seat]
        used_turn = {
            card_id: index + 1
            for index, card_id in enumerate(seat_state.used_card_ids)
        }
        cards_by_color = {
            card["color_key"]: card
            for card in (
                self._catalog.card(card_id)
                for card_id in seat_state.active_card_ids
            )
        }
        states = []
        for color in ("gold", "silver", "red", "green", "blue"):
            card = cards_by_color[color]
            card_id = card["id"]
            status = (
                "discarded"
                if card_id in seat_state.discarded_card_ids
                else "played"
                if card_id in used_turn
                else "selected"
                if state.phase not in {Phase.CARD_SELECTION, Phase.CARD_REVEAL}
                and seat_state.selected_card_id == card_id
                else "available"
            )
            states.append({
                "color": color,
                "status": status,
                "played_turn": used_turn.get(card_id),
            })
        states.append({
            "color": "purple",
            "status": "available" if seat_state.hero_level >= 8 else "locked",
            "played_turn": None,
        })
        return states

    @staticmethod
    def fast_move_targets(state: RoomState, seat: int) -> tuple[tuple[int, int], ...]:
        from goa2.engine import fast_move_targets

        return fast_move_targets(state, Seat(seat))

    @staticmethod
    def basic_attack_targets(
        state: RoomState, seat: int
    ) -> tuple[tuple[str, str], ...]:
        from goa2.engine import basic_attack_targets

        return basic_attack_targets(state, Seat(seat))

    def _attack_target_views(
        self, state: RoomState, seat: int
    ) -> list[dict[str, Any]]:
        target_kinds = dict(self.basic_attack_targets(state, seat))
        heroes = {
            unit.unit_id: {
                "unit_id": unit.unit_id,
                "kind": "hero",
                "team": unit.team.value,
                "seat": int(unit.seat),
                "x": unit.x,
                "y": unit.y,
            }
            for unit in state.units
        }
        minions = {
            minion.unit_id: {
                "unit_id": minion.unit_id,
                "kind": "minion",
                "team": minion.team.value,
                "x": minion.x,
                "y": minion.y,
            }
            for minion in state.minions
        }
        views = {**heroes, **minions}
        return [
            views[target_id]
            for target_id in sorted(target_kinds)
        ]

    def _eligible_defense_cards(
        self, state: RoomState, seat: int
    ) -> list[dict[str, Any]]:
        seat_state = state.seats[seat]
        unavailable = {
            *seat_state.used_card_ids,
            *seat_state.discarded_card_ids,
            seat_state.revealed_card_id,
        }
        result = []
        for card in (
            self._catalog.card(card_id)
            for card_id in seat_state.active_card_ids
        ):
            if card["id"] in unavailable:
                continue
            try:
                profile = self._catalog.defense_profile(card["id"])
                if profile["defense_value"] is not None:
                    profile = {
                        **profile,
                        "defense_value": modified_value(
                            seat_state,
                            "defense",
                            profile["defense_value"],
                        ),
                    }
                result.append(profile)
            except ValueError:
                continue
        return result

    def _upgrade_options(self, state: RoomState, seat: int) -> list[dict[str, Any]]:
        levels = state.seats[seat].skill_levels
        result = []
        for index, color in enumerate(("red", "green", "blue")):
            next_level = levels[index] + 1
            if next_level > 3 or (next_level == 3 and min(levels) < 2):
                continue
            candidates = self._catalog.upgrade_candidates_for_hero(
                state.hero_ids[seat], color, next_level
            )
            if len(candidates) != 2:
                continue
            result.append({
                "color": color,
                "level": next_level,
                "candidates": [
                    {
                        "card_id": card["id"],
                        "gained_passive": other.get("passive_bonus", {}).get("type"),
                    }
                    for card, other in (
                        (candidates[0], candidates[1]),
                        (candidates[1], candidates[0]),
                    )
                ],
            })
        return result

    def _validated_hero_ids(
        self, hero_ids: tuple[str, str, str, str] | None
    ) -> tuple[str, str, str, str]:
        if self._catalog is None:
            raise RuntimeError("catalog must be loaded before selecting heroes")
        if hero_ids is None:
            heroes = self._catalog.cards.get("heroes", ())
            if len(heroes) < 4:
                raise ValueError("catalog must contain at least four heroes")
            selected = tuple(hero["hero_id"] for hero in heroes[:4])
        else:
            selected = hero_ids
        if (
            not isinstance(selected, tuple)
            or len(selected) != 4
            or any(not isinstance(hero_id, str) or not hero_id for hero_id in selected)
        ):
            raise ValueError("hero_ids must contain four non-empty strings")
        if len(set(selected)) != 4:
            raise ValueError("hero_ids must contain four distinct heroes")
        for hero_id in selected:
            self._catalog.hero(hero_id)
        return selected

    @staticmethod
    def _is_unstarted_state(state: RoomState) -> bool:
        from goa2.domain import Phase, RoundEndStep

        return (
            state.phase is Phase.CARD_SELECTION
            and state.round_number == 1
            and state.turn_number == 1
            and not state.initiative_order
            and not state.revealed_initiatives
            and not state.resolved_seats
            and state.active_seat is None
            and state.pending_action is None
            and state.pending_movement is None
            and state.pending_attack is None
            and state.pending_initiative_choice is None
            and state.pending_respawn is None
            and state.pending_captain_choice is None
            and state.pending_upgrade is None
            and state.round_end_step is RoundEndStep.READY
            and not state.resume_action_after_frontline
            and state.winner is None
            and state.combat_region == "mid"
            and all(
                seat.selected_card_id is None
                and not seat.confirmed
                and seat.revealed_card_id is None
                and seat.action_profile is None
                and not seat.used_card_ids
                and not seat.discarded_card_ids
                and not seat.defeated
                and seat.coins == 0
                and seat.hero_level == 1
                and seat.skill_levels == (1, 1, 1)
                and seat.passive_bonuses == (0, 0, 0, 0, 0, 0)
                and seat.pending_upgrade_choices == 0
                for seat in state.seats
            )
            and all(unit.x == unit.spawn_x and unit.y == unit.spawn_y for unit in state.units)
            and all(
                team.crystal_life == state.starting_crystal_life
                and team.frontline_marks == 0
                for team in state.teams
            )
        )


def _field(value: Any, name: str, default: Any = None) -> Any:
    if isinstance(value, Mapping):
        return value.get(name, default)
    return getattr(value, name, default)


def _enum_value(value: Any) -> Any:
    return getattr(value, "value", value)
