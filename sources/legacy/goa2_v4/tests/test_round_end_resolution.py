from __future__ import annotations

from dataclasses import replace
import unittest
from uuid import uuid4

from goa2.domain import MinionKind, MinionState, Phase, Team
from tests.contract_support import field


def minion(unit_id: str, team: Team, kind: MinionKind) -> MinionState:
    return MinionState(unit_id, team, kind, 0, 0)


class RoundEndResolutionTests(unittest.TestCase):
    def setUp(self) -> None:
        from goa2.backend.bootstrap import build_application

        self.app = build_application()
        self.room_id = uuid4().hex
        self.app.state(self.room_id)

    def set_round_end(
        self,
        minions: tuple[MinionState, ...],
        *,
        combat_region: str = "mid",
        red_marks: int = 0,
    ) -> dict:
        self.app._rooms.transact(
            self.room_id,
            lambda current: replace(
                current,
                state=replace(
                    current.state,
                    phase=Phase.ROUND_END,
                    minions=minions,
                    combat_region=combat_region,
                    teams=(
                        current.state.teams[0],
                        replace(current.state.teams[1], frontline_marks=red_marks),
                    ),
                ),
            ),
        )
        return self.app.state(self.room_id)

    def test_losing_captain_removes_minion_then_frontline_advances(self) -> None:
        payload = self.set_round_end(
            (
                minion("blue-heavy", Team.BLUE, MinionKind.HEAVY),
                minion("red-melee", Team.RED, MinionKind.MELEE),
                minion("red-heavy", Team.RED, MinionKind.HEAVY),
            )
        )
        payload = self.app.begin_round_end(
            self.room_id, field(payload, "revision")
        )
        choice = field(field(payload, "public"), "pending_captain_choice")
        self.assertEqual(0, choice["chooser"])
        self.assertEqual(["blue-heavy"], choice["candidate_ids"])

        resolved = self.app.resolve_captain_choice(
            0,
            choice["choice_id"],
            "blue-heavy",
            self.room_id,
            field(payload, "revision"),
        )

        public = field(resolved, "public")
        self.assertEqual("blueNear", public["combat_region"])
        self.assertEqual(1, public["teams"][1]["frontline_marks"])
        self.assertEqual(11, len(public["minions"]))
        self.assertEqual("card_selection", public["phase"])
        self.assertEqual(2, public["round"])

    def test_debug_tools_can_prepare_visible_round_end_imbalance(self) -> None:
        payload = self.app.state(self.room_id)
        payload = self.app.debug_enter_round_end(
            self.room_id, field(payload, "revision")
        )
        self.assertEqual("round_end", field(field(payload, "public"), "phase"))
        target = next(
            item
            for item in field(field(payload, "public"), "minions")
            if item["team"] == "blue" and item["kind"] == "melee"
        )
        payload = self.app.debug_remove_minion(
            0,
            target["unit_id"],
            self.room_id,
            field(payload, "revision"),
        )
        self.assertEqual(
            11,
            len(field(field(payload, "public"), "minions")),
        )
        started = self.app.begin_round_end(
            self.room_id, field(payload, "revision")
        )
        choice = field(field(started, "public"), "pending_captain_choice")
        self.assertEqual("remove_minions", choice["kind"])
        self.assertEqual(0, choice["chooser"])

    def test_debug_remove_is_available_during_a_normal_turn(self) -> None:
        payload = self.app.state(self.room_id)
        self.assertIn("remove_minion", field(payload, "public")["debug_actions"])
        target = next(
            item for item in field(payload, "public")["minions"]
            if item["kind"] == "melee"
        )

        removed = self.app.debug_remove_minion(
            0,
            target["unit_id"],
            self.room_id,
            field(payload, "revision"),
        )

        self.assertEqual("card_selection", field(removed, "public")["phase"])
        self.assertNotIn(
            target["unit_id"],
            {item["unit_id"] for item in field(removed, "public")["minions"]},
        )

    def test_debug_removing_heavy_advances_immediately_during_turn(self) -> None:
        payload = self.app.state(self.room_id)
        target = next(
            item for item in field(payload, "public")["minions"]
            if item["team"] == "blue" and item["kind"] == "heavy"
        )

        advanced = self.app.debug_remove_minion(
            0,
            target["unit_id"],
            self.room_id,
            field(payload, "revision"),
        )

        public = field(advanced, "public")
        self.assertEqual("card_selection", public["phase"])
        self.assertEqual("blueNear", public["combat_region"])
        self.assertEqual(1, public["teams"][1]["frontline_marks"])
        while public["pending_captain_choice"] is not None:
            choice = public["pending_captain_choice"]
            chooser = choice["chooser"]
            if field(advanced, "controlled_seat") != chooser:
                advanced = self.app.control(
                    chooser, self.room_id, field(advanced, "revision")
                )
            advanced = self.app.resolve_captain_choice(
                chooser,
                choice["choice_id"],
                choice["candidate_ids"][0],
                self.room_id,
                field(advanced, "revision"),
            )
            public = field(advanced, "public")
        self.assertEqual(11, len(public["minions"]))

    def test_advancing_from_enemy_near_region_ends_game(self) -> None:
        payload = self.set_round_end(
            (
                minion("blue-heavy", Team.BLUE, MinionKind.HEAVY),
                minion("red-melee", Team.RED, MinionKind.MELEE),
                minion("red-heavy", Team.RED, MinionKind.HEAVY),
            ),
            combat_region="blueNear",
        )
        payload = self.app.begin_round_end(
            self.room_id, field(payload, "revision")
        )
        choice = field(field(payload, "public"), "pending_captain_choice")
        resolved = self.app.resolve_captain_choice(
            0,
            choice["choice_id"],
            choice["candidate_ids"][0],
            self.room_id,
            field(payload, "revision"),
        )

        public = field(resolved, "public")
        self.assertEqual("game_over", public["phase"])
        self.assertEqual("red", public["winner"])
        self.assertEqual([], public["minions"])

    def test_third_frontline_mark_ends_game(self) -> None:
        payload = self.set_round_end(
            (
                minion("blue-heavy", Team.BLUE, MinionKind.HEAVY),
                minion("red-melee", Team.RED, MinionKind.MELEE),
                minion("red-heavy", Team.RED, MinionKind.HEAVY),
            ),
            red_marks=2,
        )
        payload = self.app.begin_round_end(
            self.room_id, field(payload, "revision")
        )
        choice = field(field(payload, "public"), "pending_captain_choice")
        resolved = self.app.resolve_captain_choice(
            0,
            choice["choice_id"],
            choice["candidate_ids"][0],
            self.room_id,
            field(payload, "revision"),
        )

        public = field(resolved, "public")
        self.assertEqual("game_over", public["phase"])
        self.assertEqual("red", public["winner"])
        self.assertEqual(3, public["teams"][1]["frontline_marks"])

    def test_occupied_spawn_creates_captain_cell_choice(self) -> None:
        state = self.app._rooms.read(self.room_id).state
        blocked = next(
            cell
            for cell in state.board
            if cell.region == "blueNear" and cell.state == "blueMeleeSpawn"
        )
        units = list(state.units)
        units[0] = replace(units[0], x=blocked.x, y=blocked.y)
        self.app._rooms.transact(
            self.room_id,
            lambda current: replace(
                current,
                state=replace(current.state, units=tuple(units)),
            ),
        )
        payload = self.set_round_end(
            (
                minion("blue-heavy", Team.BLUE, MinionKind.HEAVY),
                minion("red-melee", Team.RED, MinionKind.MELEE),
                minion("red-heavy", Team.RED, MinionKind.HEAVY),
            )
        )
        payload = self.app.begin_round_end(
            self.room_id, field(payload, "revision")
        )
        removal = field(field(payload, "public"), "pending_captain_choice")
        payload = self.app.resolve_captain_choice(
            0,
            removal["choice_id"],
            removal["candidate_ids"][0],
            self.room_id,
            field(payload, "revision"),
        )
        spawn = field(field(payload, "public"), "pending_captain_choice")

        self.assertEqual("choose_spawn_cell", spawn["kind"])
        self.assertEqual(0, spawn["chooser"])
        self.assertTrue(spawn["candidate_ids"])
        resolved = payload
        while field(field(resolved, "public"), "pending_captain_choice") is not None:
            spawn = field(field(resolved, "public"), "pending_captain_choice")
            chooser = spawn["chooser"]
            if field(resolved, "controlled_seat") != chooser:
                resolved = self.app.control(
                    chooser, self.room_id, field(resolved, "revision")
                )
            resolved = self.app.resolve_captain_choice(
                chooser,
                spawn["choice_id"],
                spawn["candidate_ids"][0],
                self.room_id,
                field(resolved, "revision"),
            )
        self.assertEqual("card_selection", field(field(resolved, "public"), "phase"))
        self.assertEqual(11, len(field(field(resolved, "public"), "minions")))


if __name__ == "__main__":
    unittest.main()
