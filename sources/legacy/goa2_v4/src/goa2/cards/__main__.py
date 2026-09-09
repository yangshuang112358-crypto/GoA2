"""Validate the bundled milestone-1 catalogs."""

from .catalog import load_catalogs


def main() -> None:
    catalogs = load_catalogs()
    print(
        f"validated heroes={len(catalogs.cards.heroes)} "
        f"cards={len(catalogs.cards.cards)} map_cells={len(catalogs.map.cells)}"
    )


if __name__ == "__main__":
    main()
