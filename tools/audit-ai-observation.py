"""Characterize encoder-v2 information loss; this is NOT a completeness acceptance test.

Synthetic field changes keep the action list fixed to isolate encoding. They are
not submitted as real game states. No weights are updated or authority read.
"""
import argparse
import copy
import gzip
import json
from pathlib import Path
import subprocess
import sys
import torch
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "ai/trainer"))
from policy import CandidateNetwork, Encoder
from train import sha, write


def probe(encoder, decision):
    own = decision["Observation"]["Seat"]
    players = decision["Observation"]["Players"]
    team = next(p["Team"] for p in players if p["Seat"] == own)
    enemy_indexes = [i for i, p in enumerate(players) if p["Team"] != team]
    enemy = enemy_indexes[0]
    def purple_for(player):
        return next(c["Id"] for c in encoder.cards.values() if c["PrimaryFamily"] == "ultimate" and c["HeroId"] == player["Hero"])
    base_state, base_actions = encoder.encode(decision)
    results = []

    def case(name, field, change, expected):
        d = copy.deepcopy(decision)
        change(d["Observation"])
        if d == decision:
            raise ValueError("probe did not change input: " + name)
        s, a = encoder.encode(d)
        changed = not (torch.equal(s, base_state) and torch.equal(a, base_actions))
        results.append(dict(name=name, field=field, state_changed=not torch.equal(s, base_state),
                            candidate_features_changed=not torch.equal(a, base_actions),
                            encoded=changed, expected_for_v2=expected))
        if changed != expected:
            raise ValueError("encoder behavior changed; review audit expectation: " + name)

    def toggle_zone(card):
        card["Zone"] = "Discarded" if card["Zone"] != "Discarded" else "InHand"

    case("other_card_zone", "Players[].Cards[].Zone", lambda o: toggle_zone(o["Players"][enemy]["Cards"][0]), True)
    case("own_card_zone", "OwnCards[].Zone", lambda o: toggle_zone(o["OwnCards"][0]), True)
    case("revealed_current_card", "CurrentCard", lambda o: o.update(CurrentCard=next(c for c in encoder.card_ids if c != o["CurrentCard"])), True)
    case("other_purple", "Players[].Purple", lambda o: o["Players"][enemy].update(Purple=purple_for(o["Players"][enemy])), False)
    case("own_purple", "Players[].Purple", lambda o: next(p for p in o["Players"] if p["Seat"] == own).update(Purple=purple_for(next(p for p in o["Players"] if p["Seat"] == own))), False)
    case("poison_defense_scope", "Players[].PoisonDefense", lambda o: o["Players"][enemy].update(PoisonDefense=not o["Players"][enemy]["PoisonDefense"]), False)
    case("card_play_time", "Players[].Cards[].PlayedRound/PlayedTurn", lambda o: o["Players"][enemy]["Cards"][0].update(PlayedRound=o["Round"] + 1, PlayedTurn=1), False)
    case("public_history", "PublicHistory", lambda o: o["PublicHistory"].append(dict(Kind="CardRevealed", Card=encoder.card_ids[0], Seat=players[enemy]["Seat"], From=None, To=None)), False)
    case("persistent_effect", "Effects", lambda o: o["Effects"].append(dict(Kind="MovementBoundary", Card=encoder.card_ids[0], SourceUnit="hero:" + str(own), ProtectedUnit="", Controller=own, StartRound=o["Round"], StartTurn=o["Turn"], EndRound=o["Round"], EndTurn=o["Rules"]["TurnsPerRound"])), False)
    case("active_seat", "ActiveSeat", lambda o: o.update(ActiveSeat=(own + 1) % 4 if o["ActiveSeat"] != (own + 1) % 4 else own), False)
    case("response_kind", "Decision", lambda o: o.update(Decision="effect_move" if o["Decision"] != "effect_move" else "placement"), False)

    def swap_enemy_positions(o):
        heroes = [u for u in o["Units"] if u["Kind"] == "hero" and u["Team"] != team]
        if len(heroes) != 2:
            raise ValueError("probe requires two deployed enemy heroes")
        heroes[0]["Position"], heroes[1]["Position"] = heroes[1]["Position"], heroes[0]["Position"]
    case("enemy_identity_position_association", "Units[].Seat/Position association", swap_enemy_positions, False)
    model = CandidateNetwork(len(base_state), base_actions.shape[1])
    return dict(complete=False, meaning="Known-gap characterization only; unchanged means exactly identical full state and candidate tensors with actions held fixed.",
                observation_version=3, encoder_version=2, model_shape=model.shape,
                actor_parameters=sum(p.numel() for p in model.actor.parameters()),
                critic_parameters=sum(p.numel() for p in model.critic.parameters()), probes=results)


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1])
    p.add_argument("--output", type=Path, required=True)
    args = p.parse_args()
    torch.set_num_threads(2)
    args.output.mkdir(parents=True, exist_ok=False)
    catalog = args.root / "docs/verification/ai-stage4-20261007/defense-finetune-01/public-catalog.json"
    source = args.root / "docs/verification/ai-stage3-20261007/teaching-v3-01/policy.jsonl.gz"
    enc = Encoder(json.loads(catalog.read_text(encoding="utf-8")))
    rows = (json.loads(x) for x in gzip.decompress(source.read_bytes()).decode("utf-8").splitlines())
    # A real public planning input; avoid target actions when testing lost enemy identity/position binding.
    row = next(r for r in rows if r["Decision"]["Observation"]["Decision"] == "Planning"
               and len([u for u in r["Decision"]["Observation"]["Units"] if u["Kind"] == "hero"]) == 4
               and all(not a["HasDestination"] and a["Kind"] == "SelectCard" for a in r["Decision"]["Actions"])
               and all(p["Purple"] == "" for p in r["Decision"]["Observation"]["Players"]))
    report = probe(enc, row["Decision"])
    report.update(source_commit=subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=args.root, text=True).strip(),
                  script_sha256=sha(Path(__file__)), policy_sha256=sha(args.root / "ai/trainer/policy.py"),
                  catalog_sha256=sha(catalog), data_sha256=sha(source), source_group=row["Group"],
                  boundary="Public observation only; no checkpoint loading, rule mutation, training or real-game strength claim.")
    write(args.output / "input.json", row["Decision"])
    write(args.output / "report.json", report)
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
