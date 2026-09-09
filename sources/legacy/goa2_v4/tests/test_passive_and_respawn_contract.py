from __future__ import annotations

from dataclasses import replace
import unittest
from uuid import uuid4

from tests.contract_support import field


class PassiveAndRespawnContractTests(unittest.TestCase):
    def setUp(self) -> None:
        from goa2.backend.bootstrap import build_application

        self.app = build_application()
        self.room_id = uuid4().hex
        payload = self.app.state(self.room_id)
        self.payload = self.app.force_confirm(
            self.room_id,
            field(payload, "revision"),
        )

    def test_passives_modify_initiative_attack_defense_and_movement(self) -> None:
        from goa2.engine import modified_value

        seat = replace(
            self.app._rooms.read(self.room_id).state.seats[0],
            passive_bonuses=(2, 3, 4, 5, 6, 7),
        )

        self.assertEqual(12, modified_value(seat, "attack", 10))
        self.assertEqual(13, modified_value(seat, "defense", 10))
        self.assertEqual(14, modified_value(seat, "movement", 10))
        self.assertEqual(15, modified_value(seat, "initiative", 10))
        self.assertEqual(16, modified_value(seat, "range", 10))
        self.assertEqual(17, modified_value(seat, "ranged", 10))

    def test_initiative_passive_changes_reveal_order(self) -> None:
        from goa2.backend.bootstrap import build_application

        app = build_application()
        room_id = uuid4().hex
        payload = app.state(room_id)
        room = app._rooms.read(room_id)
        seats = list(room.state.seats)
        bonuses = list(seats[1].passive_bonuses)
        bonuses[3] = 2
        seats[1] = replace(seats[1], passive_bonuses=tuple(bonuses))
        app._rooms.transact(
            room_id,
            lambda current: replace(
                current,
                state=replace(current.state, seats=tuple(seats)),
            ),
        )

        revealed = app.force_confirm(room_id, field(payload, "revision"))

        self.assertEqual(1, field(revealed, "public")["active_seat"])
        self.assertEqual(
            13,
            app._rooms.read(room_id).state.revealed_initiatives[1],
        )
        revealed_card = next(
            card
            for card in field(revealed, "public")["revealed_cards"]
            if card["seat"] == 1
        )
        self.assertEqual(13, revealed_card["initiative"])

    def test_movement_passive_increases_pending_budget(self) -> None:
        room = self.app._rooms.read(self.room_id)
        active = room.state.active_seat
        seats = list(room.state.seats)
        bonuses = list(seats[int(active)].passive_bonuses)
        bonuses[2] = 2
        seats[int(active)] = replace(
            seats[int(active)],
            passive_bonuses=tuple(bonuses),
        )
        self.app._rooms.transact(
            self.room_id,
            lambda current: replace(
                current,
                state=replace(current.state, seats=tuple(seats)),
            ),
        )
        payload = self.app.state(self.room_id)
        if field(payload, "controlled_seat") != int(active):
            payload = self.app.control(
                int(active), self.room_id, field(payload, "revision")
            )
        option = next(
            option
            for option in field(payload, "private_view")["action_options"]
            if option["kind"] == "secondary_movement"
        )
        base = seats[int(active)].action_profile.secondary_movement_value

        self.app.choose_action(
            int(active),
            option["kind"],
            option["source_slot"],
            self.room_id,
            field(payload, "revision"),
        )

        self.assertEqual(
            base + 2,
            self.app._rooms.read(self.room_id).state.pending_movement,
        )

    def test_defeated_hero_chooses_spawn_when_its_card_becomes_active(self) -> None:
        from goa2.domain import Seat
        from goa2.engine import reduce_room

        room = self.app._rooms.read(self.room_id)
        target = Seat(room.state.initiative_order[1])
        seats = list(room.state.seats)
        seats[int(target)] = replace(seats[int(target)], defeated=True)
        units = list(room.state.units)
        units[int(target)] = replace(
            units[int(target)],
            x=10000 + int(target),
            y=10000,
        )
        state = replace(room.state, seats=tuple(seats), units=tuple(units))
        self.app._rooms.transact(
            self.room_id,
            lambda current: replace(current, state=state),
        )
        payload = self.app.state(self.room_id)
        for _ in range(8):
            public = field(payload, "public")
            if public.get("active_seat") == int(target):
                break
            choice = public.get("pending_initiative_choice")
            if choice is not None:
                chooser = choice["chooser"]
                if field(payload, "controlled_seat") != chooser:
                    payload = self.app.control(
                        chooser, self.room_id, field(payload, "revision")
                    )
                candidates = choice["candidate_seats"]
                chosen = int(target) if int(target) in candidates else candidates[0]
                payload = self.app.resolve_initiative_choice(
                    chooser,
                    choice["choice_id"],
                    chosen,
                    self.room_id,
                    field(payload, "revision"),
                )
                continue
            active = public["active_seat"]
            if field(payload, "controlled_seat") != active:
                payload = self.app.control(
                    active, self.room_id, field(payload, "revision")
                )
            payload = self.app.skip_action(
                active, self.room_id, field(payload, "revision")
            )
        else:
            self.fail("defeated hero did not become active")

        public = field(payload, "public")
        self.assertEqual(int(target), public["active_seat"])
        self.assertIsNotNone(public["pending_respawn"])
        state = self.app._rooms.read(self.room_id).state
        self.assertEqual(state, type(state).from_dict(state.to_dict()))
        private = field(payload, "private_view")
        self.assertEqual(["resolve_respawn"], private["legal_actions"])
        destination = private["respawn_targets"][0]

        with self.assertRaises(ValueError):
            self.app.resolve_respawn(
                int(target),
                -999,
                -999,
                self.room_id,
                field(payload, "revision"),
            )
        self.assertIsNotNone(
            self.app._rooms.read(self.room_id).state.pending_respawn
        )
        with self.assertRaises(ValueError):
            self.app.skip_action(
                int(target),
                self.room_id,
                field(payload, "revision"),
            )

        respawned = self.app.resolve_respawn(
            int(target),
            destination["x"],
            destination["y"],
            self.room_id,
            field(payload, "revision"),
        )

        self.assertFalse(
            self.app._rooms.read(self.room_id).state.seats[int(target)].defeated
        )
        self.assertIn("choose_action", field(respawned, "private_view")["legal_actions"])


if __name__ == "__main__":
    unittest.main()
