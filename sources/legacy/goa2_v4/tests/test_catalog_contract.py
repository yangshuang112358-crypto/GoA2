from __future__ import annotations

import unittest

from tests.contract_support import catalog_parts, field


class CatalogContractTests(unittest.TestCase):
    def test_catalog_has_six_heroes_108_cards_and_254_cells(self) -> None:
        from goa2.cards.catalog import load_catalogs

        heroes, cards, cells = catalog_parts(load_catalogs())

        self.assertEqual(6, len(heroes))
        self.assertEqual(108, len(cards))
        self.assertEqual(254, len(cells))
        self.assertEqual(6, len({field(hero, "hero_id", "id") for hero in heroes}))
        self.assertEqual(108, len({field(card, "id", "card_id") for card in cards}))
        self.assertEqual(
            254,
            len({(field(cell, "x", "q"), field(cell, "y", "r")) for cell in cells}),
        )

    def test_every_card_is_data_only(self) -> None:
        from goa2.cards.catalog import load_catalogs

        _, cards, _ = catalog_parts(load_catalogs())

        for card in cards:
            with self.subTest(card=field(card, "id", "card_id")):
                self.assertEqual(
                    "data_only",
                    field(card, "implementation_status", "implementation"),
                )

    def test_defense_profiles_cover_secondary_primary_and_exclamation(self) -> None:
        from goa2.application.catalog import CatalogLoader

        catalog = CatalogLoader().load()
        cards = [
            card
            for hero in catalog.cards["heroes"]
            for card in hero["cards"]
        ]
        secondary = next(
            card for card in cards
            if card["secondary_actions"]["defense"]["has_action"]
        )
        primary = next(
            card for card in cards
            if card["primary_action"]["family"] == "defense"
            and not card["primary_action"]["exclamation"]
        )
        exclamation = next(
            card for card in cards
            if card["primary_action"]["family"] == "defense"
            and card["primary_action"]["exclamation"]
        )

        self.assertEqual("secondary", catalog.defense_profile(secondary["id"])["source"])
        self.assertEqual("primary", catalog.defense_profile(primary["id"])["source"])
        self.assertFalse(catalog.defense_profile(primary["id"])["exclamation"])
        self.assertTrue(catalog.defense_profile(exclamation["id"])["exclamation"])


if __name__ == "__main__":
    unittest.main()
