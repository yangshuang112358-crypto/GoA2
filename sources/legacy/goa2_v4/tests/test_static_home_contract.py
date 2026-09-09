from __future__ import annotations

import re
import unittest
from html.parser import HTMLParser
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]


class _MarkupInventory(HTMLParser):
    def __init__(self) -> None:
        super().__init__()
        self.ids: set[str] = set()
        self.classes: set[str] = set()
        self.button_ids: set[str] = set()

    def handle_starttag(
        self,
        tag: str,
        attrs: list[tuple[str, str | None]],
    ) -> None:
        attributes = dict(attrs)
        element_id = attributes.get("id")
        if element_id:
            self.ids.add(element_id)
            if tag == "button":
                self.button_ids.add(element_id)
        self.classes.update((attributes.get("class") or "").split())


class StaticHomeContractTests(unittest.TestCase):
    def test_home_contains_v2_three_column_key_regions(self) -> None:
        parser = _MarkupInventory()
        parser.feed((ROOT / "web" / "index.html").read_text(encoding="utf-8"))

        self.assertTrue(
            {"game-shell", "left-column", "center-column", "log-column"}
            <= parser.classes
        )
        html = (ROOT / "web" / "index.html").read_text(encoding="utf-8")
        for region in (
            "room-debug-panel",
            "player-seat-panel",
            "public-action-panel",
            "battlefield-map",
            "controlled-hand-panel",
            "event-log-panel",
        ):
            self.assertIn(f'data-ui-region="{region}"', html)
        self.assertTrue(
            {
                "roomCode",
                "players",
                "phaseTitle",
                "publicActions",
                "serverActions",
                "boardViewport",
                "boardZoom",
                "board",
                "hand",
                "log",
            }
            <= parser.ids
        )
        self.assertTrue(
            {
                "seatSelect",
                "resetButton",
                "teleportButton",
                "enterRoundEndButton",
                "removeMinionButton",
            }
            <= parser.ids,
            "The home page must expose controlled-seat and reset debug controls",
        )

    def test_styles_define_v3_design_tokens(self) -> None:
        css = (ROOT / "web" / "style.css").read_text(encoding="utf-8")
        expected = {
            "ink": "#f6f3eb",
            "muted": "#a8b0bf",
            "panel": "rgba(17, 22, 32, 0.95)",
            "line": "rgba(255, 255, 255, 0.11)",
            "gold": "#f4c968",
            "team-a": "#43a8ed",
            "team-b": "#ef6a72",
        }
        actual = {
            token: match.group(1).strip()
            for token in expected
            if (
                match := re.search(
                    rf"--{re.escape(token)}\s*:\s*([^;]+)\s*;",
                    css,
                )
            )
        }

        self.assertEqual(expected, actual)

    def test_card_stats_and_compact_log_layout_follow_ui_contract(self) -> None:
        html = (ROOT / "web" / "index.html").read_text(encoding="utf-8")
        css = (ROOT / "web" / "style.css").read_text(encoding="utf-8")
        app_js = (ROOT / "web" / "app.js").read_text(encoding="utf-8")

        self.assertNotIn('id="heroPanel"', html)
        for label in ("主要行动", "次要移动", "次要防御", "范围/远程"):
            self.assertIn(label, app_js)
        self.assertIn("grid-column: 2 / 4", css)
        self.assertIn("grid-row: 2", css)

    def test_desktop_public_action_zone_is_height_compressed(self) -> None:
        css = (ROOT / "web" / "style.css").read_text(encoding="utf-8")

        self.assertRegex(
            css,
            r"\.game-shell\s*\{[^}]*grid-template-rows:\s*180px\s+"
            r"minmax\(0,\s*1fr\)\s+250px;",
        )
        self.assertRegex(
            css,
            r"\.public-actions \.card\s*\{[^}]*height:\s*162px;",
        )
        self.assertRegex(
            css,
            r"\.public-actions \.card-text\s*\{[^}]*overflow-y:\s*auto;",
        )
        self.assertRegex(
            css,
            r"\.public-actions \.card h3\s*\{[^}]*overflow-y:\s*auto;"
            r"[^}]*white-space:\s*normal;",
        )
        self.assertRegex(
            css,
            r"\.server-actions\s*\{[^}]*grid-template-columns:\s*"
            r"repeat\(2,\s*minmax\(0,\s*1fr\)\);[^}]*overflow-y:\s*auto;",
        )

    def test_planning_controls_and_api_routes_are_wired_in_frontend(self) -> None:
        parser = _MarkupInventory()
        parser.feed((ROOT / "web" / "index.html").read_text(encoding="utf-8"))
        button_ids = {button_id.lower() for button_id in parser.button_ids}
        html = (ROOT / "web" / "index.html").read_text(encoding="utf-8")
        css = (ROOT / "web" / "style.css").read_text(encoding="utf-8")
        app_js = (ROOT / "web" / "app.js").read_text(encoding="utf-8")

        self.assertTrue(any("confirm" in button_id for button_id in button_ids))
        self.assertNotIn("revealbutton", button_ids)
        self.assertNotIn('id="revealButton"', html)
        self.assertNotIn('id="confirmButton" type="button" hidden', html)
        self.assertTrue(
            any(
                "force" in button_id and "confirm" in button_id
                for button_id in button_ids
            )
        )
        self.assertRegex(
            app_js,
            r"<button[^>]+data-[^>]*(?:card|select)|"
            r"document\.createElement\([\"']button[\"']\)",
        )
        for path in (
            "/api/cards/select",
            "/api/cards/confirm",
            "/api/actions/skip",
            "/api/actions/choose",
            "/api/actions/cancel",
            "/api/actions/attack",
            "/api/actions/defend",
            "/api/actions/complete",
            "/api/actions/initiative-choice",
            "/api/upgrade",
            "/api/round-end/captain-choice",
            "/api/debug/force-confirm",
            "/api/debug/begin-round-end",
            "/api/debug/teleport",
            "/api/debug/enter-round-end",
            "/api/debug/remove-minion",
        ):
            with self.subTest(path=path):
                self.assertIn(path, app_js)

        for projection in ("state.minions", "stateRoot().teams", "debug_teleport_targets"):
            with self.subTest(projection=projection):
                self.assertIn(projection, app_js)

        for marker in (
            "completeCardMarkup",
            "card_color_states",
            "frontlineTrack",
            "crystalStatus",
            "decisionCoin",
            "showHero: false",
            "showHero: true",
            "state.turn",
            '"wheel"',
            "Math.max(0.6",
            "Math.min(1.8",
        ):
            self.assertIn(marker, app_js)
        self.assertIn("grid-template-columns: repeat(2, minmax(0, 1fr))", css)
        self.assertIn("bottom: 7px", css)
        self.assertIn("right: 50%", css)
        self.assertIn("/api/actions/defend", app_js)
        self.assertIn("/api/actions/respawn", app_js)
        self.assertIn("data-defense-card-v2", app_js)
        self.assertIn("开始轮末结算", app_js)
        self.assertIn("选择此卡将获得", app_js)
        self.assertIn("support_bonus", app_js)
        self.assertIn("passive-total", app_js)
        self.assertIn("switchActiveSeatButton", app_js)

    def test_multiplayer_identity_polling_and_conflict_contract(self) -> None:
        html = (ROOT / "web" / "index.html").read_text(encoding="utf-8")
        css = (ROOT / "web" / "style.css").read_text(encoding="utf-8")
        app_js = (ROOT / "web" / "app.js").read_text(encoding="utf-8")

        for marker in (
            "URLSearchParams",
            "localStorage",
            "sessionStorage",
            "sessionStorage.removeItem",
            "/api/rooms/join",
            "token: reconnectToken || undefined",
            "room_id",
            "token",
            "stateUrl",
            "setInterval",
            "1000",
            "pollState",
            "model.pendingCommand",
            "error.status === 409",
            "joinSeat(Number(event.target.value))",
            "delete model.tokens[model.seat]",
        ):
            with self.subTest(marker=marker):
                self.assertIn(marker, app_js)

        self.assertRegex(
            app_js,
            r"function commandBody\([^)]*\)[\s\S]*room_id:\s*model\.roomId"
            r"[\s\S]*token:\s*model\.token",
        )
        self.assertRegex(
            app_js,
            r"async function pollState\(\)[\s\S]*if \(model\.pendingCommand"
            r"[\s\S]*await api\(stateUrl\(\)\)"
            r"[\s\S]*if \(model\.pendingCommand",
        )
        self.assertGreater(
            app_js.index("loadIdentity();"),
            app_js.index("const garbledPattern"),
        )
        self.assertIn('<link rel="icon" href="data:,">', html)
        self.assertIn('style.css?v=20260621-3', html)
        self.assertIn('app.js?v=20260621-3', html)
        self.assertIn('const DEFAULT_ROOM_ID = "local-v4-main"', app_js)
        self.assertIn('const IDENTITY_STORAGE_KEY = "goa2_v4.identity.v3"', app_js)
        self.assertIn('id="identitySeat"', html)
        self.assertIn(".connection-dot.connecting", css)

    def test_room_setup_and_extended_debug_controls_are_wired(self) -> None:
        html = (ROOT / "web" / "index.html").read_text(encoding="utf-8")
        css = (ROOT / "web" / "style.css").read_text(encoding="utf-8")
        app_js = (ROOT / "web" / "app.js").read_text(encoding="utf-8")

        for control_id in (
            "configureRoomButton",
            "debugSkipCurrentButton",
            "debugSkipAllButton",
            "debugCoinsButton",
            "debugCrystalButton",
            "debugFrontlineButton",
            "debugDefeatButton",
            "debugResetMinionsButton",
            "debugAdvanceBlueButton",
            "debugAdvanceRedButton",
        ):
            self.assertIn(f'id="{control_id}"', html)
        for route in (
            "/api/rooms/configure",
            "/api/debug/skip-current",
            "/api/debug/skip-all",
            "/api/debug/set-coins",
            "/api/debug/set-crystal",
            "/api/debug/set-frontline",
            "/api/debug/defeat-hero",
            "/api/debug/reset-minions",
            "/api/debug/advance-frontline",
        ):
            self.assertIn(route, app_js)
        self.assertIn('class="room-meta" hidden', html)
        self.assertIn('class="debug-tools"', html)
        self.assertIn('class="debug-grid"', html)
        self.assertIn("grid-template-columns: repeat(2, minmax(0, 1fr))", css)
        self.assertIn("font-size: 9px", css)
        self.assertIn("if (!model.setupDirty)", app_js)
        self.assertIn("model.setupDirty = true", app_js)
        self.assertIn("tokens: model.tokens", app_js)
        self.assertIn("cleanText(model.tokens[targetSeat]", app_js)
        self.assertIn("model.token = previousToken", app_js)


if __name__ == "__main__":
    unittest.main()
