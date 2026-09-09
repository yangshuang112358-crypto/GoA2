from __future__ import annotations

import unittest
from uuid import uuid4

from tests.contract_support import field, resolve_pending_initiative, skip_current_action


class ActionResolutionContractTests(unittest.TestCase):
    def setUp(self) -> None:
        from goa2.backend.bootstrap import build_application

        self.app = build_application()
        self.room_id = uuid4().hex
        payload = self.app.state(self.room_id)
        payload = self.app.force_confirm(
            self.room_id,
            field(payload, "revision"),
        )
        self.revealed = payload

    def test_only_active_seat_can_skip_independent_of_debug_control(self) -> None:
        active_seat = field(field(self.revealed, "public"), "active_seat")
        wrong_seat = (active_seat + 1) % 4
        controlled = self.app.control(
            wrong_seat,
            self.room_id,
            field(self.revealed, "revision"),
        )
        before = self.app.state(self.room_id)

        with self.assertRaises(ValueError):
            self.app.skip_action(
                wrong_seat,
                self.room_id,
                field(controlled, "revision"),
            )

        self.assertEqual(before, self.app.state(self.room_id))
        advanced = self.app.skip_action(
            active_seat,
            self.room_id,
            field(controlled, "revision"),
        )
        self.assertIn(active_seat, field(advanced, "public")["resolved_seats"])

    def test_skip_advances_initiative_and_fourth_skip_enters_next_turn(self) -> None:
        payload = self.revealed
        resolved = []
        for index in range(4):
            payload = resolve_pending_initiative(self.app, self.room_id, payload)
            seat = field(field(payload, "public"), "active_seat")
            resolved.append(seat)
            payload = skip_current_action(self.app, self.room_id, payload)
            public = field(payload, "public")
            if index < 3:
                self.assertEqual(resolved, list(field(public, "resolved_seats")))
                self.assertEqual("card_resolution", field(public, "phase"))
            else:
                self.assertEqual("card_selection", field(public, "phase"))
                self.assertNotIn("active_seat", public)
                self.assertEqual(2, field(public, "turn"))

    def test_decision_coin_flips_when_cross_team_tie_is_consumed(self) -> None:
        payload = self.revealed
        initial_coin = field(field(payload, "public"), "decision_coin")
        payload = skip_current_action(self.app, self.room_id, payload)
        coin = field(field(payload, "public"), "decision_coin")
        self.assertNotEqual(initial_coin, coin)

    def test_team_captain_selects_between_equal_initiative_teammates(self) -> None:
        from dataclasses import replace
        from goa2.domain import PendingInitiativeChoice, Seat, Team

        room = self.app._rooms.read(self.room_id)
        state = replace(
            room.state,
            active_seat=None,
            controlled_seat=Seat.ZERO,
            initiative_order=(Seat.ZERO, Seat.TWO, Seat.ONE, Seat.THREE),
            pending_initiative_choice=PendingInitiativeChoice(
                choice_id="initiative-blue-10",
                team=Team.BLUE,
                chooser=Seat.ZERO,
                candidate_seats=(Seat.ZERO, Seat.TWO),
                initiative=10,
            ),
        )
        self.app._rooms.transact(
            self.room_id,
            lambda current: replace(current, state=state),
        )
        payload = self.app.state(self.room_id)
        self.assertIn(
            "resolve_initiative_choice",
            field(payload, "private_view")["legal_actions"],
        )

        payload = self.app.resolve_initiative_choice(
            0,
            "initiative-blue-10",
            2,
            self.room_id,
            field(payload, "revision"),
        )

        public = field(payload, "public")
        self.assertEqual(2, field(public, "active_seat"))
        self.assertEqual(2, list(field(public, "initiative_order"))[0])
        self.assertIsNone(public["pending_initiative_choice"])

    def test_non_active_seat_is_rejected_atomically(self) -> None:
        active_seat = field(field(self.revealed, "public"), "active_seat")
        wrong_seat = (active_seat + 1) % 4
        controlled = self.app.control(
            wrong_seat,
            self.room_id,
            field(self.revealed, "revision"),
        )
        before = self.app.state(self.room_id)

        with self.assertRaises(ValueError):
            self.app.skip_action(
                wrong_seat,
                self.room_id,
                field(controlled, "revision"),
            )

        self.assertEqual(before, self.app.state(self.room_id))

    def test_resolution_progress_round_trips_through_json(self) -> None:
        from goa2.domain import RoomState

        active_seat = field(field(self.revealed, "public"), "active_seat")
        payload = self.revealed
        if field(payload, "controlled_seat") != active_seat:
            payload = self.app.control(
                active_seat,
                self.room_id,
                field(payload, "revision"),
            )
        self.app.skip_action(
            active_seat,
            self.room_id,
            field(payload, "revision"),
        )
        state = self.app._rooms.read(self.room_id).state

        self.assertEqual(state, RoomState.from_dict(state.to_dict()))

    def test_server_profiles_primary_and_movement_choices(self) -> None:
        from dataclasses import replace

        active_seat = field(field(self.revealed, "public"), "active_seat")
        room = self.app._rooms.read(self.room_id)
        units = list(room.state.units)
        units[active_seat] = replace(units[active_seat], x=0, y=0)
        target_seat = (active_seat + 1) % 4
        units[target_seat] = replace(units[target_seat], x=1, y=0)
        self.app._rooms.transact(
            self.room_id,
            lambda current: replace(
                current, state=replace(current.state, units=tuple(units))
            ),
        )
        self.revealed = self.app.state(self.room_id)
        payload = self.revealed
        if field(payload, "controlled_seat") != active_seat:
            payload = self.app.control(
                active_seat, self.room_id, field(payload, "revision")
            )
        private = field(payload, "private_view")
        options = field(private, "action_options")

        self.assertIn({"kind": "primary", "source_slot": None}, options)
        chosen = self.app.choose_action(
            active_seat,
            options[0]["kind"],
            options[0]["source_slot"],
            self.room_id,
            field(payload, "revision"),
        )
        self.assertEqual(options[0], field(chosen, "private_view")["pending_action"])

    def test_primary_movement_profile_excludes_secondary_movement(self) -> None:
        from goa2.application.catalog import CatalogLoader

        catalog = CatalogLoader().load()
        movement_card = next(
            card
            for hero in catalog.cards["heroes"]
            for card in hero["cards"]
            if card["primary_action"]["family"] == "movement"
        )
        profile = catalog.action_profile(movement_card["id"])

        self.assertTrue(profile["primary_is_movement"])
        self.assertFalse(profile["secondary_movement"])
        self.assertEqual(["primary"], profile["fast_move_sources"])

    def test_data_only_primary_can_complete_and_advance_queue(self) -> None:
        from dataclasses import replace

        active_seat = field(field(self.revealed, "public"), "active_seat")
        room = self.app._rooms.read(self.room_id)
        seats = list(room.state.seats)
        seats[active_seat] = replace(
            seats[active_seat],
            action_profile=replace(
                seats[active_seat].action_profile,
                primary_family="skill",
                primary_category="攻击",
            ),
        )
        self.app._rooms.transact(
            self.room_id,
            lambda current: replace(
                current, state=replace(current.state, seats=tuple(seats))
            ),
        )
        self.revealed = self.app.state(self.room_id)
        payload = self.revealed
        if field(payload, "controlled_seat") != active_seat:
            payload = self.app.control(
                active_seat, self.room_id, field(payload, "revision")
            )
        chosen = self.app.choose_action(
            active_seat,
            "primary",
            None,
            self.room_id,
            field(payload, "revision"),
        )

        self.assertIn(
            "complete_action",
            field(chosen, "private_view")["legal_actions"],
        )
        from goa2.domain import CompletePendingAction, Seat
        from goa2.engine import reduce_room

        reduction = reduce_room(
            self.app._rooms.read(self.room_id).state,
            CompletePendingAction(
                expected_revision=field(chosen, "revision"),
                seat=Seat(active_seat),
            ),
        )
        event = reduction.events[-1].to_dict()
        self.assertEqual("data_only_action_completed", event["type"])
        self.assertEqual(active_seat, event["seat"])
        self.assertTrue(event["card_id"])
        self.assertTrue(event["primary_family"])

        completed = self.app.complete_action(
            active_seat,
            self.room_id,
            field(chosen, "revision"),
        )

        public = field(completed, "public")
        self.assertIn(active_seat, field(public, "resolved_seats"))

    def test_movement_primary_cannot_be_completed_without_destination(self) -> None:
        from dataclasses import replace
        from goa2.domain import ActionChoice, ActionProfile, Seat

        active_seat = field(field(self.revealed, "public"), "active_seat")
        room = self.app._rooms.read(self.room_id)
        seats = list(room.state.seats)
        seats[active_seat] = replace(
            seats[active_seat],
            action_profile=ActionProfile(
                implementation_status="data_only",
                primary_family="movement",
                primary_category="移动",
                primary_is_movement=True,
                secondary_movement=False,
                fast_move_sources=("primary",),
                primary_value=3,
                secondary_movement_value=None,
            ),
        )
        self.app._rooms.transact(
            self.room_id,
            lambda current: replace(
                current,
                state=replace(
                    current.state,
                    controlled_seat=Seat(active_seat),
                    seats=tuple(seats),
                    pending_action=ActionChoice("primary", None),
                    pending_movement=3,
                ),
            ),
        )
        before = self.app.state(self.room_id)

        with self.assertRaises(ValueError):
            self.app.complete_action(
                active_seat,
                self.room_id,
                field(before, "revision"),
            )

        self.assertEqual(before, self.app.state(self.room_id))

    def test_implemented_primary_cannot_use_data_only_completion(self) -> None:
        from dataclasses import replace
        from goa2.domain import ActionChoice, Seat

        active_seat = field(field(self.revealed, "public"), "active_seat")
        room = self.app._rooms.read(self.room_id)
        seats = list(room.state.seats)
        seats[active_seat] = replace(
            seats[active_seat],
            action_profile=replace(
                seats[active_seat].action_profile,
                implementation_status="behavior_tested",
            ),
        )
        self.app._rooms.transact(
            self.room_id,
            lambda current: replace(
                current,
                state=replace(
                    current.state,
                    controlled_seat=Seat(active_seat),
                    seats=tuple(seats),
                    pending_action=ActionChoice("primary", None),
                ),
            ),
        )
        before = self.app.state(self.room_id)
        self.assertNotIn(
            "complete_action",
            field(before, "private_view")["legal_actions"],
        )

        with self.assertRaises(ValueError):
            self.app.complete_action(
                active_seat,
                self.room_id,
                field(before, "revision"),
            )

        self.assertEqual(before, self.app.state(self.room_id))


if __name__ == "__main__":
    unittest.main()
