# v2/v3 Migration Inventory

## Data

- `data/cards.json`: identical in v2 and v3; 6 heroes and 108 cards.
- `data/map.json`: identical in v2 and v3; 254 cells and 8 region values.
- v2 card photographs and OCR drafts remain source evidence outside v4.
- v4 does not copy implementation or test status from either version.

## User Interface

Use v2 information architecture:

- left: room controls and four player panels;
- center top: phase, revealed cards, and current actions;
- center: scrollable region-colored hex map;
- center bottom: hero summary and horizontal hand;
- right: event log;
- persistent debug controls.

Use v3 visual language:

- dark background and translucent panels;
- gold focus/accent;
- blue and red team colors;
- red, green, blue, purple, gold, and silver card accents.

## Rules And Engineering

Use v3 rulebook, Effect Schema, Skill, Command/Event boundaries, serialized
state, revision checks, and test matrix as design inputs.

Do not copy either runtime. v2 is a product behavior reference; v3 is an
architecture experiment. v4 owns new production implementations and tests.

