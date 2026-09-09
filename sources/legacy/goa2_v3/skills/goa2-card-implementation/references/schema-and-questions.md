# Schema And Questions

## Card Contract Template

```yaml
card:
  id:
  hero:
  action_family:
  numeric_values:
  formal_text:

timing:
  trigger:
  window:

steps:
  - id:
    type:
    required:
    chooser:
    legal_targets:
    store_as:
    on_no_legal_target:
    expected_events:

duration:
  kind:
  expires:

engine_dependencies:
  reusable_primitives:
  custom_handler_reason:

open_questions: []
```

## Atomic Step Fields

- `id`: unique within the card.
- `type`: `choose_target`, `choose_cell`, `choose_card_from_hand`, `move`, `push`, `place`, `swap`, `attack`, `defend`, `discard`, `retrieve`, `defeat`, `add_effect`, `remove_effect`, `repeat`, `branch`, `end_card`, or `custom`.
- `required`: false only when the formal text says the action is optional.
- `chooser`: source controller, target controller, captain, or another explicit player.
- `legal_targets`: kind, team, range, exclusions, immunity, occupancy, and state filters.
- `store_as`: stable reference for later steps.
- `on_no_legal_target`: normally `end_card` for an impossible required atomic action; use another behavior only when rules say so.
- `expected_events`: facts produced by successful resolution.

One step handles one target. Multiple targets are sequential steps or a `repeat` over one target at a time.

## Target Defaults

When card text does not restrict team:

- unit = heroes and minions from both teams;
- hero = heroes from both teams;
- minion = minions from both teams.

Do not infer enemy-only from an attack-like narrative. Use the formal target wording.

## Questions Worth Asking

Ask only if the answer changes behavior:

Do not silently resolve quantity and fallback phrases such as `up to one`,
`if possible`, `otherwise`, `after doing so`, or `repeat`. They require either
an existing GoA2 ruling or a concrete question when different readings change
whether a step is optional, mandatory, skipped, or replaced.

### Target

- Does this include the source?
- Does "another" exclude only the previous target or also the source?
- Does the target have to remain legal at resolution?
- Does immunity prevent selection, effect, or both?

### Timing

- Is this before attack, after attack, after successful attack, after defeat, turn end, or round end?
- If the source is defeated before the delayed effect, does it remain?

### Choice

- Who chooses the target, card, cell, path, or order?
- Is skipping allowed?
- If no choice exists, does the step end the card or continue?

### Sequence

- Does failure of a later required step end only the remaining card text?
- Does a repeated effect re-evaluate legal targets and conditions each time?
- Can the same target be selected again?

### Duration And Interaction

- What is the exact expiry?
- Is the effect additive, overriding, preventing, or replacing?
- If multiple effects apply, which one is evaluated first?

## Good Question Format

```text
Card: <id/name>
Minimal scenario: <positions, cards, effects>
Ambiguous step: <exact atomic step>

A. <interpretation and result>
B. <interpretation and result>
C. <optional third interpretation>

This changes: <legal targets/event order/duration/test>.
```

## Custom Handler Boundary

Use a custom handler only when:

- the card has a unique loop or replacement that generic steps cannot express clearly;
- adding a generic primitive would serve only this one card and make the schema harder to understand;
- the handler still returns serializable pending choices and deterministic events.

A custom handler must not:

- read browser state;
- mutate global state in place;
- bypass Command/Event validation;
- hide chooser or source;
- perform multiple-target simultaneous damage;
- add card-ID logic to a shared main-flow function.
