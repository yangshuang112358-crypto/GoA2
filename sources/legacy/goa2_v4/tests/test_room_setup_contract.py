from __future__ import annotations

from dataclasses import replace
import unittest


class RoomSetupContractTests(unittest.TestCase):
    def setUp(self) -> None:
        from goa2.application.catalog import CatalogLoader
        from goa2.application.domain_engine import DomainEngineAdapter

        self.catalog = CatalogLoader().load()
        self.engine = DomainEngineAdapter()

    def test_catalog_supports_hero_id_lookups(self) -> None:
        hero = self.catalog.hero("tigerclaw")
        hand = self.catalog.initial_hand_for_hero("tigerclaw")
        candidates = self.catalog.upgrade_candidates_for_hero(
            "tigerclaw", "red", 2
        )

        self.assertEqual("tigerclaw", hero["hero_id"])
        self.assertEqual(5, len(hand))
        self.assertEqual(
            tuple(card["id"] for card in hand),
            self.catalog.initial_hand_card_ids_for_hero("tigerclaw"),
        )
        self.assertEqual(2, len(candidates))
        self.assertTrue(all(card["color_key"] == "red" for card in candidates))
        self.assertTrue(all(card["level"] == 2 for card in candidates))

        with self.assertRaises(ValueError):
            self.catalog.hero("unknown-hero")

    def test_room_state_serializes_four_distinct_hero_ids(self) -> None:
        from goa2.domain import RoomState, create_initial_room_state

        state = create_initial_room_state(
            hero_ids=("tigerclaw", "sabina", "wasp", "brogan")
        )

        self.assertEqual(
            ("tigerclaw", "sabina", "wasp", "brogan"),
            state.hero_ids,
        )
        self.assertEqual(state, RoomState.from_dict(state.to_dict()))

        with self.assertRaises(ValueError):
            create_initial_room_state(
                hero_ids=("wasp", "wasp", "brogan", "arien")
            )

    def test_legacy_room_state_without_hero_ids_uses_default_selection(self) -> None:
        from goa2.domain import RoomState, create_initial_room_state

        legacy = create_initial_room_state().to_dict()
        del legacy["hero_ids"]

        restored = RoomState.from_dict(legacy)

        self.assertEqual(
            ("wasp", "shargatha", "brogan", "arien"),
            restored.hero_ids,
        )

    def test_create_state_defaults_to_first_four_catalog_heroes(self) -> None:
        state = self.engine.create_state(self.catalog)

        self.assertEqual(
            ("wasp", "shargatha", "brogan", "arien"),
            state.hero_ids,
        )
        for seat, hero_id in enumerate(state.hero_ids):
            self.assertEqual(
                self.catalog.initial_hand_card_ids_for_hero(hero_id),
                state.seats[seat].active_card_ids,
            )

    def test_create_state_uses_selected_heroes_and_parameters(self) -> None:
        hero_ids = ("sabina", "tigerclaw", "arien", "wasp")

        state = self.engine.create_state(
            self.catalog,
            starting_crystal_life=11,
            frontline_victory_marks=5,
            hero_ids=hero_ids,
        )

        self.assertEqual(hero_ids, state.hero_ids)
        self.assertEqual(11, state.starting_crystal_life)
        self.assertEqual(5, state.frontline_victory_marks)
        self.assertEqual([11, 11], [team.crystal_life for team in state.teams])
        for seat, hero_id in enumerate(hero_ids):
            self.assertEqual(
                self.catalog.initial_hand_card_ids_for_hero(hero_id),
                state.seats[seat].active_card_ids,
            )

    def test_configure_room_replaces_heroes_hands_and_starting_values(self) -> None:
        initial = self.engine.create_state(self.catalog)
        hero_ids = ("tigerclaw", "sabina", "wasp", "shargatha")

        configured = self.engine.configure_room(
            initial,
            starting_crystal_life=9,
            frontline_victory_marks=4,
            hero_ids=hero_ids,
        )

        self.assertEqual(initial.revision + 1, configured.revision)
        self.assertEqual(hero_ids, configured.hero_ids)
        self.assertEqual(9, configured.starting_crystal_life)
        self.assertEqual(4, configured.frontline_victory_marks)
        self.assertEqual([9, 9], [team.crystal_life for team in configured.teams])
        self.assertEqual(initial.board, configured.board)
        self.assertEqual(initial.units, configured.units)
        self.assertEqual(initial.minions, configured.minions)
        self.assertEqual(initial.decision_coin, configured.decision_coin)
        for seat, hero_id in enumerate(hero_ids):
            self.assertEqual(
                self.catalog.initial_hand_card_ids_for_hero(hero_id),
                configured.seats[seat].active_card_ids,
            )

    def test_reset_preserves_room_setup(self) -> None:
        initial = self.engine.create_state(self.catalog)
        hero_ids = ("tigerclaw", "sabina", "wasp", "shargatha")
        configured = self.engine.configure_room(
            initial,
            starting_crystal_life=10,
            frontline_victory_marks=6,
            hero_ids=hero_ids,
        )

        reset = self.engine.reset(configured)

        self.assertEqual(configured.revision + 1, reset.revision)
        self.assertEqual(hero_ids, reset.hero_ids)
        self.assertEqual(10, reset.starting_crystal_life)
        self.assertEqual(6, reset.frontline_victory_marks)
        for seat, hero_id in enumerate(hero_ids):
            self.assertEqual(
                self.catalog.initial_hand_card_ids_for_hero(hero_id),
                reset.seats[seat].active_card_ids,
            )

    def test_invalid_or_started_configuration_fails_without_mutating_state(self) -> None:
        from goa2.domain import Phase

        initial = self.engine.create_state(self.catalog)
        invalid_inputs = (
            {
                "starting_crystal_life": 0,
                "frontline_victory_marks": 3,
                "hero_ids": initial.hero_ids,
            },
            {
                "starting_crystal_life": 7,
                "frontline_victory_marks": 0,
                "hero_ids": initial.hero_ids,
            },
            {
                "starting_crystal_life": 7,
                "frontline_victory_marks": 3,
                "hero_ids": ("wasp", "wasp", "brogan", "arien"),
            },
            {
                "starting_crystal_life": 7,
                "frontline_victory_marks": 3,
                "hero_ids": ("wasp", "shargatha", "brogan", "unknown"),
            },
        )
        for values in invalid_inputs:
            with self.subTest(values=values):
                before = initial.to_dict()
                with self.assertRaises(ValueError):
                    self.engine.configure_room(initial, **values)
                self.assertEqual(before, initial.to_dict())

        started = replace(initial, phase=Phase.ROUND_END)
        before = started.to_dict()
        with self.assertRaises(ValueError):
            self.engine.configure_room(
                started,
                starting_crystal_life=7,
                frontline_victory_marks=3,
                hero_ids=initial.hero_ids,
            )
        self.assertEqual(before, started.to_dict())


if __name__ == "__main__":
    unittest.main()
