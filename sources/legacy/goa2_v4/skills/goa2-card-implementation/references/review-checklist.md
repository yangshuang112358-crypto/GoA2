# Card Implementation Review Checklist

## Rules

- Uses the latest user ruling and formal card text.
- Confirms new user rulings were persisted in the appropriate project rule or Skill document.
- Does not infer enemy-only targeting without text.
- Treats unit, hero, and minion according to the rulebook.
- Treats "can/may" as optional and other actions as required.
- Ends the card at the correct impossible atomic action.
- Handles one target per atomic action.
- Lets the source controller order multiple targets.
- Does not add simultaneous area damage.
- Uses uniform movement cost for every passable terrain type.
- A primary-movement card does not expose secondary movement.
- Replacing primary movement with fast movement does not execute primary-action text.
- Defense is chosen by the attacked hero's controller from eligible hand cards.
- Defense below modified attack defeats the defender; skipping defense also defeats them.

## Architecture

- Card behavior enters through Command/Event boundaries.
- Shared phase, movement, attack, defense, or HTTP code contains no card-ID special case.
- New primitive is reusable and narrowly scoped.
- Custom handler has a written justification.
- Pending choices are serializable.
- Effect source, controller, duration, and priority are retained.
- Frontend only displays server-computed legal choices.

## State Safety

- Validation completes before mutation.
- Invalid requests preserve state and revision.
- Immutable input state is not modified.
- Events are deterministic and ordered.
- Private hand/choice information is not leaked.

## Tests

- Failing test was observed before implementation.
- Happy path is not the only test.
- Boundary ranges are covered.
- No-target and later-step failure are covered.
- Required and optional paths are covered.
- Correct player ownership of choices is covered.
- Empty hand/resource state is covered.
- Immunity, obstacle, occupancy, and invalid target cases are considered.
- Timing and expiry are covered.
- Pending state JSON round-trip is covered.
- Existing users of shared primitives remain green.
- Test names describe behavior, not implementation details.

## Status

- `specified`: questions resolved and schema exists.
- `implemented`: code exists, coverage incomplete.
- `behavior_tested`: card matrix passes.
- `integration_tested`: phases, API, visibility, serialization, and interactions pass.

Reject a status promotion unsupported by tests.

## Review Response

Report findings first, ordered by severity, with file and line references.

Then state:

- unresolved rule questions;
- missing test categories;
- status the card actually deserves;
- the smallest next corrective task.
