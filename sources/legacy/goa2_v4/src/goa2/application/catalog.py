"""Load immutable source catalogs for application use cases."""

from __future__ import annotations

from copy import deepcopy
from dataclasses import dataclass
import json
from pathlib import Path
from typing import Any


INITIAL_HAND_COLORS = frozenset({"gold", "silver"})
LEVEL_ONE_COLORS = frozenset({"red", "green", "blue"})


@dataclass(frozen=True, slots=True)
class CatalogBundle:
    cards: dict[str, Any]
    map_cells: list[dict[str, Any]]

    @property
    def hero_count(self) -> int:
        return len(self.cards.get("heroes", ()))

    @property
    def card_count(self) -> int:
        return sum(
            len(hero.get("cards", ())) for hero in self.cards.get("heroes", ())
        )

    def seat_hero(self, seat: int) -> dict[str, Any]:
        heroes = self.cards.get("heroes", ())
        if isinstance(seat, bool) or not isinstance(seat, int) or not 0 <= seat < 4:
            raise ValueError("seat must be an integer from 0 to 3")
        if len(heroes) < 4:
            raise ValueError("catalog must contain at least four heroes")
        return heroes[seat]

    def hero(self, hero_id: str) -> dict[str, Any]:
        if not isinstance(hero_id, str) or not hero_id:
            raise ValueError("hero_id must be a non-empty string")
        for hero in self.cards.get("heroes", ()):
            if hero.get("hero_id") == hero_id:
                return hero
        raise ValueError(f"unknown hero_id: {hero_id!r}")

    def initial_hand(self, seat: int) -> list[dict[str, Any]]:
        return self.initial_hand_for_hero(self.seat_hero(seat)["hero_id"])

    def initial_hand_for_hero(self, hero_id: str) -> list[dict[str, Any]]:
        return [
            card
            for card in self.hero(hero_id).get("cards", ())
            if card.get("color_key") in INITIAL_HAND_COLORS
            or (
                card.get("color_key") in LEVEL_ONE_COLORS
                and card.get("level") == 1
            )
        ]

    def initial_hand_card_ids(self, seat: int) -> tuple[str, ...]:
        return tuple(card["id"] for card in self.initial_hand(seat))

    def initial_hand_card_ids_for_hero(self, hero_id: str) -> tuple[str, ...]:
        return tuple(card["id"] for card in self.initial_hand_for_hero(hero_id))

    def upgrade_candidates(
        self, seat: int, color: str, level: int
    ) -> tuple[dict[str, Any], ...]:
        return self.upgrade_candidates_for_hero(
            self.seat_hero(seat)["hero_id"], color, level
        )

    def upgrade_candidates_for_hero(
        self, hero_id: str, color: str, level: int
    ) -> tuple[dict[str, Any], ...]:
        hero = self.hero(hero_id)
        return tuple(
            card
            for card in hero["cards"]
            if card.get("color_key") == color and card.get("level") == level
        )

    def require_hand_card(self, seat: int, card_id: Any) -> dict[str, Any]:
        if not isinstance(card_id, str) or not card_id:
            raise ValueError("card_id must be a non-empty string")
        for card in self.initial_hand(seat):
            if card.get("id") == card_id:
                return card
        raise ValueError(f"card {card_id!r} is not in seat {seat}'s hand")

    def initiative(self, card_id: str) -> int:
        return self.card(card_id)["initiative"]

    def card(self, card_id: str) -> dict[str, Any]:
        for hero in self.cards.get("heroes", ()):
            for card in hero.get("cards", ()):
                if card.get("id") == card_id:
                    return card
        raise ValueError(f"unknown card_id: {card_id!r}")

    def action_profile(self, card_id: str) -> dict[str, Any]:
        card = self.card(card_id)
        primary = card.get("primary_action", {})
        secondary = card.get("secondary_actions", {}).get("movement", {})
        family = primary.get("family")
        category = primary.get("category")
        if not isinstance(family, str) or not family:
            raise ValueError(f"card {card_id!r} has invalid primary family")
        if not isinstance(category, str) or not category:
            raise ValueError(f"card {card_id!r} has invalid primary category")
        primary_movement = family == "movement"
        secondary_movement = bool(secondary.get("has_action")) and not primary_movement
        sources = []
        if primary_movement:
            sources.append("primary")
        if secondary_movement:
            sources.append("secondary")
        return {
            "implementation_status": card.get("implementation_status", "data_only"),
            "primary_family": family,
            "primary_category": category,
            "primary_is_movement": primary_movement,
            "secondary_movement": secondary_movement,
            "fast_move_sources": sources,
            "primary_value": primary.get("value", 0),
            "secondary_movement_value": (
                secondary.get("value") if secondary_movement else None
            ),
        }

    def defense_profile(self, card_id: str) -> dict[str, Any]:
        card = self.card(card_id)
        defense = card.get("secondary_actions", {}).get("defense", {})
        primary = card.get("primary_action", {})
        has_secondary = bool(defense.get("has_action"))
        has_primary = primary.get("family") == "defense"
        exclamation = has_primary and bool(primary.get("exclamation"))
        if not has_secondary and not has_primary:
            raise ValueError(f"card {card_id!r} is not a defense card")
        return {
            "card_id": card_id,
            "defense_value": (
                defense.get("value")
                if has_secondary else primary.get("value", 0)
            ),
            "source": "secondary" if has_secondary else "primary",
            "exclamation": exclamation,
        }

    def to_dto(self) -> dict[str, Any]:
        heroes = deepcopy(self.cards.get("heroes", []))
        return {
            "schema_version": self.cards.get("schema_version"),
            "status": self.cards.get("status"),
            "heroes": heroes,
            "map": deepcopy(self.map_cells),
            "summary": {
                "hero_count": self.hero_count,
                "card_count": self.card_count,
                "map_cell_count": len(self.map_cells),
            },
        }


class CatalogLoader:
    def __init__(self, data_root: Path | None = None) -> None:
        self._data_root = data_root or Path(__file__).resolve().parents[3] / "data"

    def load(self) -> CatalogBundle:
        cards = self._load_json("cards.json")
        map_cells = self._load_json("map.json")
        if not isinstance(cards, dict) or not isinstance(map_cells, list):
            raise ValueError("catalog roots have unexpected shapes")
        return CatalogBundle(cards=cards, map_cells=map_cells)

    def _load_json(self, filename: str) -> Any:
        path = self._data_root / filename
        with path.open("r", encoding="utf-8") as source:
            return json.load(source)
