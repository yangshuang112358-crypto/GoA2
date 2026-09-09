# GoA2 v4

GoA2 v4 is an independent rebuild. It reuses verified data and product
knowledge from v2/v3, but it does not import either version at runtime.

## Product Direction

- Layout: v2 three-column desktop game layout.
- Colors: v3 dark palette, gold accent, and blue/red team colors.
- Authority: server-side game state and legality.
- Cards: all 108 cards start as `data_only` / `未实装未测试`.
- Card effects: no concrete card effect is enabled in milestone 1.
- Debug controls remain a required part of the game UI.

## Current Interaction

- switch the controlled hotseat seat;
- repeatedly select one of the controlled seat's five cards until confirming;
- lock each confirmed choice and automatically reveal after all four seats
  confirm;
- display the server-calculated initiative order;
- choose server-provided primary, secondary-movement, or fast-move slots;
- switch or cancel a pending action choice before it resolves;
- move heroes through server-calculated ordinary movement paths;
- fast-move within the current or an adjacent enemy-free region;
- choose an adjacent hero or minion for a basic attack and preserve the
  server-owned pending attack;
- complete a non-movement `data_only` primary as an explicit placeholder;
- skip the active card and automatically resolve round end after turn four;
- let the team captain order teammates whose remaining cards have equal initiative;
- resolve minion-count differences through captain choices;
- advance the three combat regions, respawn minions, and detect three-push or
  fountain-boundary victory;
- display server-owned minion, crystal, captain, and frontline-mark state;
- award minion/hero defeat coins and apply level-based crystal loss;
- automatically pay for hero levels at round end and choose card upgrades;
- expose serializable starting crystal life and frontline victory settings for
  future room creation options;
- reset, force-confirm, teleport, enter round end, or remove a minion through
  debug commands.

Basic attack/defense, defeat rewards, and the first progression loop are
enabled. Hero respawn, application of stored passive bonuses, multiplayer
identity/sync, and concrete card effects are not enabled yet.

## Round-End Demo

1. Click `调试：进入轮末`.
2. Enable `调试移除小兵` and click one or more minions to create an imbalance.
3. Click `调试：开始轮末结算`.
4. Switch to the captain seat named in the status line and click highlighted
   minions or spawn cells until resolution completes.

## Dependency Direction

`web -> backend -> application -> engine -> domain`

`cards -> domain`, with card catalogs injected into application services.
The engine must not depend on concrete card IDs.

## Run

```powershell
python server.py
```

Open `http://localhost:3229/`.

On Windows, you can also double-click `启动GoA2.bat`. Keep its terminal window
open while using the game; closing it stops the local server and the page will
show a connection failure.

Room state is stored in `goa2_rooms_v4.json`. The earlier `goa2_rooms.json`
file is left untouched as a backup. Browser seat tokens use persistent local
storage so reopening the page does not lock the player out of an occupied seat.

Run tests:

```powershell
$env:PYTHONPATH="src"
python -m unittest discover -v
```
