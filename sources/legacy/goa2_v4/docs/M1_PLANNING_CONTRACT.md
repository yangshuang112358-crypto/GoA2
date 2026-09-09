# M1 Planning And Reveal Contract

This milestone implements planning and reveal only. It does not execute
movement, attacks, defense, or concrete card effects.

## Initial State

- Phase is `card_selection`.
- Four seats have fixed default heroes and five-card initial hands.
- Each seat has `selected_card_id`, `confirmed`, and `revealed_card_id`.
- Selection remains private until all four seats confirm.

## Commands

- `select_card(seat, card_id, expected_revision)`
  - only the controlled seat may select;
  - card must be in that seat's current hand;
  - confirmed seats cannot change selection.
- `confirm_selection(seat, expected_revision)`
  - only the controlled seat may confirm;
  - a card must already be selected;
  - confirming the fourth seat changes phase to `card_reveal`.
- `reveal_cards(expected_revision)`
  - legal only in `card_reveal`;
  - reveals all four cards in one atomic command;
  - sorts initiative descending;
  - initiative ties use seat order ascending as a temporary deterministic rule;
  - phase becomes `card_resolution`;
  - the first initiative entry becomes active.
- debug force-confirm selects the first hand card for every unconfirmed seat.

## Views

- Before reveal, public views expose only whether each seat selected/confirmed.
- Only the controlled seat receives its selected card ID and private hand.
- After reveal, all four revealed cards and initiative order are public.
- All cards remain `data_only` / `未实装未测试`.

## HTTP

- `POST /api/cards/select`
- `POST /api/cards/confirm`
- `POST /api/cards/reveal`
- `POST /api/debug/force-confirm`

Every mutation requires `expected_revision`. Invalid or stale commands are
atomic and return an error without changing room state.

