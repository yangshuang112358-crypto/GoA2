# GoA2 v4 Roadmap

Estimates are active development days, not guaranteed calendar deadlines.
Every milestone must leave a runnable demonstration and a green test suite.

## M0: Independent Foundation

Status: complete.

- independent v4 runtime and repository;
- v2 three-column layout with v3 design tokens;
- 254-cell region-colored map;
- six heroes and 108 cards;
- all cards `data_only` / `未实装未测试`;
- four-seat shell, revision checks, reset, seat switching, initial hands;
- catalog, state, HTTP, and static contracts.

## M1: Planning And Initiative

Status: planning/reveal slice complete.

Estimate for remaining action-choice slice: 1-2 active days.

- hero selection and initial setup;
- private card selection and confirmation; complete
- reveal all four cards together; complete
- server-calculated initiative order; complete
- active-seat action queue;
- primary action, secondary action, fast move, and skip choice shell;
- debug controls for forcing phase transitions.

## M2: Map And Round Core

Estimate: 3-5 active days.

- spawn assignment;
- authoritative movement and fast movement;
- units, minions, occupancy, and obstacles;
- round/turn progression;
- minion-line resolution;
- victory checks and end-of-round expiry;
- debug teleport, defeat, round-end, and resource controls.

## M3: Combat And Progression

Estimate: 3-5 active days.

- basic attack target selection;
- modified attack calculation;
- defense choice and defeat;
- respawn;
- coins, levels, card upgrades, and purple passives;
- public/private multiplayer views.

At the end of M3, v4 should match the main playable process of v2.
Total estimate from M0: 10-15 active development days, about 2-3 weeks.

## M4: v2 Implemented Card Behavior Migration

Estimate: 8-15 active days.

- audit each v2 behavior against current rule authority;
- write a card contract and test matrix before migration;
- build reusable Effect Schema primitives;
- migrate cards in small related families;
- keep unproven cards at `data_only`.

## M5: Parity Hardening

Estimate: 4-7 active days.

- interaction and regression matrix;
- reconnect and persistence;
- multiple browser/player identities;
- replay/event log;
- UI polish and full debug tooling;
- parity audit against v2.

Full v2 implemented-behavior parity is estimated at 20-35 active development
days, about 4-7 weeks.
