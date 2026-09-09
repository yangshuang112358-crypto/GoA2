from __future__ import annotations

from dataclasses import replace
import unittest
from uuid import uuid4

from tests.contract_support import field


class ProgressionContractTests(unittest.TestCase):
    def setUp(self) -> None:
        from goa2.backend.bootstrap import build_application

        self.app = build_application()
        self.room_id = uuid4().hex
        self.payload = self.app.state(self.room_id)

    def test_round_end_auto_pays_levels_and_waits_for_all_pending_upgrades(self) -> None:
        from goa2.domain import Phase

        self.app._rooms.transact(
            self.room_id,
            lambda current: replace(
                current,
                state=replace(
                    current.state,
                    phase=Phase.ROUND_END,
                    seats=(
                        replace(current.state.seats[0], coins=1),
                        replace(current.state.seats[1], coins=1),
                        *current.state.seats[2:],
                    ),
                ),
            ),
        )
        payload = self.app.state(self.room_id)

        payload = self.app.begin_round_end(
            self.room_id,
            field(payload, "revision"),
        )

        public = field(payload, "public")
        self.assertEqual("upgrade", public["phase"])
        self.assertEqual(2, public["seats"][0]["level"])
        self.assertEqual(2, public["seats"][1]["level"])
        self.assertEqual(0, public["seats"][0]["coins"])
        self.assertEqual(0, public["seats"][1]["coins"])
        self.assertEqual([1, 1], [
            public["seats"][seat]["coins"] for seat in (2, 3)
        ])
        private = field(payload, "private_view")
        self.assertIn("choose_upgrade", private["legal_actions"])
        self.assertEqual(3, len(private["upgrade_options"]))
        payload = self.app.control(1, self.room_id, field(payload, "revision"))
        private = field(payload, "private_view")
        self.assertIn("choose_upgrade", private["legal_actions"])
        self.assertEqual(3, len(private["upgrade_options"]))

    def test_upgrade_choice_replaces_card_and_adds_unchosen_passive(self) -> None:
        from goa2.domain import Phase

        room = self.app._rooms.read(self.room_id)
        seat = replace(
            room.state.seats[0],
            hero_level=2,
            pending_upgrade_choices=1,
        )
        state = replace(
            room.state,
            phase=Phase.UPGRADE,
            seats=(seat, *room.state.seats[1:]),
            pending_upgrade=None,
        )
        self.app._rooms.transact(
            self.room_id,
            lambda current: replace(current, state=state),
        )
        payload = self.app.state(self.room_id)
        option = field(payload, "private_view")["upgrade_options"][0]
        candidate = option["candidates"][0]
        chosen = candidate["card_id"]
        self.assertIsNotNone(candidate["gained_passive"])
        old_hand = set(field(payload, "private_view")["hand"])

        completed = self.app.choose_upgrade(
            0,
            option["color"],
            chosen,
            self.room_id,
            field(payload, "revision"),
        )

        private = field(completed, "private_view")
        self.assertEqual("card_selection", field(completed, "public")["phase"])
        self.assertIn(chosen, private["hand"])
        self.assertNotEqual(old_hand, set(private["hand"]))
        seat_state = self.app._rooms.read(self.room_id).state.seats[0]
        self.assertEqual(2, seat_state.skill_levels[0])
        self.assertEqual(1, sum(seat_state.passive_bonuses))

    def test_players_choose_pending_upgrades_independently(self) -> None:
        from goa2.domain import Phase

        room = self.app._rooms.read(self.room_id)
        seats = list(room.state.seats)
        seats[0] = replace(
            seats[0],
            hero_level=2,
            pending_upgrade_choices=1,
        )
        seats[1] = replace(
            seats[1],
            hero_level=2,
            pending_upgrade_choices=1,
        )
        state = replace(
            room.state,
            phase=Phase.UPGRADE,
            seats=tuple(seats),
            pending_upgrade=None,
        )
        self.app._rooms.transact(
            self.room_id,
            lambda current: replace(current, state=state),
        )

        payload = self.app.state(self.room_id)
        seat_zero_option = field(payload, "private_view")["upgrade_options"][0]
        payload = self.app.control(1, self.room_id, field(payload, "revision"))
        seat_one_private = field(payload, "private_view")
        self.assertIn("choose_upgrade", seat_one_private["legal_actions"])
        seat_one_option = seat_one_private["upgrade_options"][0]

        after_one = self.app.choose_upgrade(
            1,
            seat_one_option["color"],
            seat_one_option["candidates"][0]["card_id"],
            self.room_id,
            field(payload, "revision"),
        )

        self.assertEqual("upgrade", field(after_one, "public")["phase"])
        persisted = self.app._rooms.read(self.room_id).state
        self.assertEqual(1, persisted.seats[0].pending_upgrade_choices)
        self.assertEqual(0, persisted.seats[1].pending_upgrade_choices)

        after_zero = self.app.choose_upgrade(
            0,
            seat_zero_option["color"],
            seat_zero_option["candidates"][0]["card_id"],
            self.room_id,
            field(after_one, "revision"),
        )

        self.assertEqual("card_selection", field(after_zero, "public")["phase"])

    def test_level_eight_unlocks_purple_passive_without_card_choice(self) -> None:
        from goa2.domain import Phase, RoomState

        room = self.app._rooms.read(self.room_id)
        seat = replace(
            room.state.seats[0],
            coins=7,
            hero_level=7,
            skill_levels=(3, 3, 3),
        )
        state = replace(
            room.state,
            phase=Phase.ROUND_END,
            seats=(seat, *room.state.seats[1:]),
        )
        self.app._rooms.transact(
            self.room_id,
            lambda current: replace(current, state=state),
        )

        completed = self.app.begin_round_end(
            self.room_id,
            state.revision,
        )

        public = field(completed, "public")
        self.assertEqual("card_selection", public["phase"])
        self.assertEqual(2, public["round"])
        self.assertEqual(8, public["seats"][0]["level"])
        self.assertEqual(0, public["seats"][0]["coins"])
        seat_state = self.app._rooms.read(self.room_id).state.seats[0]
        self.assertEqual(0, seat_state.pending_upgrade_choices)
        self.assertIsNone(self.app._rooms.read(self.room_id).state.pending_upgrade)
        self.assertEqual(
            "available",
            next(
                item["status"]
                for item in public["seats"][0]["card_color_states"]
                if item["color"] == "purple"
            ),
        )
        restored = RoomState.from_dict(
            self.app._rooms.read(self.room_id).state.to_dict()
        )
        self.assertEqual(8, restored.seats[0].hero_level)

    def test_level_eight_auto_unlock_does_not_block_other_upgrade_choices(self) -> None:
        from goa2.domain import Phase

        room = self.app._rooms.read(self.room_id)
        seats = list(room.state.seats)
        seats[0] = replace(
            seats[0],
            coins=7,
            hero_level=7,
            skill_levels=(3, 3, 3),
        )
        seats[1] = replace(seats[1], coins=1)
        state = replace(
            room.state,
            phase=Phase.ROUND_END,
            seats=tuple(seats),
        )
        self.app._rooms.transact(
            self.room_id,
            lambda current: replace(current, state=state),
        )

        pending = self.app.begin_round_end(
            self.room_id,
            state.revision,
        )

        public = field(pending, "public")
        self.assertEqual("upgrade", public["phase"])
        self.assertEqual(8, public["seats"][0]["level"])
        self.assertEqual(2, public["seats"][1]["level"])
        persisted = self.app._rooms.read(self.room_id).state
        self.assertEqual(0, persisted.seats[0].pending_upgrade_choices)
        self.assertEqual(1, persisted.seats[1].pending_upgrade_choices)
        self.assertIsNone(persisted.pending_upgrade)

    def test_paying_all_seven_levels_creates_only_six_card_choices(self) -> None:
        from goa2.domain import Phase

        room = self.app._rooms.read(self.room_id)
        state = replace(
            room.state,
            phase=Phase.ROUND_END,
            seats=(
                replace(room.state.seats[0], coins=sum(range(1, 8))),
                *room.state.seats[1:],
            ),
        )
        self.app._rooms.transact(
            self.room_id,
            lambda current: replace(current, state=state),
        )

        pending = self.app.begin_round_end(
            self.room_id,
            state.revision,
        )

        public = field(pending, "public")
        persisted = self.app._rooms.read(self.room_id).state
        self.assertEqual("upgrade", public["phase"])
        self.assertEqual(8, persisted.seats[0].hero_level)
        self.assertEqual(0, persisted.seats[0].coins)
        self.assertEqual(6, persisted.seats[0].pending_upgrade_choices)
        self.assertIsNone(persisted.pending_upgrade)


if __name__ == "__main__":
    unittest.main()
