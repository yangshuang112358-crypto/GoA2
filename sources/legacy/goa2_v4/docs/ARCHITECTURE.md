# GoA2 v4 Architecture

## Source Reuse

Reuse from v2:

- three-column information layout;
- room, four-seat, board, hand, action, log, and debug surfaces;
- formal map and card source data;
- gameplay flow as a requirements inventory, not as production code.

Reuse from v3:

- dark visual palette and design tokens;
- rulebook, Effect Schema, Skill, and test matrix;
- authoritative commands/events, serializable state, revision checks, and
  private player views.

Do not copy v2 `app.py`, frontend legality calculations, mutable room
dictionaries, or card-ID branches. Do not copy v3 singleton hotseat identity,
global stores, card whitelists, or a monolithic rules module.

## Modules

- `src/goa2/domain`: immutable values, state, commands, events, and ports.
- `src/goa2/engine`: phase transitions, reducers, legal queries, primitives.
- `src/goa2/cards`: catalog loading and schema validation only.
- `src/goa2/application`: room use cases and public/private view projection.
- `src/goa2/backend`: HTTP, identity, revision handling, storage adapters.
- `web`: v2 layout with v3 design tokens; no legality calculations.
- `tests`: unit, contract, integration, and end-to-end coverage.

## Milestone 1

1. Start independently without importing v2 or v3.
2. Validate one map with 254 cells and six heroes with 108 cards.
3. Report every card as `data_only` / `未实装未测试`.
4. Display the complete v2-style desktop layout using v3 colors.
5. Support one four-seat hotseat room shell and server-owned state.
6. Expose debug reset and controlled-seat switching.
7. Keep concrete card effects disabled.

