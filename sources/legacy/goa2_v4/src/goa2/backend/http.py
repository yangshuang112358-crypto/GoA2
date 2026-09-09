"""Dependency-free HTTP and static-file adapter."""

from __future__ import annotations

from functools import partial
from http import HTTPStatus
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from io import BytesIO
import json
from pathlib import Path
from typing import Any, Callable, Iterable
from urllib.parse import parse_qs, urlsplit

from goa2.application.service import RevisionConflict, RoomApplication


DEFAULT_PORT = 3229
MAX_BODY_BYTES = 1_048_576
PLAYER_ACTION_PATHS = {
    "/api/cards/select",
    "/api/cards/confirm",
    "/api/actions/skip",
    "/api/actions/choose",
    "/api/actions/cancel",
    "/api/actions/move",
    "/api/actions/attack",
    "/api/actions/defend",
    "/api/actions/complete",
    "/api/round-end/captain-choice",
    "/api/actions/initiative-choice",
    "/api/actions/respawn",
    "/api/upgrade",
}
ROOM_TOOL_PATHS = {
    "/api/rooms/configure",
    "/api/debug/control",
    "/api/debug/control-seat",
    "/api/debug/force-confirm",
    "/api/debug/reset",
    "/api/debug/begin-round-end",
    "/api/debug/teleport",
    "/api/debug/enter-round-end",
    "/api/debug/remove-minion",
    "/api/debug/skip-current",
    "/api/debug/skip-all",
    "/api/debug/set-coins",
    "/api/debug/set-crystal",
    "/api/debug/set-frontline",
    "/api/debug/defeat-hero",
    "/api/debug/reset-minions",
    "/api/debug/advance-frontline",
}


def create_app(application: RoomApplication | None = None) -> "GoA2WsgiApp":
    if application is None:
        from .bootstrap import build_application

        application = build_application()
    return GoA2WsgiApp(application)


def create_server(
    application: RoomApplication,
    *,
    host: str = "127.0.0.1",
    port: int = DEFAULT_PORT,
    web_root: Path | None = None,
) -> ThreadingHTTPServer:
    root = web_root or Path(__file__).resolve().parents[3] / "web"
    handler = partial(GoA2RequestHandler, application, directory=str(root))
    return ThreadingHTTPServer((host, port), handler)


class GoA2WsgiApp:
    def __init__(self, application: RoomApplication) -> None:
        self.application = application

    def __call__(
        self,
        environ: dict[str, Any],
        start_response: Callable[[str, list[tuple[str, str]]], Any],
    ) -> Iterable[bytes]:
        method = environ.get("REQUEST_METHOD", "GET").upper()
        path = environ.get("PATH_INFO", "/")
        query = parse_qs(environ.get("QUERY_STRING", ""))
        try:
            if method == "GET" and path in {"/health", "/api/health"}:
                return self._respond(start_response, HTTPStatus.OK, {"status": "ok"})
            if method == "GET" and path == "/api/catalog":
                return self._respond(
                    start_response, HTTPStatus.OK, self.application.catalog()
                )
            if method == "GET" and path == "/api/state":
                room_id = query.get("room_id", ["default"])[0]
                token = query.get("token", [None])[0]
                return self._respond(
                    start_response, HTTPStatus.OK, self.application.state(room_id, token)
                )
            if method == "POST" and path == "/api/rooms/join":
                body = self._read_body(environ)
                return self._respond(
                    start_response,
                    HTTPStatus.OK,
                    self.application.join(
                        body.get("seat"),
                        body.get("room_id", "default"),
                        body.get("token"),
                    ),
                )
            if method == "POST" and path in {
                "/api/cards/select",
                "/api/cards/confirm",
                "/api/cards/reveal",
                "/api/actions/skip",
                "/api/actions/choose",
                "/api/actions/cancel",
                "/api/actions/move",
                "/api/actions/attack",
                "/api/actions/defend",
                "/api/actions/complete",
                "/api/round-end/captain-choice",
                "/api/actions/initiative-choice",
                "/api/actions/respawn",
                "/api/upgrade",
                "/api/debug/control",
                "/api/debug/control-seat",
                "/api/debug/force-confirm",
                "/api/debug/reset",
                "/api/debug/begin-round-end",
                "/api/debug/teleport",
                "/api/debug/enter-round-end",
                "/api/debug/remove-minion",
                "/api/debug/skip-current",
                "/api/debug/skip-all",
                "/api/debug/set-coins",
                "/api/debug/set-crystal",
                "/api/debug/set-frontline",
                "/api/debug/defeat-hero",
                "/api/debug/reset-minions",
                "/api/debug/advance-frontline",
                "/api/rooms/configure",
            }:
                body = self._read_body(environ)
                room_id = body.get("room_id", "default")
                expected = body.get("expected_revision")
                token = body.get("token")
                if path in PLAYER_ACTION_PATHS:
                    body["seat"] = self.application.actor_seat(
                        room_id, token, body.get("seat")
                    )
                elif path in ROOM_TOOL_PATHS:
                    self.application.authorize_room_tool(room_id, token)
                if path == "/api/cards/select":
                    payload = self.application.select_card(
                        body.get("seat"),
                        body.get("card_id"),
                        room_id,
                        expected,
                    )
                elif path == "/api/cards/confirm":
                    payload = self.application.confirm_selection(
                        body.get("seat"), room_id, expected
                    )
                elif path == "/api/cards/reveal":
                    payload = self.application.reveal_cards(room_id, expected)
                elif path == "/api/actions/skip":
                    payload = self.application.skip_action(
                        body.get("seat"), room_id, expected
                    )
                elif path == "/api/actions/choose":
                    payload = self.application.choose_action(
                        body.get("seat"),
                        body.get("kind"),
                        body.get("source_slot"),
                        room_id,
                        expected,
                    )
                elif path == "/api/actions/cancel":
                    payload = self.application.cancel_action(
                        body.get("seat"), room_id, expected
                    )
                elif path == "/api/actions/move":
                    payload = self.application.move(
                        body.get("seat"), body.get("x"), body.get("y"), room_id, expected
                    )
                elif path == "/api/actions/attack":
                    payload = self.application.declare_basic_attack(
                        body.get("seat"),
                        body.get("target_id"),
                        room_id,
                        expected,
                    )
                elif path == "/api/actions/defend":
                    payload = self.application.resolve_basic_defense(
                        body.get("seat"),
                        body.get("card_id"),
                        room_id,
                        expected,
                    )
                elif path == "/api/actions/complete":
                    payload = self.application.complete_action(
                        body.get("seat"), room_id, expected
                    )
                elif path == "/api/round-end/captain-choice":
                    payload = self.application.resolve_captain_choice(
                        body.get("seat"),
                        body.get("choice_id"),
                        body.get("candidate_id"),
                        room_id,
                        expected,
                    )
                elif path == "/api/actions/initiative-choice":
                    payload = self.application.resolve_initiative_choice(
                        body.get("seat"),
                        body.get("choice_id"),
                        body.get("chosen_seat"),
                        room_id,
                        expected,
                    )
                elif path == "/api/actions/respawn":
                    payload = self.application.resolve_respawn(
                        body.get("seat"),
                        body.get("x"),
                        body.get("y"),
                        room_id,
                        expected,
                    )
                elif path == "/api/upgrade":
                    payload = self.application.choose_upgrade(
                        body.get("seat"),
                        body.get("color"),
                        body.get("card_id"),
                        room_id,
                        expected,
                    )
                elif path == "/api/debug/force-confirm":
                    payload = self.application.force_confirm(room_id, expected)
                elif path in {"/api/debug/control", "/api/debug/control-seat"}:
                    payload = self.application.control(
                        body.get("seat", body.get("controlled_seat")),
                        room_id,
                        expected,
                    )
                elif path == "/api/debug/begin-round-end":
                    payload = self.application.begin_round_end(room_id, expected)
                elif path == "/api/debug/teleport":
                    payload = self.application.debug_teleport(
                        body.get("seat"), body.get("x"), body.get("y"), room_id, expected
                    )
                elif path == "/api/debug/enter-round-end":
                    payload = self.application.debug_enter_round_end(
                        room_id, expected
                    )
                elif path == "/api/debug/remove-minion":
                    payload = self.application.debug_remove_minion(
                        body.get("seat"),
                        body.get("minion_id"),
                        room_id,
                        expected,
                    )
                elif path == "/api/debug/skip-current":
                    payload = self.application.debug_skip_current(room_id, expected)
                elif path == "/api/debug/skip-all":
                    payload = self.application.debug_skip_all(room_id, expected)
                elif path == "/api/debug/set-coins":
                    payload = self.application.debug_set_seat_coins(
                        body.get("seat"), body.get("coins"), room_id, expected
                    )
                elif path == "/api/debug/set-crystal":
                    payload = self.application.debug_set_crystal_life(
                        body.get("team"), body.get("crystal_life"), room_id, expected
                    )
                elif path == "/api/debug/set-frontline":
                    payload = self.application.debug_set_frontline_marks(
                        body.get("team"), body.get("frontline_marks"), room_id, expected
                    )
                elif path == "/api/debug/defeat-hero":
                    payload = self.application.debug_defeat_hero(
                        body.get("seat"), room_id, expected
                    )
                elif path == "/api/debug/reset-minions":
                    payload = self.application.debug_reset_minions(room_id, expected)
                elif path == "/api/debug/advance-frontline":
                    payload = self.application.debug_advance_frontline(
                        body.get("team"), room_id, expected
                    )
                elif path == "/api/rooms/configure":
                    payload = self.application.configure_room(
                        body.get("starting_crystal_life"),
                        body.get("frontline_victory_marks"),
                        body.get("hero_ids"),
                        room_id,
                        expected,
                    )
                else:
                    payload = self.application.reset(room_id, expected)
                if token is not None:
                    payload = self.application.state(room_id, token)
                return self._respond(start_response, HTTPStatus.OK, payload)
        except RevisionConflict as exc:
            return self._respond(
                start_response,
                HTTPStatus.CONFLICT,
                {"error": {"code": "revision_conflict", "message": str(exc)}},
            )
        except (TypeError, ValueError, json.JSONDecodeError) as exc:
            return self._respond(
                start_response,
                HTTPStatus.BAD_REQUEST,
                {"error": {"code": "invalid_request", "message": str(exc)}},
            )
        return self._respond(
            start_response,
            HTTPStatus.NOT_FOUND,
            {"error": {"code": "not_found", "message": "unknown API route"}},
        )

    def _read_body(self, environ: dict[str, Any]) -> dict[str, Any]:
        length = int(environ.get("CONTENT_LENGTH") or 0)
        if length > MAX_BODY_BYTES:
            raise ValueError("JSON body exceeds 1 MiB")
        stream = environ.get("wsgi.input", BytesIO())
        value = json.loads(stream.read(length) or b"{}")
        if not isinstance(value, dict):
            raise ValueError("JSON body must be an object")
        return value

    def _respond(
        self,
        start_response: Callable[[str, list[tuple[str, str]]], Any],
        status: HTTPStatus,
        payload: Any,
    ) -> list[bytes]:
        body = json.dumps(
            payload, ensure_ascii=False, separators=(",", ":")
        ).encode("utf-8")
        start_response(
            f"{status.value} {status.phrase}",
            [
                ("Content-Type", "application/json; charset=utf-8"),
                ("Content-Length", str(len(body))),
                ("Cache-Control", "no-store"),
            ],
        )
        return [body]


class GoA2RequestHandler(SimpleHTTPRequestHandler):
    server_version = "GoA2/4"

    def __init__(
        self,
        application: RoomApplication,
        *args: Any,
        **kwargs: Any,
    ) -> None:
        self.application = application
        super().__init__(*args, **kwargs)

    def end_headers(self) -> None:
        if not urlsplit(self.path).path.startswith("/api/"):
            self.send_header("Cache-Control", "no-store, no-cache, must-revalidate")
            self.send_header("Pragma", "no-cache")
            self.send_header("Expires", "0")
        super().end_headers()

    def do_GET(self) -> None:
        request = urlsplit(self.path)
        if request.path in {"/health", "/api/health"}:
            self._send_json(HTTPStatus.OK, {"status": "ok"})
            return
        if request.path == "/api/catalog":
            self._send_json(HTTPStatus.OK, self.application.catalog())
            return
        if request.path == "/api/state":
            room_id = parse_qs(request.query).get("room_id", ["default"])[0]
            token = parse_qs(request.query).get("token", [None])[0]
            self._run_json(lambda: self.application.state(room_id, token))
            return
        if request.path.startswith("/api/"):
            self._send_error_json(HTTPStatus.NOT_FOUND, "not_found", "unknown API route")
            return
        self.path = "/index.html" if request.path == "/" else request.path
        super().do_GET()

    def do_POST(self) -> None:
        path = urlsplit(self.path).path
        if path not in {
            "/api/rooms/join",
            "/api/cards/select",
            "/api/cards/confirm",
            "/api/cards/reveal",
            "/api/actions/skip",
            "/api/actions/choose",
            "/api/actions/cancel",
            "/api/actions/move",
            "/api/actions/attack",
            "/api/actions/defend",
            "/api/actions/complete",
            "/api/round-end/captain-choice",
            "/api/actions/initiative-choice",
            "/api/actions/respawn",
            "/api/upgrade",
            "/api/debug/control",
            "/api/debug/control-seat",
            "/api/debug/force-confirm",
            "/api/debug/reset",
            "/api/debug/begin-round-end",
            "/api/debug/teleport",
            "/api/debug/enter-round-end",
            "/api/debug/remove-minion",
            "/api/debug/skip-current",
            "/api/debug/skip-all",
            "/api/debug/set-coins",
            "/api/debug/set-crystal",
            "/api/debug/set-frontline",
            "/api/debug/defeat-hero",
            "/api/debug/reset-minions",
            "/api/debug/advance-frontline",
            "/api/rooms/configure",
        }:
            self._send_error_json(HTTPStatus.NOT_FOUND, "not_found", "unknown API route")
            return
        body = self._read_json()
        if body is None:
            return
        room_id = body.get("room_id", "default")
        if not isinstance(room_id, str):
            self._send_error_json(
                HTTPStatus.BAD_REQUEST, "invalid_request", "room_id must be a string"
            )
            return
        expected = body.get("expected_revision")
        token = body.get("token")
        if path == "/api/rooms/join":
            self._run_json(
                lambda: self.application.join(
                    body.get("seat"), room_id, body.get("token")
                )
            )
            return
        if path in PLAYER_ACTION_PATHS:
            try:
                body["seat"] = self.application.actor_seat(
                    room_id, token, body.get("seat")
                )
            except (TypeError, ValueError) as exc:
                self._send_error_json(
                    HTTPStatus.BAD_REQUEST, "invalid_request", str(exc)
                )
                return
        elif path in ROOM_TOOL_PATHS:
            try:
                self.application.authorize_room_tool(room_id, token)
            except (TypeError, ValueError) as exc:
                self._send_error_json(
                    HTTPStatus.BAD_REQUEST, "invalid_request", str(exc)
                )
                return
        self._response_room_id = room_id
        self._response_token = token
        if path == "/api/cards/select":
            self._run_json(
                lambda: self.application.select_card(
                    body.get("seat"),
                    body.get("card_id"),
                    room_id,
                    expected,
                )
            )
            return
        if path == "/api/cards/confirm":
            self._run_json(
                lambda: self.application.confirm_selection(
                    body.get("seat"), room_id, expected
                )
            )
            return
        if path == "/api/cards/reveal":
            self._run_json(
                lambda: self.application.reveal_cards(room_id, expected)
            )
            return
        if path == "/api/actions/skip":
            self._run_json(
                lambda: self.application.skip_action(
                    body.get("seat"), room_id, expected
                )
            )
            return
        if path == "/api/actions/choose":
            self._run_json(
                lambda: self.application.choose_action(
                    body.get("seat"),
                    body.get("kind"),
                    body.get("source_slot"),
                    room_id,
                    expected,
                )
            )
            return
        if path == "/api/actions/cancel":
            self._run_json(
                lambda: self.application.cancel_action(
                    body.get("seat"), room_id, expected
                )
            )
            return
        if path == "/api/actions/move":
            self._run_json(
                lambda: self.application.move(
                    body.get("seat"), body.get("x"), body.get("y"), room_id, expected
                )
            )
            return
        if path == "/api/actions/attack":
            self._run_json(
                lambda: self.application.declare_basic_attack(
                    body.get("seat"),
                    body.get("target_id"),
                    room_id,
                    expected,
                )
            )
            return
        if path == "/api/actions/defend":
            self._run_json(
                lambda: self.application.resolve_basic_defense(
                    body.get("seat"),
                    body.get("card_id"),
                    room_id,
                    expected,
                )
            )
            return
        if path == "/api/actions/complete":
            self._run_json(
                lambda: self.application.complete_action(
                    body.get("seat"), room_id, expected
                )
            )
            return
        if path == "/api/round-end/captain-choice":
            self._run_json(
                lambda: self.application.resolve_captain_choice(
                    body.get("seat"),
                    body.get("choice_id"),
                    body.get("candidate_id"),
                    room_id,
                    expected,
                )
            )
            return
        if path == "/api/actions/initiative-choice":
            self._run_json(
                lambda: self.application.resolve_initiative_choice(
                    body.get("seat"),
                    body.get("choice_id"),
                    body.get("chosen_seat"),
                    room_id,
                    expected,
                )
            )
            return
        if path == "/api/actions/respawn":
            self._run_json(
                lambda: self.application.resolve_respawn(
                    body.get("seat"),
                    body.get("x"),
                    body.get("y"),
                    room_id,
                    expected,
                )
            )
            return
        if path == "/api/upgrade":
            self._run_json(
                lambda: self.application.choose_upgrade(
                    body.get("seat"),
                    body.get("color"),
                    body.get("card_id"),
                    room_id,
                    expected,
                )
            )
            return
        if path == "/api/debug/force-confirm":
            self._run_json(
                lambda: self.application.force_confirm(room_id, expected)
            )
            return
        if path in {"/api/debug/control", "/api/debug/control-seat"}:
            seat = body.get("seat", body.get("controlled_seat"))
            self._run_json(
                lambda: self.application.control(
                    seat, room_id, expected
                )
            )
            return
        if path == "/api/debug/begin-round-end":
            self._run_json(
                lambda: self.application.begin_round_end(room_id, expected)
            )
            return
        if path == "/api/debug/teleport":
            self._run_json(
                lambda: self.application.debug_teleport(
                    body.get("seat"), body.get("x"), body.get("y"), room_id, expected
                )
            )
            return
        if path == "/api/debug/enter-round-end":
            self._run_json(
                lambda: self.application.debug_enter_round_end(room_id, expected)
            )
            return
        if path == "/api/debug/remove-minion":
            self._run_json(
                lambda: self.application.debug_remove_minion(
                    body.get("seat"),
                    body.get("minion_id"),
                    room_id,
                    expected,
                )
            )
            return
        if path == "/api/debug/skip-current":
            self._run_json(
                lambda: self.application.debug_skip_current(room_id, expected)
            )
            return
        if path == "/api/debug/skip-all":
            self._run_json(
                lambda: self.application.debug_skip_all(room_id, expected)
            )
            return
        if path == "/api/debug/set-coins":
            self._run_json(
                lambda: self.application.debug_set_seat_coins(
                    body.get("seat"), body.get("coins"), room_id, expected
                )
            )
            return
        if path == "/api/debug/set-crystal":
            self._run_json(
                lambda: self.application.debug_set_crystal_life(
                    body.get("team"), body.get("crystal_life"), room_id, expected
                )
            )
            return
        if path == "/api/debug/set-frontline":
            self._run_json(
                lambda: self.application.debug_set_frontline_marks(
                    body.get("team"), body.get("frontline_marks"), room_id, expected
                )
            )
            return
        if path == "/api/debug/defeat-hero":
            self._run_json(
                lambda: self.application.debug_defeat_hero(
                    body.get("seat"), room_id, expected
                )
            )
            return
        if path == "/api/debug/reset-minions":
            self._run_json(
                lambda: self.application.debug_reset_minions(room_id, expected)
            )
            return
        if path == "/api/debug/advance-frontline":
            self._run_json(
                lambda: self.application.debug_advance_frontline(
                    body.get("team"), room_id, expected
                )
            )
            return
        if path == "/api/rooms/configure":
            self._run_json(
                lambda: self.application.configure_room(
                    body.get("starting_crystal_life"),
                    body.get("frontline_victory_marks"),
                    body.get("hero_ids"),
                    room_id,
                    expected,
                )
            )
            return
        self._run_json(
            lambda: self.application.reset(room_id, expected)
        )

    def _read_json(self) -> dict[str, Any] | None:
        raw_length = self.headers.get("Content-Length", "0")
        try:
            length = int(raw_length)
        except ValueError:
            self._send_error_json(
                HTTPStatus.BAD_REQUEST, "invalid_request", "invalid Content-Length"
            )
            return None
        if length < 0 or length > MAX_BODY_BYTES:
            self._send_error_json(
                HTTPStatus.REQUEST_ENTITY_TOO_LARGE,
                "request_too_large",
                "JSON body exceeds 1 MiB",
            )
            return None
        try:
            value = json.loads(self.rfile.read(length) or b"{}")
        except (UnicodeDecodeError, json.JSONDecodeError):
            self._send_error_json(
                HTTPStatus.BAD_REQUEST, "invalid_json", "body must be valid JSON"
            )
            return None
        if not isinstance(value, dict):
            self._send_error_json(
                HTTPStatus.BAD_REQUEST, "invalid_request", "JSON body must be an object"
            )
            return None
        return value

    def _run_json(self, operation: Any) -> None:
        try:
            payload = operation()
            token = getattr(self, "_response_token", None)
            room_id = getattr(self, "_response_room_id", None)
            if token is not None and room_id is not None:
                payload = self.application.state(room_id, token)
        except RevisionConflict as exc:
            self._send_error_json(HTTPStatus.CONFLICT, "revision_conflict", str(exc))
            return
        except (TypeError, ValueError) as exc:
            self._send_error_json(
                HTTPStatus.BAD_REQUEST, "invalid_request", str(exc)
            )
            return
        self._send_json(HTTPStatus.OK, payload)
        self._response_room_id = None
        self._response_token = None

    def _send_error_json(
        self, status: HTTPStatus, code: str, message: str
    ) -> None:
        self._send_json(status, {"error": {"code": code, "message": message}})

    def _send_json(self, status: HTTPStatus, payload: Any) -> None:
        body = json.dumps(
            payload, ensure_ascii=False, separators=(",", ":")
        ).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(body)
