from __future__ import annotations

from dataclasses import replace
import json
import unittest
from uuid import uuid4

from goa2.domain import (
    DebugAdvanceFrontline,
    DebugDefeatHero,
    DebugResetMinions,
    DebugSetCrystalLife,
    DebugSetFrontlineMarks,
    DebugSetSeatCoins,
    Phase,
    Seat,
    Team,
)
from goa2.engine.reducer import IllegalCommand, reduce_room
from tests.contract_support import state_snapshot


class DebugCommandTests(unittest.TestCase):
    def setUp(self) -> None:
        from goa2.backend.bootstrap import build_application

        app = build_application()
        self.room_id = uuid4().hex
        app.state(self.room_id)
        self.state = app._rooms.read(self.room_id).state

    def test_debug_commands_are_json_serializable(self) -> None:
        commands = (
            DebugSetSeatCoins(0, Seat.TWO, 9),
            DebugSetCrystalLife(0, Team.RED, 4),
            DebugSetFrontlineMarks(0, Team.BLUE, 2),
            DebugDefeatHero(0, Seat.ONE),
            DebugResetMinions(0),
            DebugAdvanceFrontline(0, Team.RED),
        )

        for command in commands:
            json.dumps(command.to_dict(), sort_keys=True)

    def test_set_seat_coins_updates_only_target_and_emits_event(self) -> None:
        reduction = reduce_room(
            self.state,
            DebugSetSeatCoins(self.state.revision, Seat.TWO, 12),
        )

        self.assertEqual([0, 0, 12, 0], [seat.coins for seat in reduction.state.seats])
        self.assertEqual(
            {
                "type": "seat_coins_debug_set",
                "revision": 1,
                "seat": 2,
                "coins": 12,
            },
            reduction.events[0].to_dict(),
        )

    def test_zero_crystal_life_immediately_ends_game_for_opponent(self) -> None:
        reduction = reduce_room(
            self.state,
            DebugSetCrystalLife(self.state.revision, Team.BLUE, 0),
        )

        self.assertEqual(Phase.GAME_OVER, reduction.state.phase)
        self.assertEqual(Team.RED, reduction.state.winner)
        self.assertEqual(0, reduction.state.teams[0].crystal_life)
        self.assertEqual("red", reduction.events[0].to_dict()["winner"])

    def test_frontline_threshold_immediately_ends_game_for_scoring_team(self) -> None:
        reduction = reduce_room(
            self.state,
            DebugSetFrontlineMarks(
                self.state.revision,
                Team.RED,
                self.state.frontline_victory_marks,
            ),
        )

        self.assertEqual(Phase.GAME_OVER, reduction.state.phase)
        self.assertEqual(Team.RED, reduction.state.winner)
        self.assertEqual(
            self.state.frontline_victory_marks,
            reduction.state.teams[1].frontline_marks,
        )

    def test_debug_defeat_uses_crystal_and_respawn_state_without_gold(self) -> None:
        before_life = self.state.teams[1].crystal_life
        reduction = reduce_room(
            self.state,
            DebugDefeatHero(self.state.revision, Seat.ONE),
        )

        self.assertTrue(reduction.state.seats[1].defeated)
        self.assertEqual(before_life - 1, reduction.state.teams[1].crystal_life)
        self.assertEqual([0, 0, 0, 0], [seat.coins for seat in reduction.state.seats])
        self.assertGreater(reduction.state.units[1].x, 9999)
        self.assertEqual(
            {
                "type": "hero_debug_defeated",
                "revision": 1,
                "seat": 1,
                "crystal_damage": 1,
                "winner": None,
            },
            reduction.events[0].to_dict(),
        )

    def test_reset_minions_restores_both_teams_in_current_region(self) -> None:
        reduced = replace(self.state, minions=self.state.minions[:3])
        reduction = reduce_room(
            reduced,
            DebugResetMinions(reduced.revision),
        )

        teams = {minion.team for minion in reduction.state.minions}
        self.assertEqual({Team.BLUE, Team.RED}, teams)
        self.assertTrue(
            all(
                next(
                    cell.region
                    for cell in reduction.state.board
                    if (cell.x, cell.y) == (minion.x, minion.y)
                )
                == reduction.state.combat_region
                for minion in reduction.state.minions
            )
        )
        self.assertEqual(
            len(reduction.state.minions),
            reduction.events[0].to_dict()["minion_count"],
        )

    def test_force_advance_reuses_marks_region_spawn_and_victory_rules(self) -> None:
        reduction = reduce_room(
            self.state,
            DebugAdvanceFrontline(self.state.revision, Team.BLUE),
        )

        self.assertEqual("redNear", reduction.state.combat_region)
        self.assertEqual(1, reduction.state.teams[0].frontline_marks)
        self.assertEqual({Team.BLUE, Team.RED}, {item.team for item in reduction.state.minions})
        event = reduction.events[0].to_dict()
        self.assertEqual("mid", event["previous_region"])
        self.assertEqual("redNear", event["combat_region"])
        self.assertEqual(Team.BLUE.value, event["team"])

        boundary = replace(
            reduction.state,
            revision=reduction.state.revision,
            combat_region="redNear",
        )
        won = reduce_room(
            boundary,
            DebugAdvanceFrontline(boundary.revision, Team.BLUE),
        )
        self.assertEqual(Phase.GAME_OVER, won.state.phase)
        self.assertEqual(Team.BLUE, won.state.winner)
        self.assertEqual((), won.state.minions)

    def test_illegal_debug_commands_fail_atomically(self) -> None:
        before = state_snapshot(self.state)
        with self.assertRaises(IllegalCommand):
            reduce_room(
                self.state,
                DebugSetSeatCoins(self.state.revision + 1, Seat.ZERO, 3),
            )
        self.assertEqual(before, state_snapshot(self.state))

        with self.assertRaises(ValueError):
            DebugSetSeatCoins(self.state.revision, Seat.ZERO, -1)
        with self.assertRaises(ValueError):
            DebugSetCrystalLife(self.state.revision, Team.BLUE, -1)
        with self.assertRaises(ValueError):
            DebugSetFrontlineMarks(self.state.revision, Team.BLUE, -1)

        game_over = replace(self.state, phase=Phase.GAME_OVER, winner=Team.BLUE)
        game_over_before = state_snapshot(game_over)
        with self.assertRaises(IllegalCommand):
            reduce_room(
                game_over,
                DebugResetMinions(game_over.revision),
            )
        self.assertEqual(game_over_before, state_snapshot(game_over))


if __name__ == "__main__":
    unittest.main()
