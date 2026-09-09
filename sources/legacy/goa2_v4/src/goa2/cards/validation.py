"""Strict structural validation for milestone-1 card and map data."""

from __future__ import annotations

from collections.abc import Iterable, Mapping, Sequence
from typing import Any, TypeVar

from .dto import (
    ActionSubtypeDTO,
    ActionValueDTO,
    CardCatalogDTO,
    CardDTO,
    HeroDTO,
    MapCatalogDTO,
    MapCellDTO,
    PassiveBonusDTO,
    PrimaryActionDTO,
    SecondaryActionsDTO,
)

CARD_SCHEMA_VERSION = "goa2_cards_v1"
CARD_CATALOG_STATUS = "formal"
IMPLEMENTATION_STATUS = "data_only"
IMPLEMENTATION_STATUS_LABEL = "未实装未测试"
EXPECTED_HERO_COUNT = 6
EXPECTED_CARDS_PER_HERO = 18
EXPECTED_CARD_COUNT = 108
EXPECTED_MAP_CELL_COUNT = 254

_COLOR_KEYS = {
    "金": "gold",
    "红": "red",
    "银": "silver",
    "绿": "green",
    "紫": "purple",
    "蓝": "blue",
}
_PRIMARY_CATEGORIES = {"基础攻击", "基础技能", "攻击", "技能", "防御", "移动", "终极技能"}
_PRIMARY_FAMILIES = {"attack", "skill", "defense", "movement", "ultimate"}
_PASSIVE_TYPES = {None, "先攻", "防御", "攻击", "移动", "范围", "远程"}
_SUBTYPE_TYPES = {"范围", "远程"}
_TEAMS = {"red", "blue", "reserve"}
_MAP_BASES = {None, "red", "blue"}
_MAP_REGIONS = {
    "redFountain",
    "redNear",
    "mid",
    "blueNear",
    "blueFountain",
    "topGrass",
    "bottomGrass",
    "terrain",
}
_MAP_STATES = {
    "empty",
    "terrain",
    "redHeroSpawn",
    "redMeleeSpawn",
    "redRangedSpawn",
    "redHeavySpawn",
    "blueHeroSpawn",
    "blueMeleeSpawn",
    "blueRangedSpawn",
    "blueHeavySpawn",
}
T = TypeVar("T")


class CatalogValidationError(ValueError):
    """Raised when catalog data violates the published base schema."""

    def __init__(self, path: str, message: str) -> None:
        self.path = path
        self.message = message
        super().__init__(f"{path}: {message}")


def parse_card_catalog(data: object) -> CardCatalogDTO:
    root = _object(data, "$", {"schema_version", "status", "heroes"})
    schema_version = _literal_string(
        root["schema_version"], "$.schema_version", CARD_SCHEMA_VERSION
    )
    status = _literal_string(root["status"], "$.status", CARD_CATALOG_STATUS)
    hero_values = _array(root["heroes"], "$.heroes")
    _length(hero_values, "$.heroes", EXPECTED_HERO_COUNT)

    heroes = tuple(
        _parse_hero(value, f"$.heroes[{index}]")
        for index, value in enumerate(hero_values)
    )
    _unique((hero.hero_id for hero in heroes), "$.heroes", "hero_id")
    _unique((hero.name for hero in heroes), "$.heroes", "name")

    cards = tuple(card for hero in heroes for card in hero.cards)
    _length(cards, "$.heroes[*].cards", EXPECTED_CARD_COUNT)
    _unique((card.id for card in cards), "$.heroes[*].cards", "id")
    return CardCatalogDTO(schema_version=schema_version, status=status, heroes=heroes)


def parse_map_catalog(data: object) -> MapCatalogDTO:
    values = _array(data, "$")
    _length(values, "$", EXPECTED_MAP_CELL_COUNT)
    cells = tuple(_parse_map_cell(value, f"$[{index}]") for index, value in enumerate(values))
    _unique((cell.coordinate for cell in cells), "$", "coordinate")
    return MapCatalogDTO(cells=cells)


def _parse_hero(value: object, path: str) -> HeroDTO:
    obj = _object(value, path, {"hero_id", "name", "default_team", "implemented", "cards"})
    hero_id = _nonempty_string(obj["hero_id"], f"{path}.hero_id")
    name = _nonempty_string(obj["name"], f"{path}.name")
    default_team = _enum_string(obj["default_team"], f"{path}.default_team", _TEAMS)
    implemented = _bool(obj["implemented"], f"{path}.implemented")
    if implemented:
        _fail(f"{path}.implemented", "must be false for milestone 1")

    card_values = _array(obj["cards"], f"{path}.cards")
    _length(card_values, f"{path}.cards", EXPECTED_CARDS_PER_HERO)
    cards = tuple(
        _parse_card(card, f"{path}.cards[{index}]", hero_id, name)
        for index, card in enumerate(card_values)
    )
    return HeroDTO(
        hero_id=hero_id,
        name=name,
        default_team=default_team,
        implemented=implemented,
        cards=cards,
    )


def _parse_card(value: object, path: str, hero_id: str, hero_name: str) -> CardDTO:
    keys = {
        "id",
        "name",
        "hero",
        "hero_id",
        "color",
        "color_key",
        "level",
        "initiative",
        "secondary_actions",
        "primary_action",
        "passive_bonus",
        "implementation",
        "implementation_status",
        "implementation_status_label",
    }
    obj = _object(value, path, keys)
    card_id = _nonempty_string(obj["id"], f"{path}.id")
    name = _nonempty_string(obj["name"], f"{path}.name")
    card_hero = _literal_string(obj["hero"], f"{path}.hero", hero_name)
    card_hero_id = _literal_string(obj["hero_id"], f"{path}.hero_id", hero_id)
    if not card_id.startswith(f"{hero_id}-"):
        _fail(f"{path}.id", f"must start with {hero_id!r} followed by '-'")

    color = _enum_string(obj["color"], f"{path}.color", set(_COLOR_KEYS))
    color_key = _literal_string(obj["color_key"], f"{path}.color_key", _COLOR_KEYS[color])
    level = _optional_int(obj["level"], f"{path}.level", minimum=1, maximum=4)
    _validate_level(color_key, level, f"{path}.level")
    initiative = _int(obj["initiative"], f"{path}.initiative", minimum=0)

    implementation = _literal_string(
        obj["implementation"], f"{path}.implementation", IMPLEMENTATION_STATUS
    )
    implementation_status = _literal_string(
        obj["implementation_status"],
        f"{path}.implementation_status",
        IMPLEMENTATION_STATUS,
    )
    implementation_status_label = _literal_string(
        obj["implementation_status_label"],
        f"{path}.implementation_status_label",
        IMPLEMENTATION_STATUS_LABEL,
    )
    return CardDTO(
        id=card_id,
        name=name,
        hero=card_hero,
        hero_id=card_hero_id,
        color=color,
        color_key=color_key,
        level=level,
        initiative=initiative,
        secondary_actions=_parse_secondary_actions(
            obj["secondary_actions"], f"{path}.secondary_actions"
        ),
        primary_action=_parse_primary_action(obj["primary_action"], f"{path}.primary_action"),
        passive_bonus=_parse_passive_bonus(obj["passive_bonus"], f"{path}.passive_bonus"),
        implementation=implementation,
        implementation_status=implementation_status,
        implementation_status_label=implementation_status_label,
    )


def _parse_secondary_actions(value: object, path: str) -> SecondaryActionsDTO:
    obj = _object(value, path, {"defense", "movement"})
    return SecondaryActionsDTO(
        defense=_parse_action_value(obj["defense"], f"{path}.defense", "防御"),
        movement=_parse_action_value(obj["movement"], f"{path}.movement", "移动"),
    )


def _parse_action_value(value: object, path: str, expected_type: str) -> ActionValueDTO:
    obj = _object(value, path, {"type", "has_action", "value"})
    action_type = _literal_string(obj["type"], f"{path}.type", expected_type)
    has_action = _bool(obj["has_action"], f"{path}.has_action")
    action_value = _optional_int(obj["value"], f"{path}.value", minimum=0)
    if has_action != (action_value is not None):
        _fail(f"{path}.value", "must be an integer exactly when has_action is true")
    return ActionValueDTO(type=action_type, has_action=has_action, value=action_value)


def _parse_primary_action(value: object, path: str) -> PrimaryActionDTO:
    obj = _object(
        value,
        path,
        {"category", "family", "text", "value", "exclamation", "subtype"},
    )
    subtype_value = obj["subtype"]
    subtype = None if subtype_value is None else _parse_subtype(subtype_value, f"{path}.subtype")
    return PrimaryActionDTO(
        category=_enum_string(obj["category"], f"{path}.category", _PRIMARY_CATEGORIES),
        family=_enum_string(obj["family"], f"{path}.family", _PRIMARY_FAMILIES),
        text=_nonempty_string(obj["text"], f"{path}.text"),
        value=_int(obj["value"], f"{path}.value", minimum=0),
        exclamation=_bool(obj["exclamation"], f"{path}.exclamation"),
        subtype=subtype,
    )


def _parse_subtype(value: object, path: str) -> ActionSubtypeDTO:
    obj = _object(value, path, {"type", "value"})
    return ActionSubtypeDTO(
        type=_enum_string(obj["type"], f"{path}.type", _SUBTYPE_TYPES),
        value=_int(obj["value"], f"{path}.value", minimum=1),
    )


def _parse_passive_bonus(value: object, path: str) -> PassiveBonusDTO:
    obj = _object(value, path, {"type"})
    bonus_type = obj["type"]
    if bonus_type not in _PASSIVE_TYPES:
        _fail(f"{path}.type", f"must be one of {_display_values(_PASSIVE_TYPES)}")
    return PassiveBonusDTO(type=bonus_type)


def _parse_map_cell(value: object, path: str) -> MapCellDTO:
    obj = _object(value, path, {"x", "y", "state", "obstacle", "lane", "base", "region"})
    state = _enum_string(obj["state"], f"{path}.state", _MAP_STATES)
    obstacle = _bool(obj["obstacle"], f"{path}.obstacle")
    region = _enum_string(obj["region"], f"{path}.region", _MAP_REGIONS)
    base = obj["base"]
    if base not in _MAP_BASES:
        _fail(f"{path}.base", f"must be one of {_display_values(_MAP_BASES)}")
    if obstacle != (state == "terrain"):
        _fail(f"{path}.obstacle", "must be true exactly when state is 'terrain'")
    if (region == "terrain") != obstacle:
        _fail(f"{path}.region", "must be 'terrain' exactly for obstacle cells")
    if base is not None and region != f"{base}Fountain":
        _fail(f"{path}.base", "non-null base must match the cell's fountain region")
    return MapCellDTO(
        x=_int(obj["x"], f"{path}.x"),
        y=_int(obj["y"], f"{path}.y"),
        state=state,
        obstacle=obstacle,
        lane=_bool(obj["lane"], f"{path}.lane"),
        base=base,
        region=region,
    )


def _validate_level(color_key: str, level: int | None, path: str) -> None:
    if color_key in {"gold", "silver"} and level is not None:
        _fail(path, f"must be null for {color_key} cards")
    if color_key == "purple" and level != 4:
        _fail(path, "must be 4 for purple cards")
    if color_key in {"red", "green", "blue"} and level not in {1, 2, 3}:
        _fail(path, f"must be 1, 2, or 3 for {color_key} cards")


def _object(value: object, path: str, expected_keys: set[str]) -> Mapping[str, Any]:
    if not isinstance(value, dict):
        _fail(path, "must be an object")
    actual_keys = set(value)
    missing = sorted(expected_keys - actual_keys)
    extra = sorted(actual_keys - expected_keys)
    if missing or extra:
        details = []
        if missing:
            details.append(f"missing keys {missing}")
        if extra:
            details.append(f"unknown keys {extra}")
        _fail(path, "; ".join(details))
    return value


def _array(value: object, path: str) -> Sequence[object]:
    if not isinstance(value, list):
        _fail(path, "must be an array")
    return value


def _nonempty_string(value: object, path: str) -> str:
    if not isinstance(value, str) or not value.strip():
        _fail(path, "must be a non-empty string")
    return value


def _literal_string(value: object, path: str, expected: str) -> str:
    actual = _nonempty_string(value, path)
    if actual != expected:
        _fail(path, f"must be {expected!r}")
    return actual


def _enum_string(value: object, path: str, allowed: set[str]) -> str:
    actual = _nonempty_string(value, path)
    if actual not in allowed:
        _fail(path, f"must be one of {_display_values(allowed)}")
    return actual


def _bool(value: object, path: str) -> bool:
    if type(value) is not bool:
        _fail(path, "must be a boolean")
    return value


def _int(
    value: object,
    path: str,
    *,
    minimum: int | None = None,
    maximum: int | None = None,
) -> int:
    if type(value) is not int:
        _fail(path, "must be an integer")
    if minimum is not None and value < minimum:
        _fail(path, f"must be at least {minimum}")
    if maximum is not None and value > maximum:
        _fail(path, f"must be at most {maximum}")
    return value


def _optional_int(
    value: object,
    path: str,
    *,
    minimum: int | None = None,
    maximum: int | None = None,
) -> int | None:
    if value is None:
        return None
    return _int(value, path, minimum=minimum, maximum=maximum)


def _length(values: Sequence[object], path: str, expected: int) -> None:
    if len(values) != expected:
        _fail(path, f"must contain exactly {expected} items, got {len(values)}")


def _unique(values: Iterable[T], path: str, field: str) -> None:
    seen: set[T] = set()
    for value in values:
        if value in seen:
            _fail(path, f"duplicate {field} {value!r}")
        seen.add(value)


def _display_values(values: set[object]) -> str:
    return repr(sorted(values, key=lambda value: (value is not None, str(value))))


def _fail(path: str, message: str) -> None:
    raise CatalogValidationError(path, message)
