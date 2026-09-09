"""Stable, immutable data-transfer objects for formal GoA2 catalogs."""

from __future__ import annotations

from dataclasses import dataclass


@dataclass(frozen=True, slots=True)
class ActionValueDTO:
    type: str
    has_action: bool
    value: int | None


@dataclass(frozen=True, slots=True)
class SecondaryActionsDTO:
    defense: ActionValueDTO
    movement: ActionValueDTO


@dataclass(frozen=True, slots=True)
class ActionSubtypeDTO:
    type: str
    value: int


@dataclass(frozen=True, slots=True)
class PrimaryActionDTO:
    category: str
    family: str
    text: str
    value: int
    exclamation: bool
    subtype: ActionSubtypeDTO | None


@dataclass(frozen=True, slots=True)
class PassiveBonusDTO:
    type: str | None


@dataclass(frozen=True, slots=True)
class CardDTO:
    id: str
    name: str
    hero: str
    hero_id: str
    color: str
    color_key: str
    level: int | None
    initiative: int
    secondary_actions: SecondaryActionsDTO
    primary_action: PrimaryActionDTO
    passive_bonus: PassiveBonusDTO
    implementation: str
    implementation_status: str
    implementation_status_label: str


@dataclass(frozen=True, slots=True)
class HeroDTO:
    hero_id: str
    name: str
    default_team: str
    implemented: bool
    cards: tuple[CardDTO, ...]


@dataclass(frozen=True, slots=True)
class CardCatalogDTO:
    schema_version: str
    status: str
    heroes: tuple[HeroDTO, ...]

    @property
    def cards(self) -> tuple[CardDTO, ...]:
        return tuple(card for hero in self.heroes for card in hero.cards)

    def hero_by_id(self, hero_id: str) -> HeroDTO:
        for hero in self.heroes:
            if hero.hero_id == hero_id:
                return hero
        raise KeyError(hero_id)

    def card_by_id(self, card_id: str) -> CardDTO:
        for card in self.cards:
            if card.id == card_id:
                return card
        raise KeyError(card_id)


@dataclass(frozen=True, slots=True)
class MapCellDTO:
    x: int
    y: int
    state: str
    obstacle: bool
    lane: bool
    base: str | None
    region: str

    @property
    def coordinate(self) -> tuple[int, int]:
        return self.x, self.y


@dataclass(frozen=True, slots=True)
class MapCatalogDTO:
    cells: tuple[MapCellDTO, ...]

    def cell_at(self, x: int, y: int) -> MapCellDTO:
        for cell in self.cells:
            if cell.x == x and cell.y == y:
                return cell
        raise KeyError((x, y))


@dataclass(frozen=True, slots=True)
class CatalogDTO:
    cards: CardCatalogDTO
    map: MapCatalogDTO
