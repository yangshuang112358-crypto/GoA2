from __future__ import annotations

import unittest
from uuid import uuid4

from tests.contract_support import field


class PlanningContractTests(unittest.TestCase):
    def setUp(self) -> None:
        from goa2.application.catalog import CatalogLoader
        from goa2.backend.bootstrap import build_application

        self.catalog = CatalogLoader().load()
        self.app = build_application()
        self.room_id = uuid4().hex
        self.initial = self.app.state(self.room_id)

    def hand(self, seat: int) -> tuple[str, ...]:
        return self.catalog.initial_hand_card_ids(seat)

    def control(self, payload: dict, seat: int) -> dict:
        if field(payload, "controlled_seat") == seat:
            return payload
        return self.app.control(
            seat,
            self.room_id,
            field(payload, "revision"),
        )

    def select(self, payload: dict, seat: int, card_id: str) -> dict:
        return self.app.select_card(
            seat,
            card_id,
            self.room_id,
            field(payload, "revision"),
        )

    def confirm(self, payload: dict, seat: int) -> dict:
        return self.app.confirm_selection(
            seat,
            self.room_id,
            field(payload, "revision"),
        )

    def plan_all(self) -> dict:
        payload = self.initial
        for seat in range(4):
            payload = self.control(payload, seat)
            payload = self.select(payload, seat, self.hand(seat)[0])
            payload = self.confirm(payload, seat)
        return payload

    def test_initial_state_is_card_selection_with_fixed_five_card_hands(self) -> None:
        from goa2.domain import create_initial_room_state

        public = field(self.initial, "public")
        seats = list(field(public, "seats"))
        domain_seats = list(field(create_initial_room_state(), "seats"))

        self.assertEqual("card_selection", field(public, "phase"))
        self.assertEqual(["wasp", "shargatha", "brogan", "arien"], [
            field(seat, "hero_id") for seat in seats
        ])
        self.assertTrue(all("hand_size" not in seat for seat in seats))
        self.assertTrue(all(not field(seat, "selected") for seat in seats))
        self.assertTrue(all(not field(seat, "confirmed") for seat in seats))
        self.assertTrue(all(len(self.hand(seat)) == 5 for seat in range(4)))
        self.assertEqual(5, len(field(self.initial, "private_view")["hand"]))
        self.assertIsNone(field(self.initial, "private_view")["selected_card_id"])
        self.assertEqual(
            ["select_card"],
            field(self.initial, "private_view")["legal_actions"],
        )
        self.assertTrue(all(
            field(seat, "selected_card_id") is None
            and not field(seat, "confirmed")
            and field(seat, "revealed_card_id") is None
            for seat in domain_seats
        ))

    def test_legal_selection_succeeds_and_non_hand_card_fails_atomically(self) -> None:
        card_id = self.hand(0)[0]
        selected = self.select(self.initial, 0, card_id)

        self.assertEqual(card_id, field(selected, "private_view")["selected_card_id"])
        self.assertEqual(
            ["select_card", "confirm_selection"],
            field(selected, "private_view")["legal_actions"],
        )
        self.assertEqual(
            field(self.initial, "revision") + 1,
            field(selected, "revision"),
        )

        before = self.app.state(self.room_id)
        with self.assertRaises(ValueError):
            self.select(selected, 0, "not-in-hand")
        self.assertEqual(before, self.app.state(self.room_id))

    def test_planning_is_owned_by_seat_identity_not_debug_control(self) -> None:
        selected = self.select(self.initial, 1, self.hand(1)[0])
        seat_one_view = self.control(selected, 1)
        self.assertEqual(
            self.hand(1)[0],
            field(seat_one_view, "private_view")["selected_card_id"],
        )
        controlled_by_zero = self.control(seat_one_view, 0)
        confirmed = self.confirm(controlled_by_zero, 1)
        self.assertTrue(field(confirmed, "public")["seats"][1]["confirmed"])

    def test_confirmation_requires_selection_and_locks_the_card(self) -> None:
        with self.assertRaises(ValueError):
            self.confirm(self.initial, 0)

        confirmed = self.confirm(
            self.select(self.initial, 0, self.hand(0)[0]),
            0,
        )
        before = self.app.state(self.room_id)
        with self.assertRaises(ValueError):
            self.select(confirmed, 0, self.hand(0)[1])
        self.assertEqual(before, self.app.state(self.room_id))

    def test_fourth_confirmation_automatically_reveals_cards(self) -> None:
        payload = self.initial
        for seat in range(4):
            payload = self.control(payload, seat)
            payload = self.select(payload, seat, self.hand(seat)[0])
            payload = self.confirm(payload, seat)
            self.assertEqual(
                "card_resolution" if seat == 3 else "card_selection",
                field(field(payload, "public"), "phase"),
            )
        self.assertEqual(
            [self.hand(seat)[0] for seat in range(4)],
            [
                field(seat, "revealed_card_id")
                for seat in field(field(payload, "public"), "seats")
            ],
        )

    def test_reveal_atomically_publishes_four_cards_and_orders_initiative(self) -> None:
        initial_coin = field(field(self.initial, "public"), "decision_coin")
        ready = self.initial
        for seat in range(4):
            ready = self.control(ready, seat)
            ready = self.select(ready, seat, self.hand(seat)[0])
            if seat < 3:
                ready = self.confirm(ready, seat)
        self.assertTrue(all(
            "revealed_card_id" not in seat
            for seat in field(field(ready, "public"), "seats")
        ))

        revealed = self.confirm(ready, 3)
        public = field(revealed, "public")
        expected_cards = [self.hand(seat)[0] for seat in range(4)]

        self.assertEqual(
            [12, 11, 11, 11],
            [self.catalog.initiative(card_id) for card_id in expected_cards],
        )
        self.assertEqual("card_resolution", field(public, "phase"))
        self.assertEqual(expected_cards, [
            field(seat, "revealed_card_id") for seat in field(public, "seats")
        ])
        self.assertEqual(0, list(field(public, "initiative_order"))[0])
        self.assertEqual({1, 2, 3}, set(field(public, "initiative_order")[1:]))
        self.assertEqual(initial_coin, field(public, "decision_coin"))
        self.assertEqual(0, field(public, "active_seat"))
        self.assertEqual([], field(public, "legal_actions"))

    def test_four_selected_cards_do_not_reveal_without_confirmation(self) -> None:
        payload = self.initial
        for seat in range(4):
            payload = self.control(payload, seat)
            payload = self.select(payload, seat, self.hand(seat)[0])

        self.assertEqual([], field(payload, "public")["legal_actions"])
        before = self.app.state(self.room_id)
        with self.assertRaises(ValueError):
            self.app.reveal_cards(self.room_id, field(payload, "revision"))
        self.assertEqual(before, self.app.state(self.room_id))

    def test_force_confirm_and_reveal_fail_atomically_outside_legal_window(self) -> None:
        from goa2.application.service import RevisionConflict

        forced = self.app.force_confirm(
            self.room_id,
            field(self.initial, "revision"),
        )
        self.assertEqual(field(self.initial, "revision") + 2, field(forced, "revision"))
        self.assertEqual("card_resolution", field(forced, "public")["phase"])

        before = self.app.state(self.room_id)
        with self.assertRaises(ValueError):
            self.app.force_confirm(self.room_id, field(forced, "revision"))
        self.assertEqual(before, self.app.state(self.room_id))

        with self.assertRaises(RevisionConflict):
            self.app.reveal_cards(self.room_id, field(forced, "revision") - 1)
        self.assertEqual(before, self.app.state(self.room_id))

        before_repeat = self.app.state(self.room_id)
        with self.assertRaises(ValueError):
            self.app.reveal_cards(self.room_id, field(forced, "revision"))
        self.assertEqual(before_repeat, self.app.state(self.room_id))

    def test_pre_reveal_views_hide_selection_from_other_seats(self) -> None:
        card_id = self.hand(0)[0]
        selected = self.select(self.initial, 0, card_id)

        self.assertNotIn(card_id, repr(field(selected, "public")))
        self.assertEqual(card_id, field(selected, "private_view")["selected_card_id"])

        seat_one_view = self.control(selected, 1)
        self.assertNotIn(card_id, repr(field(seat_one_view, "public")))
        self.assertNotIn(card_id, repr(field(seat_one_view, "private_view")))
        self.assertTrue(all(
            color["status"] == "available"
            for color in field(field(selected, "public"), "seats")[0][
                "card_color_states"
            ][:5]
        ))

    def test_stale_selection_revision_fails_atomically(self) -> None:
        from goa2.application.service import RevisionConflict

        before = self.app.state(self.room_id)
        with self.assertRaises(RevisionConflict):
            self.app.select_card(
                0,
                self.hand(0)[0],
                self.room_id,
                field(self.initial, "revision") + 1,
            )
        self.assertEqual(before, self.app.state(self.room_id))


if __name__ == "__main__":
    unittest.main()
