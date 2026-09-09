from __future__ import annotations

import json
from collections.abc import Mapping
from typing import Any


def public_value(value: Any) -> Any:
    if isinstance(value, Mapping):
        return dict(value)
    for method_name in ("to_dict", "as_dict"):
        method = getattr(value, method_name, None)
        if callable(method):
            return method()
    return value


def field(value: Any, *names: str) -> Any:
    value = public_value(value)
    if isinstance(value, Mapping):
        for name in names:
            if name in value:
                return value[name]
    for name in names:
        if hasattr(value, name):
            return getattr(value, name)
    raise AssertionError(f"Missing required field; expected one of {names!r}")


def catalog_parts(catalog: Any) -> tuple[list[Any], list[Any], list[Any]]:
    raw = public_value(catalog)
    card_root = field(raw, "cards") if _has_field(raw, "cards") else raw
    map_root = field(raw, "map") if _has_field(raw, "map") else raw
    heroes = list(field(card_root, "heroes"))
    direct_cards = field(card_root, "cards") if _has_field(card_root, "cards") else None
    cards = list(direct_cards) if direct_cards is not None else [
        card
        for hero in heroes
        for card in field(hero, "cards")
    ]
    cells = (
        list(map_root)
        if isinstance(map_root, (list, tuple))
        else list(field(map_root, "cells", "map_cells"))
    )
    return heroes, cards, cells


def _has_field(value: Any, name: str) -> bool:
    value = public_value(value)
    return name in value if isinstance(value, Mapping) else hasattr(value, name)


def state_snapshot(state: Any) -> dict[str, Any]:
    raw = public_value(state)
    if not isinstance(raw, Mapping):
        raise AssertionError("State must expose to_dict()/as_dict() or be a mapping")
    return json.loads(json.dumps(raw, sort_keys=True))


def resolve_pending_initiative(app: Any, room_id: str, payload: Any) -> Any:
    public = field(payload, "public")
    choice = public.get("pending_initiative_choice")
    if not choice:
        return payload
    chooser = int(choice["chooser"])
    if field(payload, "controlled_seat") != chooser:
        payload = app.control(chooser, room_id, field(payload, "revision"))
    return app.resolve_initiative_choice(
        chooser,
        choice["choice_id"],
        int(choice["candidate_seats"][0]),
        room_id,
        field(payload, "revision"),
    )


def skip_current_action(app: Any, room_id: str, payload: Any) -> Any:
    payload = resolve_pending_initiative(app, room_id, payload)
    active_seat = int(field(field(payload, "public"), "active_seat"))
    if field(payload, "controlled_seat") != active_seat:
        payload = app.control(
            active_seat,
            room_id,
            field(payload, "revision"),
        )
    return app.skip_action(
        active_seat,
        room_id,
        field(payload, "revision"),
    )
