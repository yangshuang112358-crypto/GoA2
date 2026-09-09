# GoA2 v4 To v2 Playable Parity

This order tracks restoration of the v2 playable loop. Concrete card effects
remain outside this parity target and stay `data_only`.

| Order | Milestone | Status | Visible result |
| --- | --- | --- | --- |
| 1 | Four-seat card selection, reveal, initiative display | Complete | Four seats can select, confirm, and reveal together |
| 2 | Resolution queue and skip action | Complete | Active cards can be skipped in order until round end |
| 3 | Server-derived primary, secondary, fast-move action choices | Complete | Current player sees only server-provided choices for the revealed card |
| 4 | Hero units, spawn positions, occupancy, obstacles | Complete | Four heroes appear on authoritative map positions |
| 5 | Ordinary movement with server BFS and movement budget | Complete | Map highlights legal paths and consumes movement |
| 6 | Fast movement as a same/adjacent-region transfer | Complete | Both source and target regions must contain no enemy units |
| 7 | Card completion, next turn, four-turn round lifecycle | Complete | Four four-card turns automatically advance into round end |
| 8 | Minions, lane resolution, victory and round-end expiry | Complete | Three combat regions, three-push center victory, captain removal and spawn choices |
| 9 | Attack target selection | Complete | All attack-family cards can currently select one adjacent enemy target |
| 10 | Defense choice, defeat, reward, respawn | Complete | Defeated heroes leave the board and choose a legal team spawn before resolving their next card |
| 11 | Coins, levels, upgrades, and purple passives | Complete | Permanent bonuses modify card values; level 8 unlocks the purple passive without deadlocking |
| 12 | Multi-window identity, polling/sync, reconnect | Complete | Four browser windows own separate private seats; tokens and rooms survive server restart |
| 13 | Room setup, persistence, event log | Complete | Hero selection, crystal/frontline parameters, atomic snapshots, recovery, and bounded public transaction history are available |
| 14 | Debug parity and v2 behavior audit | Complete | Skip current/all, resources, defeat, minion reset/removal, frontline advance, teleport, and round-end tools are wired |

## Remaining Scope

- All 108 concrete card effects remain `data_only` and `未实装未测试`.
- Card-driven movement of minions outside the combat region will be implemented
  together with the first card family that can create that state, including the
  captain route-choice window and shortest-path tests.
- The current event panel shows bounded room transactions. Detailed
  rule-event projection remains part of the card/effect-engine rollout.

## Boundaries

- The server owns legal actions, paths, state changes, and revision checks.
- The frontend submits intent and renders server-provided legality.
- Fast movement is a same/adjacent-region transfer, not ordinary BFS movement.
- Source and target regions must both contain no enemy unit.
- A primary movement card has no secondary movement choice.
- Replacing primary movement with fast movement does not execute primary text.
- Same-initiative conflicts are resolved dynamically by decision coin and team
  captain choice.
- Debug `skip current card` is a room-level tool: any authenticated seat may
  invoke it, and the server skips the current active card regardless of which
  seat clicked the button.
