from __future__ import annotations

import json
from pathlib import Path
from tempfile import TemporaryDirectory
import unittest
from unittest.mock import patch


class JsonPersistenceTests(unittest.TestCase):
    def test_build_application_remains_in_memory_by_default(self) -> None:
        from goa2.backend.bootstrap import build_application
        from goa2.backend.memory_store import InMemoryRoomStore

        application = build_application()

        self.assertIsInstance(application._rooms, InMemoryRoomStore)

    def test_explicit_json_file_restores_room_after_restart(self) -> None:
        from goa2.backend.bootstrap import build_application
        from goa2.backend.json_store import JsonRoomStore

        with TemporaryDirectory() as directory:
            path = Path(directory) / "rooms.json"
            first = build_application(rooms_file=path)
            initial = first.state("persistent")
            changed = first.control(
                2,
                room_id="persistent",
                expected_revision=initial["revision"],
            )

            second = build_application(rooms_file=path)
            restored = second.state("persistent")

            self.assertIsInstance(second._rooms, JsonRoomStore)
            self.assertEqual(changed["revision"], restored["revision"])
            self.assertEqual(2, restored["state"]["controlled_seat"])

    def test_json_snapshot_keeps_bounded_logs_and_hides_plaintext_tokens(self) -> None:
        from goa2.application.room import EVENT_LOG_LIMIT, SNAPSHOT_HISTORY_LIMIT
        from goa2.backend.bootstrap import build_application

        with TemporaryDirectory() as directory:
            path = Path(directory) / "rooms.json"
            application = build_application(rooms_file=path)
            joined = application.join(0, room_id="logged")
            token = joined["token"]

            for _ in range(EVENT_LOG_LIMIT + 5):
                application._rooms.transact("logged", lambda current: current)

            payload = json.loads(path.read_text(encoding="utf-8"))
            saved = payload["rooms"]["logged"]

            self.assertEqual(EVENT_LOG_LIMIT, len(saved["events"]))
            self.assertEqual(SNAPSHOT_HISTORY_LIMIT, len(saved["history"]))
            self.assertNotIn(token, path.read_text(encoding="utf-8"))
            self.assertEqual(4, len(saved["seat_token_hashes"]))
            self.assertIsNotNone(saved["seat_token_hashes"][0])
            self.assertNotIn("seat_token_hashes", saved["events"][-1])
            self.assertNotIn("seat_token_hashes", saved["history"][-1])

    def test_legacy_snapshot_without_tokens_loads_but_rejects_supplied_claim(self) -> None:
        from goa2.backend.bootstrap import build_application

        with TemporaryDirectory() as directory:
            path = Path(directory) / "rooms.json"
            first = build_application(rooms_file=path)
            first.state("legacy")
            payload = json.loads(path.read_text(encoding="utf-8"))
            del payload["rooms"]["legacy"]["seat_token_hashes"]
            path.write_text(json.dumps(payload), encoding="utf-8")

            restarted = build_application(rooms_file=path)
            with self.assertRaises(ValueError):
                restarted.join(
                    0,
                    "legacy",
                    "caller-supplied-token-that-must-not-be-trusted",
                )

            joined = restarted.join(0, "legacy")
            self.assertTrue(joined["token"])

    def test_state_projects_public_transaction_log(self) -> None:
        from goa2.backend.bootstrap import build_application

        application = build_application()
        initial = application.state("events")
        application.control(1, "events", initial["revision"])

        projected = application.state("events")

        self.assertEqual(projected["events"], projected["public"]["log"])
        self.assertEqual("room_updated", projected["events"][-1]["kind"])
        self.assertEqual(projected["revision"], projected["events"][-1]["revision"])

    def test_corrupt_file_is_quarantined_and_ignored(self) -> None:
        from goa2.backend.json_store import JsonRoomStore

        with TemporaryDirectory() as directory:
            path = Path(directory) / "rooms.json"
            path.write_text("{not-json", encoding="utf-8")

            store = JsonRoomStore(path)

            self.assertIsNone(store.read("default"))
            self.assertFalse(path.exists())
            self.assertEqual(
                1,
                len(list(Path(directory).glob("rooms.json.corrupt-*"))),
            )

    def test_failed_atomic_replace_preserves_file_and_memory(self) -> None:
        from goa2.backend.bootstrap import build_application

        with TemporaryDirectory() as directory:
            path = Path(directory) / "rooms.json"
            application = build_application(rooms_file=path)
            initial = application.state("atomic")
            before_file = path.read_bytes()
            before_room = application._rooms.read("atomic")

            with patch(
                "goa2.backend.json_store.os.replace",
                side_effect=OSError("replace failed"),
            ):
                with self.assertRaises(OSError):
                    application.control(
                        1,
                        room_id="atomic",
                        expected_revision=initial["revision"],
                    )

            self.assertEqual(before_file, path.read_bytes())
            self.assertEqual(before_room, application._rooms.read("atomic"))
            self.assertEqual([], list(Path(directory).glob("*.tmp")))

    def test_server_cli_defaults_to_goa2_rooms_v4_json(self) -> None:
        from goa2.backend.__main__ import parse_args

        args = parse_args([])

        self.assertEqual(Path("goa2_rooms_v4.json"), args.rooms_file)


if __name__ == "__main__":
    unittest.main()
