"""Composition root for the milestone local server."""

from __future__ import annotations

from pathlib import Path

from goa2.application.catalog import CatalogLoader
from goa2.application.domain_engine import DomainEngineAdapter
from goa2.application.ports import EnginePort, RoomStore
from goa2.application.service import RoomApplication

from .memory_store import InMemoryRoomStore


def build_application(
    *,
    data_root: Path | None = None,
    engine: EnginePort | None = None,
    rooms: RoomStore | None = None,
    rooms_file: Path | None = None,
) -> RoomApplication:
    if rooms is not None and rooms_file is not None:
        raise ValueError("rooms and rooms_file are mutually exclusive")
    if rooms is None and rooms_file is not None:
        from .json_store import JsonRoomStore

        rooms = JsonRoomStore(rooms_file)
    catalog = CatalogLoader(data_root).load()
    if engine is None:
        engine = DomainEngineAdapter()
        engine.create_state(catalog)
    return RoomApplication(
        catalog=catalog,
        engine=engine,
        rooms=rooms if rooms is not None else InMemoryRoomStore(),
    )
