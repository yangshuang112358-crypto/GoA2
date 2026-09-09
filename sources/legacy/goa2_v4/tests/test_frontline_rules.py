from __future__ import annotations

from dataclasses import replace
import unittest

from goa2.domain import MinionKind, MinionState, Seat, Team, TeamState
from goa2.engine import (
    legal_minion_removal_ids,
    minion_count_difference,
    winner_from_team_state,
)


def minion(unit_id: str, team: Team, kind: MinionKind) -> MinionState:
    return MinionState(unit_id, team, kind, 0, 0)


class FrontlineRulesTests(unittest.TestCase):
    def test_difference_identifies_smaller_team_and_exact_count(self) -> None:
        minions = (
            minion("b1", Team.BLUE, MinionKind.MELEE),
            minion("r1", Team.RED, MinionKind.MELEE),
            minion("r2", Team.RED, MinionKind.RANGED),
            minion("r3", Team.RED, MinionKind.HEAVY),
        )
        self.assertEqual((Team.BLUE, 2), minion_count_difference(minions))

    def test_heavy_is_only_removable_after_other_minions(self) -> None:
        minions = (
            minion("melee", Team.BLUE, MinionKind.MELEE),
            minion("ranged", Team.BLUE, MinionKind.RANGED),
            minion("heavy", Team.BLUE, MinionKind.HEAVY),
        )
        self.assertEqual(
            ("melee", "ranged"),
            legal_minion_removal_ids(minions, Team.BLUE),
        )
        self.assertEqual(
            ("heavy",),
            legal_minion_removal_ids((minions[-1],), Team.BLUE),
        )

    def test_crystal_and_three_mark_victories(self) -> None:
        blue = TeamState(Team.BLUE, captain_seat=Seat.ZERO)
        red = TeamState(Team.RED, captain_seat=Seat.ONE)
        self.assertEqual(
            Team.RED,
            winner_from_team_state((replace(blue, crystal_life=0), red)),
        )
        self.assertEqual(
            Team.BLUE,
            winner_from_team_state((replace(blue, frontline_marks=3), red)),
        )

    def test_conflicting_outcomes_are_rejected(self) -> None:
        blue = TeamState(Team.BLUE, crystal_life=0)
        red = TeamState(Team.RED, crystal_life=0)
        with self.assertRaises(ValueError):
            winner_from_team_state((blue, red))


if __name__ == "__main__":
    unittest.main()
