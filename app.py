from __future__ import annotations

import json
import random
import secrets
import sys
import time
from copy import deepcopy
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import urlparse


ROOT = Path(__file__).resolve().parent
STATIC = ROOT / "static"
DATA = ROOT / "data"
PORT = int(sys.argv[1]) if len(sys.argv) > 1 else 3207

DIRS = [(1, 0), (1, -1), (0, -1), (-1, 0), (-1, 1), (0, 1)]


def load_map() -> list[dict]:
    cells = json.loads((DATA / "map.json").read_text(encoding="utf-8"))
    by_key = {(c["x"], c["y"]): c for c in cells}
    seen: set[tuple[int, int]] = set()
    comps: list[list[dict]] = []
    for cell in cells:
        start = (cell["x"], cell["y"])
        if start in seen:
            continue
        queue = [cell]
        comp = []
        seen.add(start)
        for cur in queue:
            comp.append(cur)
            for dx, dy in DIRS:
                key = (cur["x"] + dx, cur["y"] + dy)
                if key in by_key and key not in seen:
                    seen.add(key)
                    queue.append(by_key[key])
        comps.append(comp)

    def center_distance(c: dict) -> float:
        dx = c["x"] - 0.5
        dy = c["y"]
        dz = -c["x"] - c["y"] + 0.5
        return (abs(dx) + abs(dy) + abs(dz)) / 2

    main = sorted(
        comps,
        key=lambda comp: (
            -sum(1 for c in comp if c.get("region") == "mid"),
            min(center_distance(c) for c in comp),
            -len(comp),
        ),
    )[0]
    cleaned = []
    for c in main:
        item = dict(c)
        if item.get("obstacle") or item.get("state") == "terrain":
            item["region"] = "terrain"
            item["obstacle"] = True
            item["state"] = "terrain"
        cleaned.append(item)
    return sorted(cleaned, key=lambda c: (c["y"], c["x"]))


MAP = load_map()
MAP_BY_KEY = {(c["x"], c["y"]): c for c in MAP}


HEROES = {
    "tigerclaw": {"name": "切盗者", "title": "切盗者"},
    "brogan": {"name": "毁灭者", "title": "毁灭者"},
    "wasp": {"name": "战争少女", "title": "战争少女"},
    "arien": {"name": "潮汐大师", "title": "潮汐大师"},
}


def card(id: str, color: str, initiative: int, defense: int | None, movement: int, attack: int | None, name: str, text: str, primary: str) -> dict:
    return {
        "id": id,
        "color": color,
        "initiative": initiative,
        "defense": defense,
        "movement": movement,
        "attack": attack,
        "name": name,
        "text": text,
        "primary": primary,
    }


def test_cards(prefix: str) -> list[dict]:
    return [
        card(f"{prefix}-gold", "金", 10, 2, 2, None, "清兵金", "主要：击杀一个相邻小兵；次要：移动 2。", "killMinion"),
        card(f"{prefix}-red", "红", 8, 3, 3, None, "清兵红", "主要：击杀一个相邻小兵；次要：移动 3。", "killMinion"),
        card(f"{prefix}-green", "绿", 6, 4, 2, None, "清兵绿", "主要：击杀一个相邻小兵；次要：移动 2。", "killMinion"),
        card(f"{prefix}-blue", "蓝", 4, 5, 1, 2, "近身攻击", "主要：攻击一个相邻敌方英雄；次要：移动 1。", "attackHero"),
        card(f"{prefix}-silver", "银", 4, 4, 1, 5, "远程打击", "主要：攻击任意敌方单位，攻击力 5；次要：移动 1。", "attackAny"),
    ]


CARDS = {key: test_cards(key) for key in HEROES}


ROOMS: dict[str, dict] = {}


def new_room(code: str) -> dict:
    hero_keys = ["tigerclaw", "brogan", "wasp", "arien"]
    return {
        "code": code,
        "phase": "lobby",
        "round": 1,
        "turn": 1,
        "winner": None,
        "captains": {"blue": None, "red": None},
        "lives": {"blue": 7, "red": 7},
        "tiebreaker": "blue" if secrets.randbelow(2) == 0 else "red",
        "front": 0,
        "frontMarks": {"blue": 0, "red": 0},
        "activeSeat": None,
        "resolutionOrder": [],
        "pendingDefense": None,
        "pendingCaptainChoice": None,
        "pendingMinionRemoval": None,
        "pendingUpgrades": [],
        "log": [],
        "players": [
            {
                "seat": i,
                "token": None,
                "name": "",
                "team": "blue" if i % 2 == 0 else "red",
                "heroKey": hero_keys[i],
                "pos": None,
                "needsSpawn": True,
                "defeated": False,
                "hand": [c["id"] for c in CARDS[hero_keys[i]]],
                "discard": [],
                "roundUsed": [],
                "played": None,
                "selectedCardId": None,
                "resolved": False,
                "actionTaken": False,
                "coins": 0,
                "heroLevel": 1,
                "skills": {"red": 1, "blue": 1, "green": 1},
                "passives": [],
                "bonuses": {"damage": 0, "defense": 0},
                "cardUpgrades": {"red": {"initiative": 0, "movement": 0}, "green": {"initiative": 0, "movement": 0}, "blue": {"initiative": 0, "movement": 0}},
                "hasUltimate": False,
            }
            for i in range(4)
        ],
        "minions": spawn_minions(0, []),
    }


def public_state(room: dict, token: str | None) -> dict:
    state = deepcopy(room)
    state["map"] = MAP
    state["heroes"] = HEROES
    state["cards"] = CARDS
    state["meSeat"] = next((i for i, p in enumerate(room["players"]) if p["token"] == token), -1)
    for p in state["players"]:
        p["occupied"] = bool(room["players"][p["seat"]]["token"])
        p["effectiveCards"] = effective_cards(room["players"][p["seat"]])
        if p.get("token") != token:
            p.pop("token", None)
            if state["phase"] == "planning":
                p["selectedCardId"] = "selected" if p["selectedCardId"] else None
    return state


def full_hand(hero_key: str) -> list[str]:
    return [c["id"] for c in CARDS[hero_key]]


def mark_round_used(player: dict, card_id: str | None) -> None:
    if card_id and card_id not in player["roundUsed"]:
        player["roundUsed"].append(card_id)


def touch(room: dict, text: str) -> None:
    room["log"].insert(0, f"[R{room['round']}] {text}")
    room["log"] = room["log"][:100]


def cell_at(x: int, y: int) -> dict | None:
    return MAP_BY_KEY.get((x, y))


def in_bounds(x: int, y: int) -> bool:
    c = cell_at(x, y)
    return bool(c and not c.get("obstacle"))


def dist(a: dict, b: dict) -> int:
    dx = a["x"] - b["x"]
    dy = a["y"] - b["y"]
    dz = -a["x"] - a["y"] - (-b["x"] - b["y"])
    return (abs(dx) + abs(dy) + abs(dz)) // 2


def occupied(room: dict, x: int, y: int, ignore_seat: int | None = None) -> bool:
    for p in room["players"]:
        if p["seat"] != ignore_seat and p.get("pos") and not p["defeated"] and p["pos"] == {"x": x, "y": y}:
            return True
    return any(m["x"] == x and m["y"] == y for m in room["minions"])


def walk_distance(room: dict, start: dict, target: dict, ignore_seat: int | None = None, can_phase: bool = False) -> int | None:
    if start == target:
        return 0
    seen = {(start["x"], start["y"])}
    queue = [(start["x"], start["y"], 0)]
    for x, y, steps in queue:
        for dx, dy in DIRS:
            nx, ny = x + dx, y + dy
            key = (nx, ny)
            if key in seen:
                continue
            cell = cell_at(nx, ny)
            if not cell:
                continue
            if cell.get("obstacle") and not can_phase:
                continue
            if occupied(room, nx, ny, ignore_seat) and {"x": nx, "y": ny} != target:
                continue
            if {"x": nx, "y": ny} == target:
                return steps + 1
            seen.add(key)
            queue.append((nx, ny, steps + 1))
    return None


def card_by_id(hero_key: str, card_id: str) -> dict | None:
    return next((c for c in CARDS[hero_key] if c["id"] == card_id), None)


def effective_card(player: dict, card_id: str) -> dict | None:
    base = card_by_id(player["heroKey"], card_id)
    if not base:
        return None
    c = dict(base)
    bonuses = player.get("bonuses", {})
    color_key = color_key_for_card(c)
    card_upgrade = player.get("cardUpgrades", {}).get(color_key, {}) if color_key else {}
    stat_bonuses = {
        "initiative": card_upgrade.get("initiative", 0),
        "movement": card_upgrade.get("movement", 0),
        "attack": bonuses.get("damage", 0) if c["attack"] is not None else 0,
        "defense": bonuses.get("defense", 0) if c["defense"] is not None else 0,
    }
    c["baseStats"] = {
        "initiative": base["initiative"],
        "movement": base["movement"],
        "attack": base["attack"],
        "defense": base["defense"],
    }
    c["bonusStats"] = stat_bonuses
    c["initiative"] += stat_bonuses["initiative"]
    c["movement"] += stat_bonuses["movement"]
    if c["attack"] is not None:
        c["attack"] += stat_bonuses["attack"]
    if c["defense"] is not None:
        c["defense"] += stat_bonuses["defense"]
    return c


def effective_cards(player: dict) -> list[dict]:
    cards = [effective_card(player, c["id"]) for c in CARDS[player["heroKey"]]]
    if player.get("hasUltimate"):
        cards.append({
            "id": f"{player['heroKey']}-ultimate",
            "color": "紫",
            "initiative": None,
            "defense": None,
            "movement": None,
            "attack": None,
            "name": "大招占位",
            "text": "紫色大招占位符：当前无效果，不进入手牌逻辑。",
            "primary": "ultimatePassive",
            "displayOnly": True,
            "baseStats": {"initiative": None, "movement": None, "attack": None, "defense": None},
            "bonusStats": {"initiative": 0, "movement": 0, "attack": 0, "defense": 0},
        })
    return cards


def color_key_for_card(card: dict) -> str | None:
    return {"红": "red", "绿": "green", "蓝": "blue"}.get(card.get("color"))


def hero_spawn_cells(team: str) -> list[dict]:
    state = f"{team}HeroSpawn"
    return [c for c in MAP if c["state"] == state and not c.get("obstacle")]


def front_regions(front: int) -> list[str]:
    if front <= -2:
        return ["blueFountain"]
    if front == -1:
        return ["blueNear"]
    if front == 0:
        return ["mid"]
    if front == 1:
        return ["redNear"]
    return ["redFountain"]


def minion_spawn_cells(team: str, kind: str, front: int) -> list[dict]:
    state = f"{team}{kind.title()}Spawn"
    regions = set(front_regions(front))
    return sorted(
        [c for c in MAP if c["state"] == state and c["region"] in regions and not c.get("obstacle")],
        key=lambda c: (c["y"], c["x"]),
    )


def spawn_minions(front: int, existing: list[dict]) -> list[dict]:
    result = []
    for team in ("blue", "red"):
        for kind in ("melee", "ranged", "heavy"):
            for cell in minion_spawn_cells(team, kind, front):
                pos = first_free(cell, existing + result)
                if pos:
                    result.append({"id": f"{team[0]}-{kind}-{front}-{len(result)}", "team": team, "kind": kind, **pos})
    return result


def first_free(cell: dict, pieces: list[dict]) -> dict | None:
    blocked = {(p["x"], p["y"]) for p in pieces if "x" in p}
    if (cell["x"], cell["y"]) not in blocked:
        return {"x": cell["x"], "y": cell["y"]}
    for dx, dy in DIRS:
        nx, ny = cell["x"] + dx, cell["y"] + dy
        if in_bounds(nx, ny) and (nx, ny) not in blocked:
            return {"x": nx, "y": ny}
    return None


def regions_touch(a: str, b: str) -> bool:
    if not a or not b:
        return False
    if a == b:
        return True
    for c in MAP:
        if c["region"] != a:
            continue
        for dx, dy in DIRS:
            n = cell_at(c["x"] + dx, c["y"] + dy)
            if n and n["region"] == b:
                return True
    return False


def enemy_in_region(room: dict, team: str, region: str) -> bool:
    for p in room["players"]:
        if p["team"] != team and p.get("pos") and not p["defeated"]:
            c = cell_at(p["pos"]["x"], p["pos"]["y"])
            if c and c["region"] == region:
                return True
    for m in room["minions"]:
        if m["team"] != team:
            c = cell_at(m["x"], m["y"])
            if c and c["region"] == region:
                return True
    return False


def can_fast_travel(room: dict, actor: dict, target: dict) -> bool:
    if not actor.get("pos"):
        return False
    cur = cell_at(actor["pos"]["x"], actor["pos"]["y"])
    return bool(cur and regions_touch(cur["region"], target["region"]) and not enemy_in_region(room, actor["team"], cur["region"]) and not enemy_in_region(room, actor["team"], target["region"]))


def adjacent(a: dict, b: dict) -> bool:
    return dist(a, b) == 1


def minion_at(room: dict, x: int, y: int) -> dict | None:
    return next((m for m in room["minions"] if m["x"] == x and m["y"] == y), None)


def hero_at(room: dict, x: int, y: int) -> dict | None:
    return next((p for p in room["players"] if p.get("pos") == {"x": x, "y": y} and not p["defeated"]), None)


def damage_after_minions(room: dict, attacker: dict, defender: dict, base: int) -> tuple[int, list[str]]:
    total = base
    notes = []
    target = defender["pos"]
    for m in room["minions"]:
        d = dist(target, m)
        if m["team"] != defender["team"] and m["kind"] == "ranged" and d <= 2:
            total += 1
            notes.append("敌方远程+1")
        if m["team"] != defender["team"] and m["kind"] in ("melee", "heavy") and d <= 1:
            total += 1
            notes.append(f"敌方{minion_name(m['kind'])}+1")
        if m["team"] == defender["team"] and m["kind"] in ("melee", "heavy") and d <= 1:
            total -= 1
            notes.append(f"友方{minion_name(m['kind'])}-1")
    return max(0, total), notes


def minion_name(kind: str) -> str:
    return {"melee": "近战", "ranged": "远程", "heavy": "重型"}.get(kind, "小兵")


def reveal(room: dict) -> None:
    room["phase"] = "reveal"
    order = []
    for p in room["players"]:
        if not p["selectedCardId"]:
            p["resolved"] = True
            p["actionTaken"] = True
            p["played"] = None
            continue
        p["played"] = p["selectedCardId"]
        p["selectedCardId"] = None
        p["resolved"] = False
        p["actionTaken"] = False
        if p["played"] in p["hand"]:
            p["hand"].remove(p["played"])
        mark_round_used(p, p["played"])
        c = effective_card(p, p["played"])
        order.append({"seat": p["seat"], "initiative": c["initiative"] if c else 0})
    order.sort(key=lambda item: (-item["initiative"], item["seat"]))
    room["resolutionOrder"] = [item["seat"] for item in order]
    if order:
        touch(room, "全部翻牌，进入先攻结算。")
        advance_resolution(room)
    else:
        end_turn(room)


def unresolved_played(room: dict) -> list[dict]:
    items = []
    for p in room["players"]:
        if not p["resolved"] and p["played"]:
            c = effective_card(p, p["played"])
            items.append({"seat": p["seat"], "initiative": c["initiative"] if c else 0, "team": p["team"]})
    return items


def advance_resolution(room: dict) -> None:
    room["pendingCaptainChoice"] = None
    remaining = unresolved_played(room)
    if not remaining:
        end_turn(room)
        return
    top_init = max(item["initiative"] for item in remaining)
    tied = [item for item in remaining if item["initiative"] == top_init]
    teams = sorted({item["team"] for item in tied})
    if len(tied) == 1:
        room["activeSeat"] = tied[0]["seat"]
        touch(room, f"轮到座位 {room['activeSeat'] + 1} 结算。")
        return
    if len(teams) > 1:
        preferred = room["tiebreaker"]
        candidates = [item for item in tied if item["team"] == preferred]
        room["tiebreaker"] = "red" if room["tiebreaker"] == "blue" else "blue"
        touch(room, f"同先攻 {top_init}，破平币指定{team_name(preferred)}先结算并翻面。")
        if len(candidates) == 1:
            room["activeSeat"] = candidates[0]["seat"]
            return
        set_captain_choice(room, preferred, [item["seat"] for item in candidates], f"{team_name(preferred)}同先攻 {top_init}")
        return
    team = teams[0]
    set_captain_choice(room, team, [item["seat"] for item in tied], f"{team_name(team)}同先攻 {top_init}")


def set_captain_choice(room: dict, team: str, seats: list[int], reason: str) -> None:
    captain = room["captains"].get(team)
    room["activeSeat"] = captain
    room["pendingCaptainChoice"] = {"team": team, "seats": seats, "reason": reason}
    touch(room, f"{reason}，等待{team_name(team)}队长选择先结算者。")


def finish_active(room: dict) -> None:
    active = room["players"][room["activeSeat"]]
    active["resolved"] = True
    if active["played"]:
        active["discard"].append(active["played"])
        active["played"] = None
    advance_resolution(room)


def end_turn(room: dict) -> None:
    for p in room["players"]:
        p["resolved"] = False
        p["selectedCardId"] = None
        p["actionTaken"] = False
        if p["played"]:
            p["discard"].append(p["played"])
            p["played"] = None
    room["activeSeat"] = None
    room["resolutionOrder"] = []
    if room["turn"] < 4:
        room["turn"] += 1
        room["phase"] = "planning"
        touch(room, f"第 {room['turn']} 回合开始，暗选手牌。")
    else:
        end_round(room)


def end_round(room: dict) -> None:
    for p in room["players"]:
        p["hand"] = full_hand(p["heroKey"])
        p["discard"] = []
        p["played"] = None
        p["selectedCardId"] = None
        p["roundUsed"] = []
        p["resolved"] = False
        p["actionTaken"] = False
    room["activeSeat"] = None
    room["resolutionOrder"] = []
    room["pendingDefense"] = None
    room["pendingCaptainChoice"] = None
    touch(room, "一轮结束，回收手牌并结算兵线。")
    if resolve_minions(room):
        start_next_round(room)


def start_next_round(room: dict) -> None:
    collect_upgrade_payments(room)
    if begin_upgrade_phase(room):
        return
    room["round"] += 1
    room["turn"] = 1
    room["phase"] = "planning"
    room["pendingMinionRemoval"] = None
    touch(room, f"第 {room['round']} 轮开始。")


def collect_upgrade_payments(room: dict) -> None:
    room["pendingUpgrades"] = []
    for player in room["players"]:
        gained = 0
        while player["heroLevel"] < 8 and player["coins"] >= player["heroLevel"]:
            cost = player["heroLevel"]
            player["coins"] -= cost
            player["heroLevel"] += 1
            gained += 1
        for _ in range(gained):
            room["pendingUpgrades"].append({"seat": player["seat"]})
        if gained:
            touch(room, f"{player['name']} 自动花费金币升至 {player['heroLevel']} 级，需要选择 {gained} 次升级。")


def begin_upgrade_phase(room: dict) -> bool:
    while room["pendingUpgrades"]:
        seat = room["pendingUpgrades"][0]["seat"]
        player = room["players"][seat]
        if should_gain_ultimate(player):
            player["hasUltimate"] = True
            room["pendingUpgrades"].pop(0)
            touch(room, f"{player['name']} 获得紫色大招占位。")
            continue
        room["phase"] = "upgrade"
        room["activeSeat"] = seat
        touch(room, f"等待 {player['name']} 选择卡牌升级。")
        return True
    return False


def finish_upgrades_and_start_round(room: dict) -> None:
    room["round"] += 1
    room["turn"] = 1
    room["phase"] = "planning"
    room["activeSeat"] = None
    room["pendingMinionRemoval"] = None
    touch(room, f"第 {room['round']} 轮开始。")


def should_gain_ultimate(player: dict) -> bool:
    return not player.get("hasUltimate") and all(player["skills"][color] >= 3 for color in ("red", "green", "blue"))


def available_upgrade_colors(player: dict) -> list[str]:
    colors = ["red", "green", "blue"]
    if all(player["skills"][color] >= 2 for color in colors):
        return [color for color in colors if player["skills"][color] < 3]
    return [color for color in colors if player["skills"][color] < 2]


def apply_card_upgrade(player: dict, color: str, direction: str) -> tuple[int, str, str]:
    if color not in available_upgrade_colors(player):
        raise ApiError(400, "This color cannot upgrade now")
    next_level = player["skills"][color] + 1
    value = next_level - 1
    color_name = {"red": "红", "green": "绿", "blue": "蓝"}[color]
    if direction == "initiative":
        player["cardUpgrades"][color]["initiative"] += value
        player["bonuses"]["damage"] += value
        passive = f"伤害+{value}"
        active = f"先攻+{value}"
    elif direction == "movement":
        player["cardUpgrades"][color]["movement"] += value
        player["bonuses"]["defense"] += value
        passive = f"防御+{value}"
        active = f"移动+{value}"
    else:
        raise ApiError(400, "Invalid upgrade direction")
    player["skills"][color] = next_level
    player["passives"].append(passive)
    return next_level, f"{color_name}卡{active}", passive


def legacy_auto_upgrade(player: dict, room: dict) -> None:
    while player["coins"] >= player["heroLevel"] and player["heroLevel"] < 8:
        cost = player["heroLevel"]
        player["coins"] -= cost
        player["heroLevel"] += 1


def resolve_minions(room: dict) -> bool:
    blue = sum(1 for m in room["minions"] if m["team"] == "blue")
    red = sum(1 for m in room["minions"] if m["team"] == "red")
    if blue == red:
        return True
    losing = "blue" if blue < red else "red"
    remove_count = abs(blue - red)
    room["pendingMinionRemoval"] = {"team": losing, "count": remove_count}
    room["phase"] = "minionChoice"
    room["activeSeat"] = room["captains"][losing]
    touch(room, f"{team_name(losing)}少 {remove_count} 个小兵，等待队长选择移除。")
    return False


def finish_minion_resolution(room: dict) -> None:
    losing = None
    for team in ("blue", "red"):
        if not any(m["team"] == team for m in room["minions"]):
            losing = team
            break
    if not losing:
        start_next_round(room)
        return
    if not any(m["team"] == losing for m in room["minions"]):
        advancing = "red" if losing == "blue" else "blue"
        room["minions"] = []
        room["front"] += 1 if advancing == "blue" else -1
        room["frontMarks"][advancing] += 1
        touch(room, f"{team_name(advancing)}推进战线。")
        if room["frontMarks"][advancing] >= 3 or abs(room["front"]) >= 5:
            room["winner"] = advancing
            room["phase"] = "ended"
        else:
            room["minions"] = spawn_minions(room["front"], [])
    start_next_round(room)


def team_name(team: str) -> str:
    return "蓝方" if team == "blue" else "红方"


class Handler(SimpleHTTPRequestHandler):
    def do_GET(self) -> None:
        parsed = urlparse(self.path)
        if parsed.path.startswith("/api/"):
            self.send_json(404, {"error": "Unknown endpoint"})
            return
        self.path = "/index.html" if parsed.path == "/" else parsed.path
        return SimpleHTTPRequestHandler.do_GET(self)

    def do_POST(self) -> None:
        length = int(self.headers.get("content-length", "0"))
        body = json.loads(self.rfile.read(length).decode("utf-8") or "{}")
        try:
            data = self.route(urlparse(self.path).path, body)
            self.send_json(200, data)
        except ApiError as exc:
            self.send_json(exc.status, {"error": exc.message})
        except Exception as exc:
            self.send_json(500, {"error": str(exc)})

    def route(self, path: str, body: dict) -> dict:
        if path == "/api/create":
            code = secrets.token_hex(2).upper()
            room = new_room(code)
            ROOMS[code] = room
            token = body.get("token") or secrets.token_hex(16)
            room["players"][0]["token"] = token
            room["players"][0]["name"] = str(body.get("name") or "玩家1")[:24]
            room["captains"]["blue"] = 0
            touch(room, "房间已创建。")
            touch(room, f"{room['players'][0]['name']} 加入{team_name(room['players'][0]['team'])}座位 1。")
            return {"code": code, "token": token, "state": public_state(room, token)}

        room = ROOMS.get(str(body.get("code", "")).upper())
        if not room:
            raise ApiError(404, "Room not found")
        token = body.get("token")

        if path == "/api/state":
            return public_state(room, token)

        if path == "/api/join":
            if room["phase"] != "lobby":
                raise ApiError(400, "Game already started")
            seat = int(body.get("seat", 0))
            player = room["players"][seat]
            if player["token"] and player["token"] != token:
                raise ApiError(409, "Seat already taken")
            token = token or secrets.token_hex(16)
            for p in room["players"]:
                if p["seat"] != seat and p["token"] == token:
                    p["token"] = None
                    p["name"] = ""
            player["token"] = token
            player["name"] = str(body.get("name") or f"玩家{seat + 1}")[:24]
            if room["captains"][player["team"]] is None:
                room["captains"][player["team"]] = seat
            touch(room, f"{player['name']} 加入{team_name(player['team'])}座位 {seat + 1}。")
            return {"token": token, "state": public_state(room, token)}

        actor = next((p for p in room["players"] if p["token"] == token), None)
        if not actor:
            raise ApiError(403, "Join a seat first")

        if path == "/api/captain":
            room["captains"][actor["team"]] = actor["seat"]
            touch(room, f"{actor['name']} 成为{team_name(actor['team'])}队长。")
            return public_state(room, token)

        if path == "/api/spawn":
            self_respawn = room["phase"] == "reveal" and room["activeSeat"] == actor["seat"] and actor["needsSpawn"]
            if not self_respawn and room["captains"][actor["team"]] != actor["seat"]:
                raise ApiError(400, "Captain only")
            x, y = int(body["x"]), int(body["y"])
            c = cell_at(x, y)
            if not c or c["state"] != f"{actor['team']}HeroSpawn":
                raise ApiError(400, "Invalid hero spawn")
            if occupied(room, x, y):
                raise ApiError(400, "Spawn already occupied")
            target = actor if self_respawn else next((p for p in room["players"] if p["team"] == actor["team"] and (p["needsSpawn"] or p["defeated"] or not p["pos"])), None)
            if not target:
                raise ApiError(400, "No hero needs spawn")
            if self_respawn:
                room["lives"][actor["team"]] -= 1
                if room["lives"][actor["team"]] <= 0:
                    room["winner"] = "red" if actor["team"] == "blue" else "blue"
                    room["phase"] = "ended"
            target["pos"] = {"x": x, "y": y}
            target["needsSpawn"] = False
            target["defeated"] = False
            touch(room, f"{actor['name']} 选择出生点 {x},{y}。")
            return public_state(room, token)

        if path == "/api/start":
            if any(not p["token"] for p in room["players"]):
                raise ApiError(400, "Need all 4 seats filled")
            if any(p["needsSpawn"] or not p["pos"] for p in room["players"]):
                raise ApiError(400, "Need captains choose spawns")
            room["phase"] = "planning"
            touch(room, "游戏开始，进入暗选。")
            return public_state(room, token)

        if path == "/api/select":
            if room["phase"] != "planning":
                raise ApiError(400, "Not in planning phase")
            card_id = body["cardId"]
            if card_id not in actor["hand"]:
                raise ApiError(400, "Card not in hand")
            actor["selectedCardId"] = card_id
            touch(room, f"{actor['name']} 已暗选。")
            if all(p["selectedCardId"] or not p["hand"] for p in room["players"]):
                reveal(room)
            return public_state(room, token)

        if path == "/api/move":
            if room["phase"] != "reveal" or room["activeSeat"] != actor["seat"]:
                raise ApiError(400, "Not your resolution")
            if actor["actionTaken"]:
                raise ApiError(400, "Action already used")
            x, y = int(body["x"]), int(body["y"])
            target = cell_at(x, y)
            if not target or not in_bounds(x, y) or occupied(room, x, y, actor["seat"]):
                raise ApiError(400, "Invalid destination")
            played = effective_card(actor, actor["played"])
            if not played:
                raise ApiError(400, "No played card")
            can_phase = bool(played.get("canPhaseThroughWalls"))
            steps = walk_distance(room, actor["pos"], {"x": x, "y": y}, actor["seat"], can_phase)
            if not can_fast_travel(room, actor, target) and (steps is None or steps > played["movement"]):
                raise ApiError(400, "Too far for card movement")
            actor["pos"] = {"x": x, "y": y}
            actor["actionTaken"] = True
            touch(room, f"{actor['name']} 移动到 {x},{y}。")
            return public_state(room, token)

        if path == "/api/main-action":
            if room["phase"] != "reveal" or room["activeSeat"] != actor["seat"]:
                raise ApiError(400, "Not your resolution")
            if actor["actionTaken"]:
                raise ApiError(400, "Action already used")
            played = effective_card(actor, actor["played"])
            if not played:
                raise ApiError(400, "No played card")
            x, y = int(body["x"]), int(body["y"])
            if not actor.get("pos"):
                raise ApiError(400, "Actor not on board")
            if played["primary"] != "attackAny" and not adjacent(actor["pos"], {"x": x, "y": y}):
                raise ApiError(400, "Need adjacent target")
            if played["primary"] == "killMinion":
                m = minion_at(room, x, y)
                if not m or m["team"] == actor["team"]:
                    raise ApiError(400, "Need adjacent enemy minion")
                if m["kind"] == "heavy" and any(mm["team"] == m["team"] and mm["kind"] != "heavy" for mm in room["minions"]):
                    raise ApiError(400, "Heavy minion cannot be killed before other friendly minions")
                room["minions"] = [mm for mm in room["minions"] if mm["id"] != m["id"]]
                gain = 4 if m["kind"] == "heavy" else 2
                actor["coins"] += gain
                actor["actionTaken"] = True
                touch(room, f"{actor['name']} 击杀{team_name(m['team'])}{minion_name(m['kind'])}小兵，获得 {gain} 金。")
                return public_state(room, token)
            if played["primary"] in ("attackHero", "attackAny"):
                defender = hero_at(room, x, y)
                target_minion = minion_at(room, x, y)
                if played["primary"] == "attackHero" and (not defender or defender["team"] == actor["team"]):
                    raise ApiError(400, "Need adjacent enemy hero")
                if played["primary"] == "attackAny" and target_minion and target_minion["team"] != actor["team"]:
                    if target_minion["kind"] == "heavy" and any(mm["team"] == target_minion["team"] and mm["kind"] != "heavy" for mm in room["minions"]):
                        raise ApiError(400, "Heavy minion cannot be killed before other friendly minions")
                    room["minions"] = [mm for mm in room["minions"] if mm["id"] != target_minion["id"]]
                    gain = 4 if target_minion["kind"] == "heavy" else 2
                    actor["coins"] += gain
                    actor["actionTaken"] = True
                    touch(room, f"{actor['name']} 攻击并击杀{team_name(target_minion['team'])}{minion_name(target_minion['kind'])}小兵，获得 {gain} 金。")
                    return public_state(room, token)
                if not defender or defender["team"] == actor["team"]:
                    raise ApiError(400, "Need enemy target")
                damage, notes = damage_after_minions(room, actor, defender, played["attack"] or 0)
                room["pendingDefense"] = {"attackerSeat": actor["seat"], "defenderSeat": defender["seat"], "damage": damage}
                room["activeSeat"] = defender["seat"]
                room["phase"] = "defense"
                detail = "，".join(notes) if notes else "无修正"
                touch(room, f"{actor['name']} 攻击 {defender['name']}：基础{played['attack']}，{detail}，最终伤害{damage}。")
                return public_state(room, token)
            raise ApiError(400, "This card has no implemented primary action")

        if path == "/api/choose-active":
            choice = room.get("pendingCaptainChoice")
            seat = int(body["seat"])
            if not choice or room["captains"].get(choice["team"]) != actor["seat"]:
                raise ApiError(400, "Captain choice not available")
            if seat not in choice["seats"]:
                raise ApiError(400, "Invalid chosen seat")
            room["pendingCaptainChoice"] = None
            room["activeSeat"] = seat
            touch(room, f"{actor['name']} 选择座位 {seat + 1} 先结算。")
            return public_state(room, token)

        if path == "/api/defend":
            pending = room.get("pendingDefense")
            if room["phase"] != "defense" or not pending or pending["defenderSeat"] != actor["seat"]:
                raise ApiError(400, "Not your defense")
            card_id = body.get("cardId")
            attacker = room["players"][pending["attackerSeat"]]
            damage = pending["damage"]
            if card_id:
                if card_id not in actor["hand"]:
                    raise ApiError(400, "Defense card not in hand")
                defense_card = effective_card(actor, card_id)
                if (defense_card.get("defense") or 0) < damage:
                    raise ApiError(400, "Defense too low")
                actor["hand"].remove(card_id)
                actor["discard"].append(card_id)
                touch(room, f"{actor['name']} 弃置 {defense_card['name']} 防御 {defense_card['defense']}，防住伤害 {damage}。")
            else:
                actor["defeated"] = True
                actor["needsSpawn"] = True
                actor["pos"] = None
                gain = actor["heroLevel"]
                attacker["coins"] += gain
                touch(room, f"{actor['name']} 未防住伤害 {damage}，死亡；{attacker['name']} 获得 {gain} 金。")
            attacker["actionTaken"] = True
            room["pendingDefense"] = None
            room["phase"] = "reveal"
            room["activeSeat"] = attacker["seat"]
            return public_state(room, token)

        if path == "/api/skip-action":
            if room["phase"] != "reveal" or room["activeSeat"] != actor["seat"]:
                raise ApiError(400, "Not your resolution")
            actor["actionTaken"] = True
            touch(room, f"{actor['name']} 放弃本张牌效果。")
            return public_state(room, token)

        if path == "/api/remove-minion":
            pending = room.get("pendingMinionRemoval")
            if room["phase"] != "minionChoice" or not pending or room["captains"].get(pending["team"]) != actor["seat"]:
                raise ApiError(400, "Not your minion choice")
            x, y = int(body["x"]), int(body["y"])
            m = minion_at(room, x, y)
            if not m or m["team"] != pending["team"]:
                raise ApiError(400, "Choose your team's minion")
            if m["kind"] == "heavy" and any(mm["team"] == m["team"] and mm["kind"] != "heavy" for mm in room["minions"]):
                raise ApiError(400, "Heavy minion must be removed last")
            room["minions"] = [mm for mm in room["minions"] if mm["id"] != m["id"]]
            pending["count"] -= 1
            touch(room, f"{actor['name']} 移除{team_name(m['team'])}{minion_name(m['kind'])}小兵。")
            if pending["count"] <= 0:
                room["pendingMinionRemoval"] = None
                finish_minion_resolution(room)
            return public_state(room, token)

        if path == "/api/upgrade":
            if room["phase"] != "upgrade" or room["activeSeat"] != actor["seat"] or not room["pendingUpgrades"]:
                raise ApiError(400, "Not your upgrade")
            color = str(body.get("color"))
            direction = str(body.get("direction"))
            level, active_text, passive_text = apply_card_upgrade(actor, color, direction)
            room["pendingUpgrades"].pop(0)
            touch(room, f"{actor['name']} 将{ {'red':'红','green':'绿','blue':'蓝'}[color] }卡升至 {level} 级：{active_text}，被动{passive_text}。")
            if not begin_upgrade_phase(room):
                finish_upgrades_and_start_round(room)
            return public_state(room, token)

        if path == "/api/finish":
            if room["phase"] != "reveal" or room["activeSeat"] != actor["seat"]:
                raise ApiError(400, "Not your resolution")
            finish_active(room)
            return public_state(room, token)

        if path == "/api/debug/end-round":
            end_round(room)
            return public_state(room, token)

        if path == "/api/debug/coin":
            actor["coins"] += 1
            touch(room, f"{actor['name']} 获得 1 调试金币。")
            return public_state(room, token)

        if path == "/api/debug/coins99":
            actor["coins"] += 99
            touch(room, f"{actor['name']} 获得 99 调试金币。")
            return public_state(room, token)

        if path == "/api/debug/teleport":
            x, y = int(body["x"]), int(body["y"])
            if not in_bounds(x, y):
                raise ApiError(400, "Invalid destination")
            actor["pos"] = {"x": x, "y": y}
            actor["needsSpawn"] = False
            actor["defeated"] = False
            touch(room, f"{actor['name']} 调试传送到 {x},{y}。")
            return public_state(room, token)

        if path == "/api/debug/kill":
            x, y = int(body["x"]), int(body["y"])
            m = minion_at(room, x, y)
            h = hero_at(room, x, y)
            if m:
                room["minions"] = [mm for mm in room["minions"] if mm["id"] != m["id"]]
                touch(room, f"{actor['name']} 调试击杀{team_name(m['team'])}{minion_name(m['kind'])}小兵。")
            elif h and h["seat"] != actor["seat"]:
                h["defeated"] = True
                h["needsSpawn"] = True
                h["pos"] = None
                touch(room, f"{actor['name']} 调试击杀 {h['name']}。")
            else:
                raise ApiError(400, "No unit on target")
            return public_state(room, token)

        raise ApiError(404, "Unknown endpoint")

    def send_json(self, status: int, payload: dict) -> None:
        data = json.dumps(payload, ensure_ascii=False).encode("utf-8")
        self.send_response(status)
        self.send_header("content-type", "application/json; charset=utf-8")
        self.send_header("cache-control", "no-store")
        self.send_header("content-length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)


class ApiError(Exception):
    def __init__(self, status: int, message: str):
        self.status = status
        self.message = message
        super().__init__(message)


if __name__ == "__main__":
    import os

    os.chdir(STATIC)
    server = ThreadingHTTPServer(("127.0.0.1", PORT), Handler)
    print(f"GoA2 v2 running at http://localhost:{PORT}")
    server.serve_forever()
