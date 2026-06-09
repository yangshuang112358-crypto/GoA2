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
CARD_DRAFT = DATA / "cards_draft" / "cards_ocr_draft.json"
CARD_DATA = DATA / "cards.json"

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


def formal_cards_to_game_cards(hero: dict) -> list[dict]:
    result = []
    for source in hero.get("cards", []):
        primary_action = source.get("primary_action", {})
        secondary = source.get("secondary_actions", {})
        defense = secondary.get("defense", {})
        movement = secondary.get("movement", {})
        family = primary_action.get("family")
        subtype = primary_action.get("subtype")
        primary = {
            "attack": "attackGeneric",
            "skill": "skillGeneric",
            "ultimate": "skillGeneric",
            "defense": "defenseGeneric",
            "defense_skill": "skillGeneric",
            "movement": "primaryMove",
        }.get(family, "effectGeneric")
        if family == "attack" and subtype and subtype.get("type") == "范围":
            primary = "attackArea"
        elif family == "attack" and subtype and subtype.get("type") == "远程":
            primary = "attackRanged"
        result.append({
            "id": source["id"],
            "color": source["color"],
            "level": source.get("level"),
            "initiative": source.get("initiative") or 0,
            "defense": defense.get("value") if defense.get("has_action") else None,
            "movement": movement.get("value") if movement.get("has_action") else None,
            "attack": primary_action.get("value") if family == "attack" else None,
            "name": source.get("name") or "未命名",
            "text": primary_action.get("text") or "",
            "primary": primary,
            "primaryCategory": primary_action.get("category"),
            "actionFamily": family,
            "subtype": subtype,
            "exclamation": bool(primary_action.get("exclamation")),
            "passiveBonus": (source.get("passive_bonus") or {}).get("type"),
            "sourceCard": source,
        })
    return result


def load_card_data() -> tuple[dict, dict]:
    raw = json.loads(CARD_DATA.read_text(encoding="utf-8"))
    heroes = {}
    cards = {}
    active_heroes = {"wasp", "shargatha", "brogan", "arien"}
    for hero in raw.get("heroes", []):
        key = hero["hero_id"]
        implemented = bool(hero.get("implemented") or key in active_heroes)
        heroes[key] = {
            "name": hero["name"],
            "title": hero["name"],
            "implemented": implemented,
        }
        cards[key] = formal_cards_to_game_cards(hero) if implemented else test_cards(key)
    return heroes, cards


HEROES, CARDS = load_card_data()


ROOMS: dict[str, dict] = {}


def new_room(code: str) -> dict:
    hero_keys = ["brogan", "wasp", "arien", "shargatha"]
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
        "currentTrick": [],
        "pendingDefense": None,
        "pendingCaptainChoice": None,
        "pendingMinionRemoval": None,
        "pendingUpgrades": [],
        "decisionCoin": None,
        "effects": [],
        "log": [],
        "players": [
            (lambda active_cards: {
                "seat": i,
                "token": None,
                "name": "",
                "team": "blue" if i % 2 == 0 else "red",
                "heroKey": hero_keys[i],
                "pos": None,
                "needsSpawn": True,
                "defeated": False,
                "activeSkillCards": active_cards,
                "hand": starting_hand(hero_keys[i], {"activeSkillCards": active_cards}),
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
                "bonuses": {"damage": 0, "defense": 0, "initiative": 0, "movement": 0, "range": 0, "ranged": 0},
                "cardUpgrades": {"red": {"initiative": 0, "movement": 0}, "green": {"initiative": 0, "movement": 0}, "blue": {"initiative": 0, "movement": 0}},
                "hasUltimate": False,
            })(init_active_skill_cards(hero_keys[i]))
            for i in range(4)
        ],
        "minions": spawn_minions(0, []),
    }


def public_state(room: dict, token: str | None) -> dict:
    state = deepcopy(room)
    state["decisionCoin"] = room.get("decisionCoin") or room.get("tiebreaker")
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
    return starting_hand(hero_key)


def is_implemented_hero(hero_key: str) -> bool:
    return bool(HEROES.get(hero_key, {}).get("implemented"))


def starting_hand(hero_key: str, player: dict | None = None) -> list[str]:
    if not is_implemented_hero(hero_key):
        return [c["id"] for c in CARDS[hero_key]]
    result = []
    active = (player or {}).get("activeSkillCards", {})
    for c in CARDS[hero_key]:
        color_key = color_key_for_card(c)
        if c.get("color") in ("金", "银"):
            result.append(c["id"])
        elif color_key and active.get(color_key) == c["id"]:
            result.append(c["id"])
        elif color_key and not active.get(color_key) and c.get("level") == 1:
            result.append(c["id"])
    return result


def init_active_skill_cards(hero_key: str) -> dict:
    active = {"red": None, "green": None, "blue": None}
    if not is_implemented_hero(hero_key):
        return active
    for color in active:
        card = next((c for c in CARDS[hero_key] if color_key_for_card(c) == color and c.get("level") == 1), None)
        active[color] = card["id"] if card else None
    return active


def mark_round_used(player: dict, card_id: str | None) -> None:
    if card_id and card_id not in player["roundUsed"]:
        player["roundUsed"].append(card_id)


def playable_hand(player: dict) -> list[str]:
    used = set(player.get("roundUsed", [])) | set(player.get("discard", []))
    return [card_id for card_id in player.get("hand", []) if card_id not in used]


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


def has_movement_action(card: dict | None) -> bool:
    if not card:
        return False
    return card.get("movement") is not None or card.get("primary") in ("move", "primaryMove")


def normalized_action(action: str | None) -> str | None:
    return {
        "basicAttack": "attack",
        "基础攻击": "attack",
        "攻击": "attack",
        "basicSkill": "skill",
        "基础技能": "skill",
        "技能": "skill",
    }.get(action, action)


def immune_to(piece: dict | None, action: str | None) -> bool:
    if not piece:
        return False
    action = normalized_action(action)
    immunities = piece.get("immunities") or piece.get("immune") or []
    if isinstance(immunities, str):
        immunities = [immunities]
    normalized = {normalized_action(item) for item in immunities}
    return "all" in normalized or "全部" in normalized or action in normalized


def effect_in_range(source: dict, target: dict, rng: int) -> bool:
    return bool(source.get("pos") and target.get("pos") and dist(source["pos"], target["pos"]) <= rng)


def action_blocked_by_effect(room: dict, actor: dict, action: str) -> str | None:
    action = normalized_action(action)
    for effect in room.get("effects", []):
        source = room["players"][effect["sourceSeat"]]
        if source["team"] == actor["team"] or not effect_in_range(source, actor, effect.get("range", 0)):
            continue
        if effect["type"] == "noSkill" and action == "skill":
            return effect["name"]
        if effect["type"] == "noMove" and action == "movement":
            return effect["name"]
    return None


def movement_blocked_by_effect(room: dict, actor: dict, target: dict) -> str | None:
    base = action_blocked_by_effect(room, actor, "movement")
    if base:
        return base
    for effect in room.get("effects", []):
        if effect["type"] != "staticLock":
            continue
        source = room["players"][effect["sourceSeat"]]
        if source["team"] == actor["team"] or not source.get("pos") or not actor.get("pos"):
            continue
        rng = effect.get("range", 0)
        starts_inside = dist(source["pos"], actor["pos"]) <= rng
        ends_inside = dist(source["pos"], target) <= rng
        if starts_inside != ends_inside:
            return effect["name"]
    return None


def expire_turn_effects(room: dict) -> None:
    room["effects"] = [effect for effect in room.get("effects", []) if effect.get("duration") != "turn"]


def apply_text_effect(room: dict, actor: dict, card: dict) -> str | None:
    if card["id"] == "arien-06-打断施法":
        room.setdefault("effects", []).append({"type": "noSkill", "sourceSeat": actor["seat"], "range": card_range(card), "duration": "turn", "name": card["name"]})
        return f"技能范围 {card_range(card)} 内的敌方英雄本回合不能执行技能。"
    if card["id"] == "wasp-06-静电封锁":
        room.setdefault("effects", []).append({"type": "staticLock", "sourceSeat": actor["seat"], "range": card_range(card), "duration": "turn", "name": card["name"]})
        return f"技能范围 {card_range(card)} 形成静电封锁；移动穿越范围边界会被阻止。"
    return None


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
        "initiative": card_upgrade.get("initiative", 0) + bonuses.get("initiative", 0),
        "movement": card_upgrade.get("movement", 0) + bonuses.get("movement", 0),
        "attack": bonuses.get("damage", 0) if c["attack"] is not None else 0,
        "defense": bonuses.get("defense", 0) if c["defense"] is not None else 0,
        "range": bonuses.get("range", 0),
        "ranged": bonuses.get("ranged", 0),
    }
    c["baseStats"] = {
        "initiative": base["initiative"],
        "movement": base["movement"],
        "attack": base["attack"],
        "defense": base["defense"],
        "range": (base.get("subtype") or {}).get("value") if (base.get("subtype") or {}).get("type") == "范围" else None,
        "ranged": (base.get("subtype") or {}).get("value") if (base.get("subtype") or {}).get("type") == "远程" else None,
    }
    c["bonusStats"] = stat_bonuses
    if c["initiative"] is not None:
        c["initiative"] += stat_bonuses["initiative"]
    if c["movement"] is not None:
        c["movement"] += stat_bonuses["movement"]
    if c["attack"] is not None:
        c["attack"] += stat_bonuses["attack"]
    if c["defense"] is not None:
        c["defense"] += stat_bonuses["defense"]
    return c


def effective_cards(player: dict) -> list[dict]:
    cards = [effective_card(player, c["id"]) for c in CARDS[player["heroKey"]] if c.get("color") != "紫"]
    if player.get("hasUltimate"):
        ultimate = next((c for c in CARDS[player["heroKey"]] if c.get("color") == "紫"), None)
        if ultimate:
            ultimate = effective_card(player, ultimate["id"])
            ultimate["displayOnly"] = True
            cards.append(ultimate)
        else:
            cards.append({
                "id": f"{player['heroKey']}-ultimate",
                "color": "紫",
                "initiative": None,
                "defense": None,
                "movement": None,
                "attack": None,
                "name": "大招占位",
                "text": "紫色大招占位符：当前无效果，不可选择。",
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


def card_range(card: dict) -> int:
    subtype = card.get("subtype")
    if isinstance(subtype, dict) and subtype.get("value") is not None:
        bonus_key = {"范围": "range", "远程": "ranged"}.get(subtype.get("type"))
        return int(subtype["value"]) + int((card.get("bonusStats") or {}).get(bonus_key, 0))
    return 1


def in_card_range(actor: dict, card: dict, target: dict) -> bool:
    return dist(actor["pos"], target) <= card_range(card)


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
def assist_gold_for_level(level: int) -> int:
    if level <= 3:
        return 1
    if level <= 6:
        return 2
    return 3


def defeat_hero(room: dict, attacker: dict, defender: dict) -> None:
    defeated_level = int(defender.get("heroLevel", 1))
    defender["defeated"] = True
    defender["needsSpawn"] = True
    defender["pos"] = None
    attacker["coins"] += defeated_level
    assist_gold = assist_gold_for_level(defeated_level)
    for teammate in room["players"]:
        if teammate["team"] == attacker["team"] and teammate["seat"] != attacker["seat"]:
            teammate["coins"] += assist_gold
    defender_team = defender["team"]
    room["lives"][defender_team] -= defeated_level
    touch(room, f"{attacker['name']} 击败 {defender['name']}，获得 {defeated_level} 金；队友助攻获得 {assist_gold} 金；{team_name(defender_team)}生命 -{defeated_level}。")
    if room["lives"][defender_team] <= 0:
        room["winner"] = "red" if defender_team == "blue" else "blue"
        room["phase"] = "ended"


def advance_front_after_elimination(room: dict, losing: str) -> None:
    advancing = "red" if losing == "blue" else "blue"
    room["minions"] = []
    room["front"] += 1 if advancing == "blue" else -1
    room["frontMarks"][advancing] += 1
    touch(room, f"{team_name(advancing)}推进战线。")
    if room["frontMarks"][advancing] >= 3 or abs(room["front"]) >= 5:
        room["winner"] = advancing
        room["phase"] = "ended"
        return
    room["minions"] = spawn_minions(room["front"], [])


def after_minion_removed(room: dict, removed_team: str) -> None:
    if not any(m["team"] == removed_team for m in room["minions"]):
        advance_front_after_elimination(room, removed_team)


def reveal(room: dict) -> None:
    room["phase"] = "reveal"
    order = []
    room["currentTrick"] = []
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
        mark_round_used(p, p["played"])
        c = effective_card(p, p["played"])
        order.append({"seat": p["seat"], "initiative": c["initiative"] if c else 0})
        room["currentTrick"].append({"seat": p["seat"], "cardId": p["played"], "resolved": False})
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
        preferred = room.get("decisionCoin") or room["tiebreaker"]
        candidates = [item for item in tied if item["team"] == preferred]
        next_face = "red" if preferred == "blue" else "blue"
        room["tiebreaker"] = next_face
        room["decisionCoin"] = next_face
        touch(room, f"同先攻 {top_init}，决策币指定{team_name(preferred)}先结算并翻面。")
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
    for item in room.get("currentTrick", []):
        if item["seat"] == active["seat"] and item["cardId"] == active["played"]:
            item["resolved"] = True
    if active["played"]:
        active["discard"].append(active["played"])
        active["played"] = None
    advance_resolution(room)


def end_turn(room: dict) -> None:
    expire_turn_effects(room)
    for p in room["players"]:
        p["resolved"] = False
        p["selectedCardId"] = None
        p["actionTaken"] = False
        if p["played"]:
            p["discard"].append(p["played"])
            p["played"] = None
    room["activeSeat"] = None
    room["resolutionOrder"] = []
    room["currentTrick"] = []
    if room["turn"] < 4:
        room["turn"] += 1
        room["phase"] = "planning"
        touch(room, f"第 {room['turn']} 回合开始，暗选手牌。")
    else:
        end_round(room)


def end_round(room: dict) -> None:
    for p in room["players"]:
        p["hand"] = starting_hand(p["heroKey"], p)
        p["discard"] = []
        p["played"] = None
        p["selectedCardId"] = None
        p["roundUsed"] = []
        p["resolved"] = False
        p["actionTaken"] = False
    room["activeSeat"] = None
    room["resolutionOrder"] = []
    room["currentTrick"] = []
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
        if is_implemented_hero(player["heroKey"]):
            for _ in range(gained):
                room["pendingUpgrades"].append({"seat": player["seat"]})
        if gained:
            if is_implemented_hero(player["heroKey"]):
                touch(room, f"{player['name']} 自动花费金币升至 {player['heroLevel']} 级，需要选择 {gained} 次升级。")
            else:
                touch(room, f"{player['name']} 自动花费金币升至 {player['heroLevel']} 级。")
        if not gained:
            player["coins"] += 1
            touch(room, f"{player['name']} 本轮未升级，获得 1 金补偿（下轮可用）。")


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


def upgrade_candidates(player: dict, color: str) -> list[dict]:
    if color not in available_upgrade_colors(player):
        return []
    next_level = player["skills"][color] + 1
    return [c for c in CARDS[player["heroKey"]] if color_key_for_card(c) == color and c.get("level") == next_level]


def add_passive_bonus(player: dict, passive_type: str | None) -> str:
    if not passive_type:
        return "无"
    key = {"攻击": "damage", "防御": "defense", "先攻": "initiative", "移动": "movement", "范围": "range", "远程": "ranged"}.get(passive_type)
    if key:
        player["bonuses"][key] = player["bonuses"].get(key, 0) + 1
    text = f"{passive_type}+1"
    player["passives"].append(text)
    return text


def apply_card_upgrade(player: dict, color: str, card_id: str) -> tuple[int, str, str]:
    if color not in available_upgrade_colors(player):
        raise ApiError(400, "This color cannot upgrade now")
    next_level = player["skills"][color] + 1
    color_name = {"red": "红", "green": "绿", "blue": "蓝"}[color]
    candidates = upgrade_candidates(player, color)
    chosen = next((c for c in candidates if c["id"] == card_id), None)
    if not chosen:
        raise ApiError(400, "Invalid upgrade card")
    unchosen = next((c for c in candidates if c["id"] != card_id), None)
    passive = add_passive_bonus(player, unchosen.get("passiveBonus") if unchosen else None)
    player["skills"][color] = next_level
    player.setdefault("activeSkillCards", {})[color] = chosen["id"]
    player["hand"] = starting_hand(player["heroKey"], player)
    return next_level, f"{color_name}卡替换为 {chosen['name']}", passive


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
        advance_front_after_elimination(room, losing)
    if not room.get("winner"):
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
        if path == "/api/card-draft/load":
            return json.loads(CARD_DATA.read_text(encoding="utf-8"))

        if path == "/api/card-draft/save":
            draft = body.get("draft")
            if not isinstance(draft, dict) or not isinstance(draft.get("heroes"), list):
                raise ApiError(400, "Invalid card draft")
            CARD_DATA.write_text(json.dumps(draft, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
            return {"ok": True, "savedAt": int(time.time())}

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

        if path == "/api/select-hero":
            if room["phase"] != "lobby":
                raise ApiError(400, "Hero selection only in lobby")
            hero_key = str(body.get("heroKey", ""))
            if hero_key not in HEROES:
                raise ApiError(400, "Unknown hero")
            actor["heroKey"] = hero_key
            actor["activeSkillCards"] = init_active_skill_cards(hero_key)
            actor["hand"] = starting_hand(hero_key, actor)
            actor["discard"] = []
            actor["roundUsed"] = []
            actor["played"] = None
            actor["selectedCardId"] = None
            actor["resolved"] = False
            actor["actionTaken"] = False
            actor["skills"] = {"red": 1, "blue": 1, "green": 1}
            actor["passives"] = []
            actor["bonuses"] = {"damage": 0, "defense": 0, "initiative": 0, "movement": 0, "range": 0, "ranged": 0}
            actor["cardUpgrades"] = {"red": {"initiative": 0, "movement": 0}, "green": {"initiative": 0, "movement": 0}, "blue": {"initiative": 0, "movement": 0}}
            actor["hasUltimate"] = False
            touch(room, f"{actor['name']} 选择 {HEROES[hero_key]['name']}。")
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
            if card_id in actor["roundUsed"] or card_id in actor["discard"]:
                raise ApiError(400, "Card already used this round")
            actor["selectedCardId"] = card_id
            touch(room, f"{actor['name']} 已暗选。")
            if all(p["selectedCardId"] or not playable_hand(p) for p in room["players"]):
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
            if not has_movement_action(played):
                raise ApiError(400, "Card has no movement action")
            blocked = movement_blocked_by_effect(room, actor, {"x": x, "y": y})
            if blocked:
                raise ApiError(400, f"Movement blocked by {blocked}")
            can_phase = bool(played.get("canPhaseThroughWalls"))
            steps = walk_distance(room, actor["pos"], {"x": x, "y": y}, actor["seat"], can_phase)
            if not can_fast_travel(room, actor, target) and (steps is None or steps > (played.get("movement") or 0)):
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
            blocked_action = action_blocked_by_effect(room, actor, played.get("primaryCategory") or played.get("actionFamily"))
            if blocked_action:
                raise ApiError(400, f"Action blocked by {blocked_action}")
            if played["primary"] not in ("attackAny", "attackGeneric", "attackRanged", "attackArea", "skillGeneric", "defenseGeneric", "primaryMove") and not adjacent(actor["pos"], {"x": x, "y": y}):
                raise ApiError(400, "Need adjacent target")
            if played["id"] in ("arien-07-潮水", "arien-09-魔法水流", "arien-11-潮汐之力"):
                target = cell_at(x, y)
                if not target or target.get("obstacle") or occupied(room, x, y, actor["seat"]):
                    raise ApiError(400, "Invalid destination")
                if not in_card_range(actor, played, {"x": x, "y": y}):
                    raise ApiError(400, "Need destination in range")
                if target.get("state", "").endswith("HeroSpawn"):
                    raise ApiError(400, "Destination cannot be a spawn")
                if played["id"] != "arien-11-潮汐之力":
                    for spawn in [c for c in MAP if c.get("state", "").endswith("HeroSpawn")]:
                        if not occupied(room, spawn["x"], spawn["y"]) and adjacent({"x": x, "y": y}, spawn):
                            raise ApiError(400, "Destination cannot be adjacent to an empty spawn")
                actor["pos"] = {"x": x, "y": y}
                actor["actionTaken"] = True
                touch(room, f"{actor['name']} 执行 {played['name']}，放置到 {x},{y}。")
                return public_state(room, token)
            if played["primary"] in ("skillGeneric", "defenseGeneric", "effectGeneric"):
                actor["actionTaken"] = True
                effect_text = apply_text_effect(room, actor, played)
                touch(room, f"{actor['name']} 执行 {played['name']}：{effect_text or played['text']}")
                return public_state(room, token)
            if played["primary"] == "primaryMove":
                target = cell_at(x, y)
                if not target or not in_bounds(x, y) or occupied(room, x, y, actor["seat"]):
                    raise ApiError(400, "Invalid destination")
                blocked = movement_blocked_by_effect(room, actor, {"x": x, "y": y})
                if blocked:
                    raise ApiError(400, f"Movement blocked by {blocked}")
                can_phase = bool(played.get("canPhaseThroughWalls"))
                steps = walk_distance(room, actor["pos"], {"x": x, "y": y}, actor["seat"], can_phase)
                if steps is None or steps > card_range(played):
                    raise ApiError(400, "Too far for card movement")
                actor["pos"] = {"x": x, "y": y}
                actor["actionTaken"] = True
                touch(room, f"{actor['name']} 执行 {played['name']} 移动到 {x},{y}。")
                return public_state(room, token)
            if played["primary"] == "killMinion":
                m = minion_at(room, x, y)
                if not m or m["team"] == actor["team"]:
                    raise ApiError(400, "Need adjacent enemy minion")
                if immune_to(m, "attack"):
                    raise ApiError(400, "Target is immune")
                if m["kind"] == "heavy" and any(mm["team"] == m["team"] and mm["kind"] != "heavy" for mm in room["minions"]):
                    raise ApiError(400, "Heavy minion cannot be killed before other friendly minions")
                removed_team = m["team"]
                room["minions"] = [mm for mm in room["minions"] if mm["id"] != m["id"]]
                gain = 4 if m["kind"] == "heavy" else 2
                actor["coins"] += gain
                actor["actionTaken"] = True
                touch(room, f"{actor['name']} 击杀{team_name(m['team'])}{minion_name(m['kind'])}小兵，获得 {gain} 金。")
                after_minion_removed(room, removed_team)
                return public_state(room, token)
            if played["primary"] in ("attackGeneric", "attackRanged", "attackArea"):
                defender = hero_at(room, x, y)
                target_minion = minion_at(room, x, y)
                target_pos = {"x": x, "y": y}
                if not in_card_range(actor, played, target_pos):
                    raise ApiError(400, "Need target in range")
                if target_minion and target_minion["team"] != actor["team"]:
                    if immune_to(target_minion, "attack"):
                        raise ApiError(400, "Target is immune")
                    if target_minion["kind"] == "heavy" and any(mm["team"] == target_minion["team"] and mm["kind"] != "heavy" for mm in room["minions"]):
                        raise ApiError(400, "Heavy minion cannot be killed before other friendly minions")
                    removed_team = target_minion["team"]
                    room["minions"] = [mm for mm in room["minions"] if mm["id"] != target_minion["id"]]
                    gain = 4 if target_minion["kind"] == "heavy" else 2
                    actor["coins"] += gain
                    actor["actionTaken"] = True
                    touch(room, f"{actor['name']} 使用 {played['name']} 击杀{team_name(target_minion['team'])}{minion_name(target_minion['kind'])}小兵，获得 {gain} 金。")
                    after_minion_removed(room, removed_team)
                    return public_state(room, token)
                if not defender or defender["team"] == actor["team"]:
                    raise ApiError(400, "Need enemy target")
                if immune_to(defender, "attack"):
                    raise ApiError(400, "Target is immune")
                damage, notes = damage_after_minions(room, actor, defender, played.get("attack") or 0)
                room["pendingDefense"] = {"attackerSeat": actor["seat"], "defenderSeat": defender["seat"], "damage": damage, "attackCard": played["id"]}
                room["activeSeat"] = defender["seat"]
                room["phase"] = "defense"
                detail = "；".join(notes) if notes else "无修正"
                touch(room, f"{actor['name']} 使用 {played['name']} 攻击 {defender['name']}：基础{played.get('attack') or 0}，{detail}，最终伤害{damage}。")
                return public_state(room, token)
            if played["primary"] in ("attackHero", "attackAny"):
                defender = hero_at(room, x, y)
                target_minion = minion_at(room, x, y)
                if played["primary"] == "attackHero" and (not defender or defender["team"] == actor["team"]):
                    raise ApiError(400, "Need adjacent enemy hero")
                if played["primary"] == "attackAny" and target_minion and target_minion["team"] != actor["team"]:
                    if immune_to(target_minion, "attack"):
                        raise ApiError(400, "Target is immune")
                    if target_minion["kind"] == "heavy" and any(mm["team"] == target_minion["team"] and mm["kind"] != "heavy" for mm in room["minions"]):
                        raise ApiError(400, "Heavy minion cannot be killed before other friendly minions")
                    removed_team = target_minion["team"]
                    room["minions"] = [mm for mm in room["minions"] if mm["id"] != target_minion["id"]]
                    gain = 4 if target_minion["kind"] == "heavy" else 2
                    actor["coins"] += gain
                    actor["actionTaken"] = True
                    touch(room, f"{actor['name']} 攻击并击杀{team_name(target_minion['team'])}{minion_name(target_minion['kind'])}小兵，获得 {gain} 金。")
                    after_minion_removed(room, removed_team)
                    return public_state(room, token)
                if not defender or defender["team"] == actor["team"]:
                    raise ApiError(400, "Need enemy target")
                if immune_to(defender, "attack"):
                    raise ApiError(400, "Target is immune")
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
                if not defense_card.get("exclamation") and (defense_card.get("defense") or 0) < damage:
                    raise ApiError(400, "Defense too low")
                actor["hand"].remove(card_id)
                actor["discard"].append(card_id)
                touch(room, f"{actor['name']} 弃置 {defense_card['name']} 防御 {defense_card['defense']}，防住伤害 {damage}。")
            else:
                defeat_hero(room, attacker, actor)
            attacker["actionTaken"] = True
            room["pendingDefense"] = None
            if room.get("winner"):
                room["activeSeat"] = None
            else:
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
            card_id = str(body.get("cardId"))
            level, active_text, passive_text = apply_card_upgrade(actor, color, card_id)
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
                removed_team = m["team"]
                room["minions"] = [mm for mm in room["minions"] if mm["id"] != m["id"]]
                after_minion_removed(room, removed_team)
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
