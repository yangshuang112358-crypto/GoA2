"""Run the local GoA2 HTTP service."""

from __future__ import annotations

import argparse
from pathlib import Path

from .bootstrap import build_application
from .http import DEFAULT_PORT, create_server


def parse_args(argv: list[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Run the GoA2 v4 local server")
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", default=DEFAULT_PORT, type=int)
    parser.add_argument("--web-root", type=Path)
    parser.add_argument(
        "--rooms-file",
        type=Path,
        default=Path("goa2_rooms_v4.json"),
        help="JSON room store path (default: goa2_rooms_v4.json)",
    )
    return parser.parse_args(argv)


def main() -> None:
    args = parse_args()

    server = create_server(
        build_application(rooms_file=args.rooms_file),
        host=args.host,
        port=args.port,
        web_root=args.web_root,
    )
    print(f"GoA2 v4 listening on http://{args.host}:{args.port}", flush=True)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        server.server_close()


if __name__ == "__main__":
    main()
