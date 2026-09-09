from __future__ import annotations

import unittest

from tests.contract_support import field, state_snapshot


class RoomContractTests(unittest.TestCase):
    def setUp(self) -> None:
        from goa2.domain import create_initial_room_state

        self.state = create_initial_room_state()

    def test_switch_controlled_seat_increments_revision(self) -> None:
        from goa2.domain import Seat, SwitchControlledSeat
        from goa2.engine.reducer import reduce_room

        reduction = reduce_room(
            self.state,
            SwitchControlledSeat(
                expected_revision=self.state.revision,
                target_seat=Seat.TWO,
            ),
        )
        after = reduction.state

        self.assertEqual(2, field(after, "controlled_seat"))
        self.assertEqual(self.state.revision + 1, field(after, "revision"))
        self.assertEqual(1, len(reduction.events))
        self.assertEqual(0, self.state.revision)
        self.assertEqual(0, field(self.state, "controlled_seat"))

    def test_reset_restores_initial_shell_and_increments_revision(self) -> None:
        from goa2.domain import ResetRoom, Seat, SwitchControlledSeat
        from goa2.engine.reducer import reduce_room

        switched = reduce_room(
            self.state,
            SwitchControlledSeat(expected_revision=0, target_seat=Seat.THREE),
        ).state
        reset = reduce_room(
            switched,
            ResetRoom(expected_revision=switched.revision),
        ).state

        self.assertEqual(0, field(reset, "controlled_seat"))
        self.assertEqual(field(switched, "revision") + 1, field(reset, "revision"))
        self.assertEqual(4, len(field(reset, "seats")))

    def test_illegal_revision_fails_atomically(self) -> None:
        from goa2.domain import ResetRoom
        from goa2.engine.reducer import IllegalCommand, reduce_room

        before = state_snapshot(self.state)

        with self.assertRaises(IllegalCommand):
            reduce_room(self.state, ResetRoom(expected_revision=99))

        self.assertEqual(before, state_snapshot(self.state))

    def test_stale_revision_fails_atomically(self) -> None:
        from goa2.domain import ResetRoom, Seat, SwitchControlledSeat
        from goa2.engine.reducer import IllegalCommand, reduce_room

        current = reduce_room(
            self.state,
            SwitchControlledSeat(expected_revision=0, target_seat=Seat.ONE),
        ).state
        before = state_snapshot(current)

        with self.assertRaises(IllegalCommand):
            reduce_room(current, ResetRoom(expected_revision=current.revision - 1))

        self.assertEqual(before, state_snapshot(current))


if __name__ == "__main__":
    unittest.main()
