---
name: goa2-card-implementation
description: Analyze, specify, implement, test, and review Guards of Atlantis II (GoA2) card effects in the GoA2 v3 project. Use when Codex is asked to interpret a card, resolve card-rule ambiguity, design or update an effect schema, implement card behavior or engine primitives, create comprehensive card tests, audit implementation status, or generate a small Codex task for GoA2 card work.
---

# GoA2 Card Implementation

Implement cards from rules, not from existing accidental behavior.

## Establish Authority

Locate the GoA2 v3 repository, then read:

- `docs/GOA2_RULEBOOK.md`
- `docs/GOA2_EFFECT_SCHEMA_DRAFT.md`
- the card entry in `data/cards.json`

Read `docs/GOA2_IMPLEMENTATION_AUDIT.md` when planning migrations or judging completion.

Apply this authority order:

1. User's latest explicit ruling
2. Formal card text in `data/cards.json`
3. `GOA2_RULEBOOK.md`
4. Tested v3 behavior
5. v2 behavior
6. Early prototypes or external summaries

Never silently use a lower authority to resolve a conflict.

Persist every new or corrected user ruling before implementation. Update the
project rulebook for game rules, the schema draft for effect semantics, and
this Skill or its references when the ruling changes the recurring Codex
workflow or review criteria.

## Choose The Task Mode

Determine the requested mode:

- **Analyze**: explain semantics, split into atomic steps, identify questions.
- **Specify**: produce or revise an Effect Schema without changing production code.
- **Test design**: produce a comprehensive behavior matrix.
- **Implement**: write tests first, add the smallest reusable engine primitive, then wire the card.
- **Review**: inspect code and tests for rule errors, leakage into the main flow, and missing cases.
- **Prompt generation**: produce a small, executable Codex task with acceptance checks.

Do not edit code when the user requests analysis or a plan only.

## Analyze One Card

Produce a card contract before implementation:

1. Identify card ID, hero, level, color, initiative, action family, numeric values, subtype, text, and passive.
2. Split the text into ordered atomic actions. One atomic action handles one target.
3. For every step record:
   - trigger or timing;
   - required or optional;
   - chooser;
   - target kind, team, range, exclusions, immunity;
   - saved result used by later steps;
   - behavior when no legal target exists;
   - events produced;
   - duration and expiry, if any.
4. Distinguish card text from base attack, defense, movement, discard, and defeat rules.
5. Identify reusable engine primitives before proposing a custom handler.

Use `references/schema-and-questions.md` for the contract and ambiguity checklist.

## Stop For Material Ambiguity

Ask the user only when the answer changes legal targets, execution order, mandatory behavior, state duration, replacement behavior, or interaction outcome.

Ask with a concrete minimal scenario and 2-3 interpretations. Do not ask broad questions such as "How should this card work?"

Continue without asking when the answer follows directly from the rulebook or formal card data.

Never invent:

- simultaneous area damage;
- implicit enemy-only targeting where the text does not restrict team;
- optionality without "can/may";
- a card exception to round expiry;
- target ordering not chosen by the source controller;
- priority between novel interacting effects.

Treat wording such as "up to one", "if possible", "otherwise", "after doing so",
and "repeat" as material when it can change optionality, fallback behavior, or
event order. Cite an existing ruling or ask the user; do not settle that
interpretation from ordinary-language intuition alone.

## Build The Effect Schema

Prefer schema-driven steps for ordinary cards. Use a custom handler only when generic steps would be less clear or cannot preserve serializable choices.

Every schema or handler must preserve:

- card and effect source;
- controller and chooser;
- ordered steps;
- pending choices in serializable state;
- duration and expiry;
- deterministic events;
- failure without partial mutation.

Use `references/schema-and-questions.md` for the required shape and custom-handler boundary.

## Design Tests Before Code

Read `references/test-matrix.md`.

Create a test matrix tailored to the card. Include every applicable category and explicitly mark non-applicable categories. Do not claim a card is tested from one happy-path assertion.

At minimum, consider:

- data/contract;
- normal resolution;
- exact lower and upper range boundaries;
- no legal target;
- target taxonomy and team neutrality;
- required versus optional choices;
- chooser ownership;
- empty hand or exhausted resource;
- obstacle, occupancy, and immunity;
- partial sequence failure;
- repeated or multi-target sequencing;
- timing and expiry;
- passive modifiers;
- defense and discard interactions;
- event order and source tracking;
- serialization during pending choice;
- invalid-command atomicity;
- interaction regression with existing effects;
- public/private payload visibility for multiplayer.

For each case state preconditions, command, expected events, expected final state, and forbidden side effects.

## Implement In Small Steps

1. Confirm the working tree and existing user changes.
2. Add failing tests that demonstrate the missing behavior.
3. Run them and record the expected failure.
4. Add or extend a reusable engine primitive.
5. Connect the card schema or handler through Command/Event boundaries.
6. Keep HTTP and UI layers as adapters; do not calculate legality there.
7. Run focused tests, then the full suite.
8. Check serialization, invalid-command atomicity, `git diff --check`, generated files, and personal paths.
9. Update card implementation status only to the level actually proven.

Never add card-ID branches to the main turn, attack, movement, or HTTP flow.

## Status Rules

Use evidence-based states:

- `data_only`: formal data exists.
- `specified`: atomic contract and ambiguities are resolved.
- `implemented`: engine behavior exists but coverage is incomplete.
- `behavior_tested`: card-specific behavior matrix passes.
- `integration_tested`: interactions with phases, other effects, API visibility, and serialization pass.

Do not mark `tested` merely because one smoke test passes.

## Review Output

Lead with findings ordered by severity. Check:

- conflict with the latest rule ruling;
- wrong target taxonomy or team restriction;
- skipped mandatory action;
- inability to skip an optional action;
- incorrect no-target termination;
- simultaneous handling of multiple targets;
- choice made by the wrong player;
- partial mutation on failure;
- lost effect source or duration;
- direct mutation outside the rule engine;
- card hard-coding in the main flow;
- insufficient tests or misleading status.

Use `references/review-checklist.md` for detailed review.

## Required Deliverables

For analysis/specification, return:

1. Card contract
2. Atomic steps
3. Questions requiring user ruling
4. Proposed schema/handler boundary
5. Test matrix
6. Small next implementation task

For implementation, also return:

1. Files changed
2. Failing test observed before the fix
3. Final focused and full test results
4. Card status reached
5. Remaining risks and interactions not yet tested

Keep tasks small: one engine primitive or one closely related card family per implementation turn.
