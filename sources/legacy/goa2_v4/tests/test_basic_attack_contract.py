from __future__ import annotations

from dataclasses import replace
import unittest
from uuid import uuid4

from tests.contract_support import field


class BasicAttackContractTests(unittest.TestCase):
    def setUp(self) -> None:
        from goa2.backend.bootstrap import build_application

        self.app = build_application()
        self.room_id = uuid4().hex
        payload = self.app.state(self.room_id)
        payload = self.app.force_confirm(
            self.room_id, field(payload, "revision")
        )
        self.active_seat = field(field(payload, "public"), "active_seat")
        if field(payload, "controlled_seat") != self.active_seat:
            payload = self.app.control(
                self.active_seat,
                self.room_id,
                field(payload, "revision"),
            )
        self.payload = payload

    def _place_targets(self) -> dict[str, str]:
        room = self.app._rooms.read(self.room_id)
        units = list(room.state.units)
        attacker = units[self.active_seat]
        units[self.active_seat] = replace(attacker, x=0, y=0)
        other_seats = [seat for seat in range(4) if seat != self.active_seat]
        units[other_seats[0]] = replace(units[other_seats[0]], x=1, y=0)
        units[other_seats[1]] = replace(units[other_seats[1]], x=0, y=-1)
        units[other_seats[2]] = replace(units[other_seats[2]], x=3, y=0)
        minions = [
            replace(minion, x=20 + index, y=20)
            for index, minion in enumerate(room.state.minions)
        ]
        enemy_minion_index = next(
            index
            for index, minion in enumerate(minions)
            if minion.team is not attacker.team
        )
        minions[enemy_minion_index] = replace(
            minions[enemy_minion_index], x=-1, y=0
        )
        minions[1] = replace(minions[1], x=4, y=0)
        self.app._rooms.transact(
            self.room_id,
            lambda current: replace(
                current,
                state=replace(
                    current.state,
                    units=tuple(units),
                    minions=tuple(minions),
                ),
            ),
        )
        self.payload = self.app.state(self.room_id)
        return {
            "adjacent_hero_a": units[other_seats[0]].unit_id,
            "adjacent_hero_b": units[other_seats[1]].unit_id,
            "distant_hero": units[other_seats[2]].unit_id,
            "adjacent_minion": minions[enemy_minion_index].unit_id,
            "distant_minion": minions[1].unit_id,
        }

    def test_catalog_profile_preserves_primary_category(self) -> None:
        room = self.app._rooms.read(self.room_id)
        profile = room.state.seats[self.active_seat].action_profile

        self.assertEqual("基础攻击", profile.primary_category)
        self.assertEqual(profile, type(profile).from_dict(profile.to_dict()))

    def test_targets_include_adjacent_enemy_units_only(self) -> None:
        ids = self._place_targets()
        chosen = self.app.choose_action(
            self.active_seat,
            "primary",
            None,
            self.room_id,
            field(self.payload, "revision"),
        )
        targets = {
            target["unit_id"]: target
            for target in field(chosen, "private_view")["attack_targets"]
        }

        room = self.app._rooms.read(self.room_id)
        attacker_team = room.state.units[self.active_seat].team
        expected = {
            unit_id
            for unit_id in (
                ids["adjacent_hero_a"],
                ids["adjacent_hero_b"],
                ids["adjacent_minion"],
            )
            if next(
                unit
                for unit in (*room.state.units, *room.state.minions)
                if unit.unit_id == unit_id
            ).team is not attacker_team
        }
        self.assertEqual(expected, set(targets))
        self.assertEqual(
            {"hero", "minion"},
            {target["kind"] for target in targets.values()},
        )
        self.assertNotIn(
            f"hero-{self.active_seat}",
            targets,
        )

    def test_primary_is_unavailable_without_an_adjacent_target(self) -> None:
        private = field(self.payload, "private_view")

        self.assertNotIn(
            {"kind": "primary", "source_slot": None},
            private["action_options"],
        )
        before = self.app.state(self.room_id)
        with self.assertRaises(ValueError):
            self.app.choose_action(
                self.active_seat,
                "primary",
                None,
                self.room_id,
                field(before, "revision"),
            )
        self.assertEqual(before, self.app.state(self.room_id))

    def test_declaring_target_creates_serializable_pending_attack(self) -> None:
        from goa2.domain import RoomState

        ids = self._place_targets()
        chosen = self.app.choose_action(
            self.active_seat,
            "primary",
            None,
            self.room_id,
            field(self.payload, "revision"),
        )
        room = self.app._rooms.read(self.room_id)
        attacker_team = room.state.units[self.active_seat].team
        hero_target = next(
            unit_id
            for unit_id in (ids["adjacent_hero_a"], ids["adjacent_hero_b"])
            if next(
                unit for unit in room.state.units if unit.unit_id == unit_id
            ).team is not attacker_team
        )
        declared = self.app.declare_basic_attack(
            self.active_seat,
            hero_target,
            self.room_id,
            field(chosen, "revision"),
        )
        pending = field(field(declared, "public"), "pending_attack")

        self.assertEqual(self.active_seat, pending["attacker_seat"])
        self.assertEqual(hero_target, pending["target_id"])
        self.assertEqual("hero", pending["target_kind"])
        self.assertGreaterEqual(pending["attack_value"], 0)
        self.assertEqual(
            "card_resolution",
            field(field(declared, "public"), "phase"),
        )
        state = self.app._rooms.read(self.room_id).state
        self.assertEqual(state, RoomState.from_dict(state.to_dict()))

    def test_attacking_a_minion_immediately_defeats_it_and_advances(self) -> None:
        ids = self._place_targets()
        chosen = self.app.choose_action(
            self.active_seat,
            "primary",
            None,
            self.room_id,
            field(self.payload, "revision"),
        )

        resolved = self.app.declare_basic_attack(
            self.active_seat,
            ids["adjacent_minion"],
            self.room_id,
            field(chosen, "revision"),
        )

        public = field(resolved, "public")
        self.assertIsNone(public["pending_attack"])
        self.assertNotIn(
            ids["adjacent_minion"],
            {minion["unit_id"] for minion in public["minions"]},
        )
        self.assertEqual(
            2,
            public["seats"][self.active_seat]["coins"],
        )
        if public["pending_initiative_choice"] is None:
            self.assertNotEqual(self.active_seat, public["active_seat"])
        else:
            self.assertNotIn("active_seat", public)

    def test_defeating_heavy_advances_before_action_queue_continues(self) -> None:
        from dataclasses import replace
        from goa2.domain import MinionKind

        ids = self._place_targets()
        room = self.app._rooms.read(self.room_id)
        target_team = next(
            minion.team
            for minion in room.state.minions
            if minion.unit_id == ids["adjacent_minion"]
        )
        minions = tuple(
            replace(minion, kind=MinionKind.HEAVY)
            if minion.unit_id == ids["adjacent_minion"]
            else minion
            for minion in room.state.minions
            if minion.team is not target_team
            or minion.unit_id == ids["adjacent_minion"]
        )
        self.app._rooms.transact(
            self.room_id,
            lambda current: replace(
                current,
                state=replace(current.state, minions=minions),
            ),
        )
        payload = self.app.state(self.room_id)
        chosen = self.app.choose_action(
            self.active_seat,
            "primary",
            None,
            self.room_id,
            field(payload, "revision"),
        )
        payload = self.app.declare_basic_attack(
            self.active_seat,
            ids["adjacent_minion"],
            self.room_id,
            field(chosen, "revision"),
        )
        while field(payload, "public")["pending_captain_choice"] is not None:
            choice = field(payload, "public")["pending_captain_choice"]
            chooser = choice["chooser"]
            if field(payload, "controlled_seat") != chooser:
                payload = self.app.control(
                    chooser, self.room_id, field(payload, "revision")
                )
            payload = self.app.resolve_captain_choice(
                chooser,
                choice["choice_id"],
                choice["candidate_ids"][0],
                self.room_id,
                field(payload, "revision"),
            )

        public = field(payload, "public")
        self.assertIn(self.active_seat, public["resolved_seats"])
        self.assertEqual(1, sum(team["frontline_marks"] for team in public["teams"]))

    def test_attacked_hero_can_discard_a_card_to_defend(self) -> None:
        ids = self._place_targets()
        room = self.app._rooms.read(self.room_id)
        attacker_team = room.state.units[self.active_seat].team
        target = next(
            unit
            for unit in room.state.units
            if unit.unit_id in {ids["adjacent_hero_a"], ids["adjacent_hero_b"]}
            and unit.team is not attacker_team
        )
        chosen = self.app.choose_action(
            self.active_seat,
            "primary",
            None,
            self.room_id,
            field(self.payload, "revision"),
        )
        pending = self.app.declare_basic_attack(
            self.active_seat,
            target.unit_id,
            self.room_id,
            field(chosen, "revision"),
        )

        self.assertEqual(int(target.seat), field(pending, "controlled_seat"))
        private = field(pending, "private_view")
        self.assertIn("resolve_defense", private["legal_actions"])
        defense = next(
            card
            for card in private["eligible_defense_cards"]
            if card["exclamation"]
            or card["defense_value"] >= field(field(pending, "public"), "pending_attack")["attack_value"]
        )
        defended = self.app.resolve_basic_defense(
            int(target.seat),
            defense["card_id"],
            self.room_id,
            field(pending, "revision"),
        )

        self.assertIsNone(field(field(defended, "public"), "pending_attack"))
        self.assertIn(defense["card_id"], field(defended, "private_view")["discard"])
        self.assertFalse(
            self.app._rooms.read(self.room_id).state.seats[int(target.seat)].defeated
        )

    def test_attack_and_defense_passives_change_resolution_values(self) -> None:
        ids = self._place_targets()
        room = self.app._rooms.read(self.room_id)
        attacker = self.active_seat
        target = next(
            unit
            for unit in room.state.units
            if unit.unit_id == ids["adjacent_hero_a"]
        )
        seats = list(room.state.seats)
        attack_bonuses = list(seats[attacker].passive_bonuses)
        attack_bonuses[0] = 2
        seats[attacker] = replace(
            seats[attacker],
            passive_bonuses=tuple(attack_bonuses),
        )
        defense_bonuses = list(seats[int(target.seat)].passive_bonuses)
        defense_bonuses[1] = 20
        seats[int(target.seat)] = replace(
            seats[int(target.seat)],
            passive_bonuses=tuple(defense_bonuses),
        )
        self.app._rooms.transact(
            self.room_id,
            lambda current: replace(
                current,
                state=replace(current.state, seats=tuple(seats)),
            ),
        )
        payload = self.app.state(self.room_id)
        chosen = self.app.choose_action(
            attacker, "primary", None, self.room_id, field(payload, "revision")
        )
        pending = self.app.declare_basic_attack(
            attacker,
            target.unit_id,
            self.room_id,
            field(chosen, "revision"),
        )
        base_attack = seats[attacker].action_profile.primary_value
        self.assertEqual(
            base_attack + 2,
            field(pending, "public")["pending_attack"]["attack_value"],
        )
        defense = next(
            card
            for card in field(pending, "private_view")["eligible_defense_cards"]
            if not card["exclamation"]
        )
        base_defense = self.app._catalog.defense_profile(
            defense["card_id"]
        )["defense_value"]
        self.assertEqual(base_defense + 20, defense["defense_value"])

        defended = self.app.resolve_basic_defense(
            int(target.seat),
            defense["card_id"],
            self.room_id,
            field(pending, "revision"),
        )

        self.assertFalse(
            self.app._rooms.read(self.room_id).state.seats[int(target.seat)].defeated
        )
        self.assertIsNone(field(defended, "public")["pending_attack"])

    def test_minions_modify_attack_against_a_hero_by_kind_and_distance(self) -> None:
        from goa2.domain import MinionKind

        room = self.app._rooms.read(self.room_id)
        attacker = room.state.units[self.active_seat]
        target = next(
            unit for unit in room.state.units
            if unit.team is not attacker.team
        )
        units = tuple(
            replace(unit, x=0, y=0)
            if unit.seat == attacker.seat
            else replace(unit, x=1, y=0)
            if unit.seat == target.seat
            else replace(unit, x=30 + int(unit.seat), y=30)
            for unit in room.state.units
        )
        attacker_minions = [
            minion for minion in room.state.minions
            if minion.team is attacker.team
        ]
        defender_minions = [
            minion for minion in room.state.minions
            if minion.team is target.team
        ]
        placements = {
            attacker_minions[0].unit_id: (MinionKind.MELEE, 2, 0),
            attacker_minions[1].unit_id: (MinionKind.HEAVY, 2, -1),
            attacker_minions[2].unit_id: (MinionKind.RANGED, 1, -2),
            attacker_minions[3].unit_id: (MinionKind.RANGED, 1, -3),
            defender_minions[0].unit_id: (MinionKind.MELEE, 0, 1),
            defender_minions[1].unit_id: (MinionKind.HEAVY, 1, 1),
        }
        minions = tuple(
            replace(
                minion,
                kind=placements[minion.unit_id][0],
                x=placements[minion.unit_id][1],
                y=placements[minion.unit_id][2],
            )
            if minion.unit_id in placements
            else replace(minion, x=50, y=50 + index)
            for index, minion in enumerate(room.state.minions)
        )
        self.app._rooms.transact(
            self.room_id,
            lambda current: replace(
                current,
                state=replace(current.state, units=units, minions=minions),
            ),
        )
        payload = self.app.state(self.room_id)
        chosen = self.app.choose_action(
            self.active_seat,
            "primary",
            None,
            self.room_id,
            field(payload, "revision"),
        )
        pending_payload = self.app.declare_basic_attack(
            self.active_seat,
            target.unit_id,
            self.room_id,
            field(chosen, "revision"),
        )
        pending = field(pending_payload, "public")["pending_attack"]

        self.assertEqual(3, pending["support_bonus"])
        self.assertEqual(1, pending["defense_reduction"])
        self.assertEqual(
            pending["base_attack"] + 3 - 1,
            pending["attack_value"],
        )

    def test_declining_defense_defeats_hero_and_reduces_crystal(self) -> None:
        ids = self._place_targets()
        room = self.app._rooms.read(self.room_id)
        attacker_team = room.state.units[self.active_seat].team
        target = next(
            unit
            for unit in room.state.units
            if unit.unit_id in {ids["adjacent_hero_a"], ids["adjacent_hero_b"]}
            and unit.team is not attacker_team
        )
        chosen = self.app.choose_action(
            self.active_seat,
            "primary",
            None,
            self.room_id,
            field(self.payload, "revision"),
        )
        pending = self.app.declare_basic_attack(
            self.active_seat,
            target.unit_id,
            self.room_id,
            field(chosen, "revision"),
        )
        before_life = next(
            team["crystal_life"]
            for team in field(field(pending, "public"), "teams")
            if team["team"] == target.team.value
        )

        defeated = self.app.resolve_basic_defense(
            int(target.seat),
            None,
            self.room_id,
            field(pending, "revision"),
        )

        after_life = next(
            team["crystal_life"]
            for team in field(field(defeated, "public"), "teams")
            if team["team"] == target.team.value
        )
        self.assertEqual(before_life - 1, after_life)
        attacker_team = room.state.units[self.active_seat].team.value
        rewarded = [
            seat["coins"]
            for seat in field(defeated, "public")["seats"]
            if seat["team"] == attacker_team
        ]
        self.assertEqual([1, 1], sorted(rewarded))
        self.assertTrue(
            self.app._rooms.read(self.room_id).state.seats[int(target.seat)].defeated
        )

    def test_negative_attack_still_requires_a_defense_card(self) -> None:
        from goa2.domain import MinionKind

        ids = self._place_targets()
        room = self.app._rooms.read(self.room_id)
        attacker = room.state.units[self.active_seat]
        target = next(
            unit for unit in room.state.units
            if unit.unit_id in {ids["adjacent_hero_a"], ids["adjacent_hero_b"]}
            and unit.team is not attacker.team
        )
        minions = list(room.state.minions)
        friendly = [
            index for index, minion in enumerate(minions)
            if minion.team is target.team
        ]
        for offset, index in enumerate(friendly[:4]):
            minions[index] = replace(
                minions[index],
                kind=MinionKind.MELEE,
                x=target.x + ((1, 0, -1, -1)[offset]),
                y=target.y + ((0, -1, 0, 1)[offset]),
            )
        self.app._rooms.transact(
            self.room_id,
            lambda current: replace(
                current,
                state=replace(current.state, minions=tuple(minions)),
            ),
        )
        payload = self.app.state(self.room_id)
        chosen = self.app.choose_action(
            self.active_seat,
            "primary",
            None,
            self.room_id,
            field(payload, "revision"),
        )
        pending = self.app.declare_basic_attack(
            self.active_seat,
            target.unit_id,
            self.room_id,
            field(chosen, "revision"),
        )
        self.assertLess(
            field(pending, "public")["pending_attack"]["attack_value"],
            0,
        )

        defeated = self.app.resolve_basic_defense(
            int(target.seat),
            None,
            self.room_id,
            field(pending, "revision"),
        )
        self.assertTrue(
            self.app._rooms.read(self.room_id).state.seats[int(target.seat)].defeated
        )
        self.assertIsNone(field(defeated, "public")["pending_attack"])

    def test_invalid_target_is_rejected_atomically(self) -> None:
        ids = self._place_targets()
        chosen = self.app.choose_action(
            self.active_seat,
            "primary",
            None,
            self.room_id,
            field(self.payload, "revision"),
        )
        before = self.app.state(self.room_id)

        with self.assertRaises(ValueError):
            self.app.declare_basic_attack(
                self.active_seat,
                ids["distant_hero"],
                self.room_id,
                field(chosen, "revision"),
            )

        self.assertEqual(before, self.app.state(self.room_id))

    def test_heavy_minion_is_protected_while_an_ally_survives(self) -> None:
        from dataclasses import replace
        from goa2.domain import MinionKind
        from goa2.engine import basic_attack_targets

        ids = self._place_targets()
        room = self.app._rooms.read(self.room_id)
        target = next(
            minion for minion in room.state.minions
            if minion.unit_id == ids["adjacent_minion"]
        )
        minions = tuple(
            replace(minion, kind=MinionKind.HEAVY)
            if minion.unit_id == target.unit_id
            else minion
            for minion in room.state.minions
        )
        state = replace(room.state, minions=minions)

        protected = dict(basic_attack_targets(state, self.active_seat))
        self.assertNotIn(target.unit_id, protected)

        exposed = replace(
            state,
            minions=tuple(
                minion
                for minion in minions
                if minion.team is not target.team or minion.unit_id == target.unit_id
            ),
        )
        self.assertIn(
            target.unit_id,
            dict(basic_attack_targets(exposed, self.active_seat)),
        )

    def test_basic_attack_cannot_use_data_only_completion(self) -> None:
        ids = self._place_targets()
        chosen = self.app.choose_action(
            self.active_seat,
            "primary",
            None,
            self.room_id,
            field(self.payload, "revision"),
        )

        self.assertNotIn(
            "complete_action",
            field(chosen, "private_view")["legal_actions"],
        )
        self.assertIn(
            "declare_basic_attack",
            field(chosen, "private_view")["legal_actions"],
        )

    def test_any_attack_family_card_can_choose_an_adjacent_enemy(self) -> None:
        ids = self._place_targets()
        from dataclasses import replace

        room = self.app._rooms.read(self.room_id)
        seats = list(room.state.seats)
        seats[self.active_seat] = replace(
            seats[self.active_seat],
            action_profile=replace(
                seats[self.active_seat].action_profile,
                primary_family="attack",
                primary_category="攻击",
            ),
        )
        self.app._rooms.transact(
            self.room_id,
            lambda current: replace(
                current,
                state=replace(current.state, seats=tuple(seats)),
            ),
        )
        payload = self.app.state(self.room_id)
        chosen = self.app.choose_action(
            self.active_seat,
            "primary",
            None,
            self.room_id,
            field(payload, "revision"),
        )

        self.assertIn(
            ids["adjacent_minion"],
            {
                target["unit_id"]
                for target in field(chosen, "private_view")["attack_targets"]
            },
        )

    def test_pending_action_can_be_switched_or_cancelled(self) -> None:
        ids = self._place_targets()
        chosen = self.app.choose_action(
            self.active_seat,
            "primary",
            None,
            self.room_id,
            field(self.payload, "revision"),
        )
        private = field(chosen, "private_view")
        self.assertIn("cancel_action", private["legal_actions"])
        self.assertNotIn("skip_action", private["legal_actions"])

        option = next(
            item
            for item in private["action_options"]
            if item["kind"] != "primary"
        )
        switched = self.app.choose_action(
            self.active_seat,
            option["kind"],
            option["source_slot"],
            self.room_id,
            field(chosen, "revision"),
        )
        self.assertEqual(
            option,
            field(switched, "private_view")["pending_action"],
        )

        cancelled = self.app.cancel_action(
            self.active_seat,
            self.room_id,
            field(switched, "revision"),
        )
        self.assertIsNone(field(cancelled, "private_view")["pending_action"])
        self.assertIn(
            "skip_action",
            field(cancelled, "private_view")["legal_actions"],
        )


if __name__ == "__main__":
    unittest.main()
