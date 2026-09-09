from __future__ import annotations

from goa2.domain import SeatState


PASSIVE_INDEX = {
    "attack": 0,
    "defense": 1,
    "movement": 2,
    "initiative": 3,
    "range": 4,
    "ranged": 5,
}

PASSIVE_TYPE_INDEX = {
    "攻击": PASSIVE_INDEX["attack"],
    "防御": PASSIVE_INDEX["defense"],
    "移动": PASSIVE_INDEX["movement"],
    "先攻": PASSIVE_INDEX["initiative"],
    "范围": PASSIVE_INDEX["range"],
    "远程": PASSIVE_INDEX["ranged"],
}


def passive_bonus(seat: SeatState, attribute: str) -> int:
    return seat.passive_bonuses[PASSIVE_INDEX[attribute]]


def modified_value(seat: SeatState, attribute: str, base_value: int) -> int:
    return base_value + passive_bonus(seat, attribute)


def passive_type_index(passive_type: str | None) -> int | None:
    return PASSIVE_TYPE_INDEX.get(passive_type)
