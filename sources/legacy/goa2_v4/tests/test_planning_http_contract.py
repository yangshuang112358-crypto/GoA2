from __future__ import annotations

import json
from threading import Thread
import unittest
from urllib.error import HTTPError
from urllib.request import Request, urlopen
from uuid import uuid4

from tests.contract_support import field


class PlanningHttpContractTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        from goa2.backend.bootstrap import build_application
        from goa2.backend.http import GoA2RequestHandler, create_server

        cls.original_log_message = GoA2RequestHandler.log_message
        GoA2RequestHandler.log_message = lambda *args, **kwargs: None
        cls.handler_class = GoA2RequestHandler
        cls.application = build_application()
        cls.server = create_server(cls.application, port=0)
        cls.thread = Thread(target=cls.server.serve_forever, daemon=True)
        cls.thread.start()
        host, port = cls.server.server_address
        cls.base_url = f"http://{host}:{port}"

    @classmethod
    def tearDownClass(cls) -> None:
        cls.server.shutdown()
        cls.server.server_close()
        cls.thread.join(timeout=5)
        cls.handler_class.log_message = cls.original_log_message

    def request_json(
        self,
        path: str,
        body: dict | None = None,
    ) -> tuple[int, dict]:
        data = None if body is None else json.dumps(body).encode("utf-8")
        request = Request(
            self.base_url + path,
            data=data,
            headers={"content-type": "application/json"},
            method="GET" if body is None else "POST",
        )
        try:
            with urlopen(request, timeout=5) as response:
                return response.status, json.loads(response.read().decode("utf-8"))
        except HTTPError as exc:
            return exc.code, json.loads(exc.read().decode("utf-8"))

    def new_room(self) -> tuple[str, dict]:
        room_id = uuid4().hex
        status, payload = self.request_json(f"/api/state?room_id={room_id}")
        self.assertEqual(200, status)
        return room_id, payload

    def test_four_planning_endpoints_complete_selection_and_reveal(self) -> None:
        room_id, initial = self.new_room()
        card_id = field(initial, "private_view")["hand"][0]
        _, catalog = self.request_json("/api/catalog")
        expected_cards = []
        for hero in field(catalog, "heroes")[:4]:
            initial_hand = [
                card
                for card in field(hero, "cards")
                if field(card, "color_key") in {"gold", "silver"}
                or (
                    field(card, "color_key") in {"red", "green", "blue"}
                    and field(card, "level") == 1
                )
            ]
            expected_cards.append(field(initial_hand[0], "id"))
        self.assertEqual(expected_cards[0], card_id)

        status, selected = self.request_json("/api/cards/select", {
            "room_id": room_id,
            "seat": 0,
            "card_id": card_id,
            "expected_revision": field(initial, "revision"),
        })
        self.assertEqual(200, status)
        self.assertEqual(card_id, field(selected, "private_view")["selected_card_id"])

        status, confirmed = self.request_json("/api/cards/confirm", {
            "room_id": room_id,
            "seat": 0,
            "expected_revision": field(selected, "revision"),
        })
        self.assertEqual(200, status)
        self.assertTrue(field(confirmed, "private_view")["confirmed"])

        status, ready = self.request_json("/api/debug/force-confirm", {
            "room_id": room_id,
            "expected_revision": field(confirmed, "revision"),
        })
        self.assertEqual(200, status)
        self.assertEqual("card_resolution", field(field(ready, "public"), "phase"))
        self.assertGreater(field(ready, "revision"), field(confirmed, "revision"))
        self.assertTrue(all(
            field(seat, "confirmed") for seat in field(field(ready, "public"), "seats")
        ))

        public = field(ready, "public")
        self.assertEqual("card_resolution", field(public, "phase"))
        self.assertEqual(4, len(field(public, "initiative_order")))
        self.assertEqual(expected_cards, [
            field(seat, "revealed_card_id") for seat in field(public, "seats")
        ])

    def test_stale_http_selection_is_conflict_and_changes_nothing(self) -> None:
        room_id, initial = self.new_room()
        hand = field(initial, "private_view")["hand"]
        _, selected = self.request_json("/api/cards/select", {
            "room_id": room_id,
            "seat": 0,
            "card_id": hand[0],
            "expected_revision": field(initial, "revision"),
        })

        status, error = self.request_json("/api/cards/select", {
            "room_id": room_id,
            "seat": 0,
            "card_id": hand[1],
            "expected_revision": field(initial, "revision"),
        })
        self.assertEqual(409, status)
        self.assertEqual("revision_conflict", field(field(error, "error"), "code"))

        _, after = self.request_json(f"/api/state?room_id={room_id}")
        self.assertEqual(selected, after)

    def test_skip_action_endpoint_advances_active_seat(self) -> None:
        room_id, payload = self.new_room()
        status, payload = self.request_json("/api/debug/force-confirm", {
            "room_id": room_id,
            "expected_revision": field(payload, "revision"),
        })
        self.assertEqual(200, status)
        public = field(payload, "public")
        active_seat = field(public, "active_seat")
        if field(payload, "controlled_seat") != active_seat:
            status, payload = self.request_json("/api/debug/control-seat", {
                "room_id": room_id,
                "seat": active_seat,
                "expected_revision": field(payload, "revision"),
            })
            self.assertEqual(200, status)
        status, payload = self.request_json("/api/actions/skip", {
            "room_id": room_id,
            "seat": active_seat,
            "expected_revision": field(payload, "revision"),
        })

        self.assertEqual(200, status)
        public = field(payload, "public")
        choice = public["pending_initiative_choice"]
        self.assertEqual(
            [active_seat],
            list(field(public, "resolved_seats")),
        )
        if choice is None:
            self.assertIn("active_seat", public)
            return
        chooser = choice["chooser"]
        if field(payload, "controlled_seat") != chooser:
            status, payload = self.request_json("/api/debug/control-seat", {
                "room_id": room_id,
                "seat": chooser,
                "expected_revision": field(payload, "revision"),
            })
            self.assertEqual(200, status)
        chosen = choice["candidate_seats"][0]
        status, payload = self.request_json("/api/actions/initiative-choice", {
            "room_id": room_id,
            "seat": chooser,
            "choice_id": choice["choice_id"],
            "chosen_seat": chosen,
            "expected_revision": field(payload, "revision"),
        })
        self.assertEqual(200, status)
        self.assertEqual(chosen, field(field(payload, "public"), "active_seat"))

    def test_debug_round_end_and_minion_removal_routes(self) -> None:
        room_id, payload = self.new_room()
        status, payload = self.request_json("/api/debug/enter-round-end", {
            "room_id": room_id,
            "expected_revision": field(payload, "revision"),
        })
        self.assertEqual(200, status)
        self.assertEqual("round_end", field(field(payload, "public"), "phase"))
        target = field(field(payload, "public"), "minions")[0]

        status, payload = self.request_json("/api/debug/remove-minion", {
            "room_id": room_id,
            "seat": 0,
            "minion_id": target["unit_id"],
            "expected_revision": field(payload, "revision"),
        })
        self.assertEqual(200, status)
        self.assertEqual(11, len(field(field(payload, "public"), "minions")))

        status, payload = self.request_json("/api/debug/begin-round-end", {
            "room_id": room_id,
            "expected_revision": field(payload, "revision"),
        })
        self.assertEqual(200, status)
        choice = field(field(payload, "public"), "pending_captain_choice")
        self.assertEqual("remove_minions", choice["kind"])

        status, payload = self.request_json("/api/round-end/captain-choice", {
            "room_id": room_id,
            "seat": choice["chooser"],
            "choice_id": choice["choice_id"],
            "candidate_id": choice["candidate_ids"][0],
            "expected_revision": field(payload, "revision"),
        })
        self.assertEqual(200, status)

    def test_basic_attack_route_is_registered(self) -> None:
        room_id, payload = self.new_room()

        status, error = self.request_json("/api/actions/attack", {
            "room_id": room_id,
            "seat": 0,
            "target_id": "missing-unit",
            "expected_revision": field(payload, "revision"),
        })

        self.assertEqual(400, status)
        self.assertEqual("invalid_request", field(field(error, "error"), "code"))

        status, error = self.request_json("/api/actions/defend", {
            "room_id": room_id,
            "seat": 0,
            "card_id": None,
            "expected_revision": field(payload, "revision"),
        })
        self.assertEqual(400, status)
        self.assertEqual("invalid_request", field(field(error, "error"), "code"))

    def test_respawn_and_upgrade_routes_return_json_in_desktop_server(self) -> None:
        from dataclasses import replace
        from goa2.domain import PendingRespawn, Phase, Seat

        respawn_room, _ = self.new_room()
        room = self.application._rooms.read(respawn_room)
        spawn = next(
            cell for cell in room.state.board
            if cell.state == "blueHeroSpawn"
        )
        seats = list(room.state.seats)
        seats[0] = replace(seats[0], defeated=True)
        units = list(room.state.units)
        units[0] = replace(units[0], x=10000, y=10000)
        respawn_state = replace(
            room.state,
            phase=Phase.CARD_RESOLUTION,
            controlled_seat=Seat.ZERO,
            active_seat=Seat.ZERO,
            initiative_order=(Seat.ZERO, Seat.ONE, Seat.TWO, Seat.THREE),
            resolved_seats=(),
            seats=tuple(seats),
            units=tuple(units),
            pending_respawn=PendingRespawn(Seat.ZERO, ((spawn.x, spawn.y),)),
        )
        self.application._rooms.transact(
            respawn_room,
            lambda current: replace(current, state=respawn_state),
        )
        payload = self.application.state(respawn_room)
        status, payload = self.request_json("/api/actions/respawn", {
            "room_id": respawn_room,
            "seat": 0,
            "x": spawn.x,
            "y": spawn.y,
            "expected_revision": field(payload, "revision"),
        })
        self.assertEqual(200, status)
        self.assertIsNone(field(payload, "public")["pending_respawn"])

        upgrade_room, _ = self.new_room()
        room = self.application._rooms.read(upgrade_room)
        seat = replace(
            room.state.seats[0],
            hero_level=2,
            pending_upgrade_choices=1,
        )
        upgrade_state = replace(
            room.state,
            phase=Phase.UPGRADE,
            controlled_seat=Seat.ZERO,
            seats=(seat, *room.state.seats[1:]),
            pending_upgrade=None,
        )
        self.application._rooms.transact(
            upgrade_room,
            lambda current: replace(current, state=upgrade_state),
        )
        payload = self.application.state(upgrade_room)
        option = field(payload, "private_view")["upgrade_options"][0]
        status, payload = self.request_json("/api/upgrade", {
            "room_id": upgrade_room,
            "seat": 0,
            "color": option["color"],
            "card_id": option["candidates"][0]["card_id"],
            "expected_revision": field(payload, "revision"),
        })
        self.assertEqual(200, status)
        self.assertEqual("card_selection", field(payload, "public")["phase"])

    def test_join_token_controls_private_projection_and_actor_seat(self) -> None:
        room_id = uuid4().hex
        status, joined = self.request_json("/api/rooms/join", {
            "room_id": room_id,
            "seat": 1,
        })
        self.assertEqual(200, status)
        token = joined["token"]

        status, state = self.request_json(
            f"/api/state?room_id={room_id}&token={token}"
        )
        self.assertEqual(200, status)
        self.assertEqual(1, field(state, "controlled_seat"))
        card_id = field(state, "private_view")["hand"][0]

        status, selected = self.request_json("/api/cards/select", {
            "room_id": room_id,
            "token": token,
            "seat": 3,
            "card_id": card_id,
            "expected_revision": field(state, "revision"),
        })
        self.assertEqual(200, status)
        self.assertEqual(1, field(selected, "controlled_seat"))
        self.assertEqual(
            card_id,
            field(selected, "private_view")["selected_card_id"],
        )

        status, error = self.request_json("/api/cards/confirm", {
            "room_id": room_id,
            "seat": 1,
            "expected_revision": field(selected, "revision"),
        })
        self.assertEqual(400, status)
        self.assertIn("token", field(field(error, "error"), "message"))

    def test_debug_skip_routes_and_room_configuration(self) -> None:
        room_id, initial = self.new_room()
        _, catalog = self.request_json("/api/catalog")
        hero_ids = [
            field(hero, "hero_id")
            for hero in field(catalog, "heroes")[2:6]
        ]
        status, configured = self.request_json("/api/rooms/configure", {
            "room_id": room_id,
            "expected_revision": field(initial, "revision"),
            "starting_crystal_life": 9,
            "frontline_victory_marks": 4,
            "hero_ids": hero_ids,
        })
        self.assertEqual(200, status)
        self.assertEqual(
            hero_ids,
            field(configured, "public")["room_config"]["hero_ids"],
        )

        status, revealed = self.request_json("/api/debug/force-confirm", {
            "room_id": room_id,
            "expected_revision": field(configured, "revision"),
        })
        self.assertEqual(200, status)
        status, skipped = self.request_json("/api/debug/skip-current", {
            "room_id": room_id,
            "expected_revision": field(revealed, "revision"),
        })
        self.assertEqual(200, status)
        status, completed = self.request_json("/api/debug/skip-all", {
            "room_id": room_id,
            "expected_revision": field(skipped, "revision"),
        })
        self.assertEqual(200, status)
        self.assertEqual("card_selection", field(completed, "public")["phase"])

    def test_debug_resource_routes_use_domain_commands(self) -> None:
        room_id, initial = self.new_room()
        status, changed = self.request_json("/api/debug/set-coins", {
            "room_id": room_id,
            "expected_revision": field(initial, "revision"),
            "seat": 2,
            "coins": 17,
        })
        self.assertEqual(200, status)
        self.assertEqual(17, field(changed, "public")["seats"][2]["coins"])

        status, reset = self.request_json("/api/debug/reset-minions", {
            "room_id": room_id,
            "expected_revision": field(changed, "revision"),
        })
        self.assertEqual(200, status)
        self.assertEqual(12, len(field(reset, "public")["minions"]))

    def test_claimed_room_requires_token_for_debug_tools(self) -> None:
        room_id = uuid4().hex
        status, joined = self.request_json("/api/rooms/join", {
            "room_id": room_id,
            "seat": 0,
        })
        self.assertEqual(200, status)

        status, error = self.request_json("/api/debug/force-confirm", {
            "room_id": room_id,
            "expected_revision": field(joined["state"], "revision"),
        })
        self.assertEqual(400, status)
        self.assertIn("token", field(field(error, "error"), "message"))

        status, _ = self.request_json("/api/debug/force-confirm", {
            "room_id": room_id,
            "token": joined["token"],
            "expected_revision": field(joined["state"], "revision"),
        })
        self.assertEqual(200, status)

    def test_any_claimed_seat_can_debug_skip_the_active_card(self) -> None:
        room_id = uuid4().hex
        joined_by_seat = {}
        for seat in range(4):
            status, joined = self.request_json("/api/rooms/join", {
                "room_id": room_id,
                "seat": seat,
            })
            self.assertEqual(200, status)
            joined_by_seat[seat] = joined

        status, revealed = self.request_json("/api/debug/force-confirm", {
            "room_id": room_id,
            "token": joined_by_seat[0]["token"],
            "expected_revision": field(joined_by_seat[0]["state"], "revision"),
        })
        self.assertEqual(200, status)
        active_seat = field(revealed, "public")["active_seat"]
        caller_seat = next(seat for seat in range(4) if seat != active_seat)

        status, skipped = self.request_json("/api/debug/skip-current", {
            "room_id": room_id,
            "token": joined_by_seat[caller_seat]["token"],
            "expected_revision": field(revealed, "revision"),
        })

        self.assertEqual(200, status)
        self.assertIn(active_seat, field(skipped, "public")["resolved_seats"])

    def test_each_active_player_token_receives_its_action_options(self) -> None:
        room_id = uuid4().hex
        joined_by_seat = {}
        for seat in range(4):
            status, joined = self.request_json("/api/rooms/join", {
                "room_id": room_id,
                "seat": seat,
            })
            self.assertEqual(200, status)
            joined_by_seat[seat] = joined

        status, revealed = self.request_json("/api/debug/force-confirm", {
            "room_id": room_id,
            "token": joined_by_seat[0]["token"],
            "expected_revision": field(joined_by_seat[0]["state"], "revision"),
        })
        self.assertEqual(200, status)

        for _ in range(2):
            initiative_choice = field(revealed, "public").get(
                "pending_initiative_choice"
            )
            if initiative_choice is not None:
                chooser = initiative_choice["chooser"]
                status, revealed = self.request_json(
                    "/api/actions/initiative-choice",
                    {
                        "room_id": room_id,
                        "token": joined_by_seat[chooser]["token"],
                        "choice_id": initiative_choice["choice_id"],
                        "chosen_seat": initiative_choice["candidate_seats"][0],
                        "expected_revision": field(revealed, "revision"),
                    },
                )
                self.assertEqual(200, status)
            active_seat = field(revealed, "public")["active_seat"]
            token = joined_by_seat[active_seat]["token"]
            status, active_view = self.request_json(
                f"/api/state?room_id={room_id}&token={token}"
            )
            self.assertEqual(200, status)
            private = field(active_view, "private_view")
            self.assertIn("skip_action", private["legal_actions"])
            self.assertIn("choose_action", private["legal_actions"])
            self.assertTrue(private["action_options"])

            status, revealed = self.request_json("/api/actions/skip", {
                "room_id": room_id,
                "token": token,
                "expected_revision": field(active_view, "revision"),
            })
            self.assertEqual(200, status)


if __name__ == "__main__":
    unittest.main()
