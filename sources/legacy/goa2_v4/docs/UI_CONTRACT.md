# GoA2 v4 UI Contract

## Card Statistics

Every card displays four values in this exact order:

1. `主要行动`
2. `次要移动`
3. `次要防御`
4. `范围` or `远程`

The first value is the primary action value. The next two values come from
secondary movement and defense. The fourth value comes from the primary
action subtype; cards without a range/ranged subtype display `—`.

Card width must allow all four labels to remain readable.

## Desktop Layout

- The left room/player column spans the full height.
- The public action zone spans the center and right columns.
- The battlefield occupies the center middle area.
- The log occupies only the right middle area.
- The hand zone spans the center and right columns.
- The hand zone has no separate hero-name block.

