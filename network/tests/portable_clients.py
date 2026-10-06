"""Four real TCP connections to the packaged host, not a rules simulation or UI test."""
import json
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "client"))
from player import Player
from packaged_draft import complete_packaged_draft


def run(room):
    checks, players = [], []

    def check(name, condition):
        assert condition, name
        checks.append(name)

    try:
        for seat in range(4):
            player = Player(json.loads((room / "private" / f"seat-{seat}.private.json").read_text()))
            players.append(player)
            player.connect()
            check(f"authenticated seat {seat}", player.seat == seat)
        revision = complete_packaged_draft(players, check)
        for seat, player in enumerate(players):
            player.wait(lambda: player.view["Revision"] == revision)
            check(f"public heroes agree {seat}", [p["HeroId"] for p in player.view["Players"]] == ["wasp", "sabina", "tigerclaw", "arien"])
        players[2].disconnect()
        players[2].connect()
        check("reconnect retains identity and revision", players[2].seat == 2 and players[2].view["Revision"] == revision)
        return {"passed": True, "runner": "portable exe + four TCP connections; same PC; no UI", "checks": checks}
    finally:
        for player in players:
            player.disconnect()


if __name__ == "__main__":
    result = run(Path(sys.argv[1]))
    Path(sys.argv[2]).write_text(json.dumps(result, indent=2), encoding="utf-8")
    print(json.dumps({"passed": True, "checks": len(result["checks"])}))
