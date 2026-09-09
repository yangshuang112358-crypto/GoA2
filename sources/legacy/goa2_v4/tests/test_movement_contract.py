from __future__ import annotations

import unittest
from uuid import uuid4

from tests.contract_support import field


class MovementContractTests(unittest.TestCase):
    def setUp(self) -> None:
        from goa2.backend.bootstrap import build_application

        self.app = build_application()
        self.room_id = uuid4().hex
        payload = self.app.state(self.room_id)
        payload = self.app.force_confirm(self.room_id, field(payload, "revision"))
        self.payload = payload

    def control_active(self) -> tuple[int, dict]:
        active = field(field(self.payload, "public"), "active_seat")
        payload = self.payload
        if field(payload, "controlled_seat") != active:
            payload = self.app.control(active, self.room_id, field(payload, "revision"))
        return active, payload

    def test_four_heroes_spawn_on_unique_server_map_cells(self) -> None:
        units = field(field(self.payload, "public"), "units")
        self.assertEqual(4, len(units))
        self.assertEqual(4, len({(unit["x"], unit["y"]) for unit in units}))
        self.assertEqual([0, 1, 2, 3], [unit["seat"] for unit in units])

    def test_server_reachable_and_move_update_authoritative_position(self) -> None:
        active, payload = self.control_active()
        options = field(payload, "private_view")["action_options"]
        movement = next(
            option
            for option in options
            if option["kind"] in {"secondary_movement", "primary"}
            and option["kind"] != "primary"
        )
        payload = self.app.choose_action(
            active, movement["kind"], movement["source_slot"],
            self.room_id, field(payload, "revision"),
        )
        reachable = field(payload, "private_view")["reachable"]
        self.assertTrue(reachable)
        destination = reachable[0]
        before = next(
            unit for unit in field(field(payload, "public"), "units")
            if unit["seat"] == active
        )
        moved = self.app.move(
            active, destination["x"], destination["y"],
            self.room_id, field(payload, "revision"),
        )
        after = next(
            unit for unit in field(field(moved, "public"), "units")
            if unit["seat"] == active
        )
        self.assertNotEqual((before["x"], before["y"]), (after["x"], after["y"]))
        self.assertEqual(
            (destination["x"], destination["y"]), (after["x"], after["y"])
        )

    def test_fast_move_allows_same_or_adjacent_enemy_free_region(self) -> None:
        active, payload = self.control_active()
        option = next(
            option
            for option in field(payload, "private_view")["action_options"]
            if option["kind"] == "fast_move"
        )
        payload = self.app.choose_action(
            active,
            option["kind"],
            option["source_slot"],
            self.room_id,
            field(payload, "revision"),
        )
        targets = field(payload, "private_view")["fast_move_targets"]
        self.assertTrue(targets)
        state = self.app._rooms.read(self.room_id).state
        source = state.units[active]
        cells = {(cell.x, cell.y): cell for cell in state.board}
        source_region = cells[(source.x, source.y)].region
        target_regions = {cells[(item["x"], item["y"])].region for item in targets}
        from goa2.engine import adjacent_regions

        self.assertTrue(
            target_regions <= {source_region, *adjacent_regions(state)[source_region]}
        )
        destination = targets[0]
        moved = self.app.move(
            active,
            destination["x"],
            destination["y"],
            self.room_id,
            field(payload, "revision"),
        )
        unit = next(
            unit for unit in field(field(moved, "public"), "units")
            if unit["seat"] == active
        )
        self.assertEqual((destination["x"], destination["y"]), (unit["x"], unit["y"]))

    def test_fast_move_has_no_targets_when_source_region_has_enemy(self) -> None:
        from dataclasses import replace

        active, payload = self.control_active()
        state = self.app._rooms.read(self.room_id).state
        source = state.units[active]
        cells = {(cell.x, cell.y): cell for cell in state.board}
        source_region = cells[(source.x, source.y)].region
        enemy_index = next(
            index for index, unit in enumerate(state.units)
            if unit.team is not source.team
        )
        destination = next(
            cell
            for cell in state.board
            if cell.region == source_region
            and not cell.obstacle
            and (cell.x, cell.y) != (source.x, source.y)
        )
        units = list(state.units)
        units[enemy_index] = replace(
            units[enemy_index], x=destination.x, y=destination.y
        )
        self.app._rooms.transact(
            self.room_id,
            lambda current: replace(
                current,
                state=replace(current.state, units=tuple(units)),
            ),
        )
        option = next(
            option
            for option in field(payload, "private_view")["action_options"]
            if option["kind"] == "fast_move"
        )
        payload = self.app.choose_action(
            active,
            option["kind"],
            option["source_slot"],
            self.room_id,
            field(payload, "revision"),
        )
        self.assertEqual([], field(payload, "private_view")["fast_move_targets"])

    def test_debug_teleport_preserves_pending_action_and_movement_budget(self) -> None:
        active, payload = self.control_active()
        movement = next(
            option
            for option in field(payload, "private_view")["action_options"]
            if option["kind"] == "secondary_movement"
        )
        payload = self.app.choose_action(
            active,
            movement["kind"],
            movement["source_slot"],
            self.room_id,
            field(payload, "revision"),
        )
        before_private = field(payload, "private_view")
        before_state = self.app._rooms.read(self.room_id).state
        destination = before_private["debug_teleport_targets"][0]

        teleported = self.app.debug_teleport(
            active,
            destination["x"],
            destination["y"],
            self.room_id,
            field(payload, "revision"),
        )

        after_private = field(teleported, "private_view")
        self.assertEqual(
            before_private["pending_action"],
            after_private["pending_action"],
        )
        after_state = self.app._rooms.read(self.room_id).state
        self.assertEqual(before_state.pending_movement, after_state.pending_movement)
        unit = next(
            unit for unit in field(field(teleported, "public"), "units")
            if unit["seat"] == active
        )
        self.assertEqual((destination["x"], destination["y"]), (unit["x"], unit["y"]))

    def test_debug_teleport_rejects_occupied_cell_atomically(self) -> None:
        active, payload = self.control_active()
        occupied = next(
            unit for unit in field(field(payload, "public"), "units")
            if unit["seat"] != active
        )
        before = self.app.state(self.room_id)

        with self.assertRaises(ValueError):
            self.app.debug_teleport(
                active,
                occupied["x"],
                occupied["y"],
                self.room_id,
                field(payload, "revision"),
            )

        self.assertEqual(before, self.app.state(self.room_id))

    def test_debug_teleport_can_target_any_seat_in_multiplayer_debug_mode(self) -> None:
        from goa2.domain import DebugTeleportHero, Seat
        from goa2.engine import reduce_room

        state = self.app._rooms.read(self.room_id).state
        wrong_seat = Seat((int(state.controlled_seat) + 1) % 4)
        destination = next(
            cell
            for cell in state.board
            if not cell.obstacle
            and (cell.x, cell.y)
            not in {(unit.x, unit.y) for unit in state.units}
        )

        reduction = reduce_room(
            state,
            DebugTeleportHero(
                expected_revision=state.revision,
                seat=wrong_seat,
                x=destination.x,
                y=destination.y,
            ),
        )
        moved = reduction.state.units[int(wrong_seat)]
        self.assertEqual((destination.x, destination.y), (moved.x, moved.y))


if __name__ == "__main__":
    unittest.main()
