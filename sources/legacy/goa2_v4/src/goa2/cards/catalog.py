"""Load formal GoA2 JSON catalogs into validated DTOs."""

from __future__ import annotations

import json
from pathlib import Path
from typing import Any

from .dto import CatalogDTO, CardCatalogDTO, MapCatalogDTO
from .validation import CatalogValidationError, parse_card_catalog, parse_map_catalog

DEFAULT_DATA_DIR = Path(__file__).resolve().parents[3] / "data"


def load_card_catalog(path: str | Path | None = None) -> CardCatalogDTO:
    source = Path(path) if path is not None else DEFAULT_DATA_DIR / "cards.json"
    return parse_card_catalog(_read_json(source))


def load_map_catalog(path: str | Path | None = None) -> MapCatalogDTO:
    source = Path(path) if path is not None else DEFAULT_DATA_DIR / "map.json"
    return parse_map_catalog(_read_json(source))


def load_catalogs(data_dir: str | Path | None = None) -> CatalogDTO:
    directory = Path(data_dir) if data_dir is not None else DEFAULT_DATA_DIR
    return CatalogDTO(
        cards=load_card_catalog(directory / "cards.json"),
        map=load_map_catalog(directory / "map.json"),
    )


def load_catalog(data_dir: str | Path | None = None) -> CatalogDTO:
    return load_catalogs(data_dir)


def _read_json(path: Path) -> object:
    try:
        with path.open("r", encoding="utf-8") as source:
            return json.load(source, object_pairs_hook=_reject_duplicate_keys)
    except CatalogValidationError:
        raise
    except (OSError, UnicodeError, json.JSONDecodeError) as exc:
        raise CatalogValidationError(str(path), f"cannot load JSON: {exc}") from exc


def _reject_duplicate_keys(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    result: dict[str, Any] = {}
    for key, value in pairs:
        if key in result:
            raise CatalogValidationError("$", f"duplicate JSON key {key!r}")
        result[key] = value
    return result
