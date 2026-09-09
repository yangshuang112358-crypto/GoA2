from __future__ import annotations

import json
from threading import Thread
import unittest
from urllib.request import urlopen

from tests.contract_support import catalog_parts, field


class HttpContractTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        from goa2.backend.bootstrap import build_application
        from goa2.backend.http import GoA2RequestHandler, create_server

        cls.original_log_message = GoA2RequestHandler.log_message
        GoA2RequestHandler.log_message = lambda *args, **kwargs: None
        cls.handler_class = GoA2RequestHandler
        cls.server = create_server(build_application(), port=0)
        cls.thread = Thread(target=cls.server.serve_forever, daemon=True)
        cls.thread.start()
        host, port = cls.server.server_address
        cls.base_url = f"http://{host}:{port}"

    @classmethod
    def tearDownClass(cls) -> None:
        cls.server.shutdown()
        cls.server.server_close()
        cls.thread.join(timeout=5)
        cls.handler_class.log_message = cls.original_log_message

    def get_json(self, path: str) -> tuple[int, dict]:
        with urlopen(self.base_url + path, timeout=5) as response:
            return response.status, json.loads(response.read().decode("utf-8"))

    def test_health(self) -> None:
        status, payload = self.get_json("/api/health")

        self.assertEqual(200, status)
        self.assertEqual("ok", field(payload, "status"))

    def test_static_assets_disable_browser_caching(self) -> None:
        with urlopen(self.base_url + "/", timeout=5) as response:
            self.assertEqual(
                "no-store, no-cache, must-revalidate",
                response.headers["Cache-Control"],
            )

    def test_catalog(self) -> None:
        status, payload = self.get_json("/api/catalog")
        heroes, cards, cells = catalog_parts(payload)

        self.assertEqual(200, status)
        self.assertEqual((6, 108, 254), (len(heroes), len(cards), len(cells)))
        self.assertTrue(
            all(
                field(card, "implementation_status", "implementation") == "data_only"
                for card in cards
            )
        )

    def test_state(self) -> None:
        status, payload = self.get_json("/api/state")
        public = field(payload, "public")

        self.assertEqual(200, status)
        self.assertEqual(4, len(field(public, "seats")))
        self.assertEqual(0, field(payload, "controlled_seat", "controlledSeat"))
        self.assertEqual(0, field(payload, "revision"))
        self.assertEqual(5, len(field(payload, "private_view")["hand"]))
        self.assertEqual("wasp", field(payload, "private_view")["hero_id"])


if __name__ == "__main__":
    unittest.main()
