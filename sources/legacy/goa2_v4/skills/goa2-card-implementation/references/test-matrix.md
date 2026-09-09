# Comprehensive Card Test Matrix

Use this as a selection matrix, not a requirement to create meaningless tests. Mark every category `covered`, `not applicable`, or `blocked by ruling`.

## Test Case Format

```yaml
name:
category:
preconditions:
command:
expected_events:
expected_state:
forbidden_side_effects:
```

## 1. Data And Contract

- Card ID, hero, color, level, initiative, action family, values, subtype, text, and passive parse correctly.
- Schema references valid step IDs and supported primitives.
- Every effect has source, controller, and duration when applicable.
- Implementation status matches evidence.

## 2. Normal Resolution

- Minimum normal happy path.
- Each alternative legal choice.
- Optional branch chosen.
- Optional branch skipped.
- Required steps execute in formal text order.
- Event order matches atomic step order.

## 3. Range And Geometry

- Exact minimum range.
- Exact maximum range.
- One cell outside maximum.
- Adjacent versus non-adjacent.
- Ranged/range passive at zero and with bonuses.
- Alignment or direction restrictions.
- Obstacle blocks path or line when applicable.
- Occupied destination rejected when applicable.

## 4. Target Taxonomy And Team

- Friendly hero.
- Enemy hero.
- Friendly minion.
- Enemy minion.
- Source itself.
- Previous target excluded by "another".
- Heavy minion restrictions.
- Defeated, spawning, or otherwise invalid unit.

When text says unit/hero/minion without team restriction, test both teams.

## 5. No Legal Target And Partial Failure

- No legal target for the first required step.
- First step succeeds, later required step has no legal target.
- Optional step has no legal target.
- Target becomes invalid between choice and resolution.
- Required action cannot be executed because of immunity, occupancy, or resource state.
- Card ends at the correct point without rolling back earlier completed steps unless rules require replacement-style atomicity.

## 6. Choice Ownership

- Source controller makes source choices.
- Target controller chooses their discarded card.
- Captain makes captain decisions.
- A different seat cannot submit the choice.
- Public payload hides private choices from other players.
- Reconnection or serialized pending choice preserves chooser identity.

## 7. Hand, Discard, Retrieve, And Swap

- Valid card selected from hand.
- Invalid/non-hand card rejected.
- Empty hand behavior.
- Mandatory discard cannot be skipped when possible.
- Optional discard can be skipped.
- Defense discard does not trigger primary action text.
- Retrieve changes status to in-hand.
- Swap exchanges cards and all statuses specified by rules.
- Round-end recovery restores the correct five-card set.

## 8. Attack And Defense

- Base damage and passive modifier.
- Minion-based damage modifiers.
- Defense exactly equals damage.
- Defense below damage.
- Exclamation defense.
- No defense/accept defeat.
- Attack-before, defense discard, defense result, attack-after, attack-end, defense-end order.
- Ranged immunity prevents selection.
- Defeat-triggered continuation or repeat.

## 9. Movement, Push, Place, And Swap

- Exact movement budget.
- Segmented movement.
- Obstacle and occupied cells.
- Fast movement alternative.
- Forced movement ignores unable-to-move when rules say so.
- Push blocked immediately still counts as completing the push atomic action.
- Controller chooses placement cell.
- Invalid placement leaves state unchanged.

## 10. Repetition And Multiple Targets

- Source controller selects target order.
- One atomic action resolves one target.
- Legal targets are re-evaluated before every repeat.
- Repeat condition true once, multiple times, then false.
- Player declines an optional repeat.
- Previously selected target may or may not repeat according to the ruling.
- No simultaneous area damage is introduced.

## 11. Timing And Duration

- Effect inactive before its stated timing.
- Active during the correct turn.
- Expires at turn, next turn, or round boundary.
- Next-turn effect created in the final turn of a round expires immediately.
- No card exception to final-turn next-turn expiry.
- Purple ultimate remains active while owned.
- Source defeat or card state changes do not incorrectly remove/preserve an effect.

## 12. Trigger, Replacement, Prevention, Immunity

- Trigger condition false.
- Trigger condition true once.
- Trigger fires repeatedly only when permitted.
- Replacement prevents the original event.
- Prevention leaves expected event trace.
- Immunity prevents targeting and effect.
- Multiple applicable effects use a user-confirmed order.
- Effect source remains traceable.

## 13. State Integrity

- Successful command increments revision exactly as designed.
- Invalid command changes no state or revision.
- Pending choice serializes and round-trips.
- Event log is deterministic.
- Original immutable state remains unchanged.
- No UI-computed legality is trusted.

## 14. Integration Regression

- Planning/reveal/action phase restrictions.
- Decision coin and initiative ordering.
- Captain choice.
- Respawn before card resolution.
- Minion and front progression.
- Upgrade/passive interaction.
- Existing cards using the same primitive still pass.
- HTTP API maps errors without partial mutation.
- Four-seat hotseat and future private player views do not leak cards.

## Coverage Standard

`behavior_tested` requires:

- all applicable normal and boundary cases;
- no-target behavior;
- required/optional semantics;
- chooser ownership;
- failure atomicity;
- serialization for pending choices;
- at least one interaction with each engine primitive the card uses.

`integration_tested` additionally requires:

- phase and API coverage;
- visibility/privacy coverage;
- timing/expiry coverage;
- regression with at least one other effect when interaction is possible.
