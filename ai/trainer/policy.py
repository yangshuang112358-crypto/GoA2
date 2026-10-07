"""Small variable-candidate actor/critic. Only public input enters this module."""
import hashlib
import json
import torch
from torch import nn

ENCODER_VERSION = 1
PHASES = ["HeroSelection", "Deployment", "Planning", "InitiativeChoice", "Action", "RoundEnd", "EffectChoice", "Finished"]
ZONES = ["InHand", "Selected", "PlayedUnresolved", "PlayedResolved", "Discarded"]
KINDS = ["hero", "melee", "ranged", "heavy"]


class Encoder:
    def __init__(self, description):
        self.contract = description["Contract"]
        if self.contract["ObservationVersion"] != 2 or self.contract["ActionVersion"] != 1:
            raise ValueError("unsupported observation/action format")
        self.cards = {c["Id"]: c for c in description["Cards"]}
        self.card_ids = sorted(self.cards)
        self.kinds = description["ActionKinds"]
        self.cells = {(c["Position"]["X"], c["Position"]["Y"]): c for c in description["Cells"]}
        self.signature = hashlib.sha256(json.dumps(description, sort_keys=True).encode()).hexdigest()

    def encode(self, decision):
        o, actions = decision["Observation"], decision["Actions"]
        if o["Schema"] != 2 or o["Rules"] != self.contract["RuleProfile"]:
            raise ValueError("unvalidated rule profile or observation version")
        if o["Phase"] not in PHASES:
            raise ValueError("unknown phase")
        if not actions or len({a["Id"] for a in actions}) != len(actions):
            raise ValueError("empty/duplicate candidates")
        own = next(p for p in o["Players"] if p["Seat"] == o["Seat"])
        team = own["Team"]
        friendly = "Blue" if team == "Blue" else "Red"
        enemy = "Red" if team == "Blue" else "Blue"
        rules = o["Rules"]
        life, marks = rules["StartingCrystalLife"], rules["VictoryMarksRequired"]
        f = [float(o["Phase"] == p) for p in PHASES]
        f += [o["Round"] / 20, o["Turn"] / rules["TurnsPerRound"], life / 10, marks / 5,
              o[friendly + "Crystal"] / life, o[enemy + "Crystal"] / life,
              o[friendly + "Marks"] / marks, o[enemy + "Marks"] / marks,
              o[friendly + "Crystal"] / 10, o[enemy + "Crystal"] / 10,
              o[friendly + "Marks"] / 5, o[enemy + "Marks"] / 5,
              float(o["Coin"] == team), rules["HandSize"] / 5]
        for same in (True, False):
            ps = [p for p in o["Players"] if (p["Team"] == team) == same]
            f += [sum(p[key] for p in ps) / scale for key, scale in
                  [("Level", 16), ("Gold", 40), ("HandCount", 10), ("AwaitingRespawn", 2), ("Poisoned", 2), ("Petrified", 2)]]
            for kind in KINDS:
                us = [u for u in o["Units"] if (u["Team"] == team) == same and u["Kind"] == kind]
                f += [len(us) / 10, sum(u["Position"]["X"] for u in us) / max(1, len(us)) / 20,
                      sum(u["Position"]["Y"] for u in us) / max(1, len(us)) / 20]
        unknown = {u["Kind"] for u in o["Units"]} - set(KINDS)
        if unknown:
            raise ValueError(f"unsupported unit kinds: {unknown}")
        self_unit = next((u for u in o["Units"] if u["Seat"] == o["Seat"]), None)
        pos = self_unit["Position"] if self_unit else {"X": 0, "Y": 0}
        f += [float(self_unit is not None), pos["X"] / 20, pos["Y"] / 20, own["Level"] / 8, own["Gold"] / 20]
        own_cards = {c["Id"]: c["Zone"] for c in o["OwnCards"]}
        if set(own_cards) - set(self.cards) or set(own_cards.values()) - set(ZONES):
            raise ValueError("unknown card or zone")
        f += [float(own_cards.get(c) == z) for c in self.card_ids for z in ZONES]
        rows = []
        units = {u["Id"]: u for u in o["Units"]}
        for a in actions:
            if a["Kind"] not in self.kinds:
                raise ValueError("unknown action kind")
            # Literal stable value bytes distinguish branch IDs; no candidate index is used as a feature.
            raw = a["Value"].encode("utf-8")
            if len(raw) > 64:
                raise ValueError("action_value_capacity_exceeded: extend encoder version")
            row = [float(a["Kind"] == k) for k in self.kinds]
            row += [float(a["Value"] == c) for c in self.card_ids]
            row += [v / 255 for v in raw] + [0.] * (64 - len(raw))
            target = units.get(a["Value"])
            dst = a["Destination"]
            cell = self.cells.get((dst["X"], dst["Y"])) if a["HasDestination"] else None
            card = self.cards.get(a["Value"], {})
            row += [float(a["SuccessfulDefense"]), float(a["ImmediateSkip"]), float(a["HasDestination"]),
                    dst["X"] / 20 if a["HasDestination"] else 0., dst["Y"] / 20 if a["HasDestination"] else 0.,
                    float(a["Mode"] == "Fast"), float(a["Value"] == "skip"),
                    float(target is not None and target["Team"] == team), float(target is not None),
                    float(cell is not None and cell["Region"] == o["CombatRegion"]),
                    (card.get("PrimaryValue") or 0) / 10, (card.get("SecondaryDefense") or 0) / 10,
                    (card.get("SecondaryMovement") or 0) / 10, (card.get("Initiative") or 0) / 10]
            row += [float(target is not None and target["Kind"] == k) for k in KINDS]
            row += [(target["Position"]["X"] - pos["X"]) / 20 if target else 0.,
                    (target["Position"]["Y"] - pos["Y"]) / 20 if target else 0.,
                    float(a["TargetSeat"] == o["Seat"]),
                    float(a["TargetSeat"] >= 0 and any(p["Seat"] == a["TargetSeat"] and p["Team"] == team for p in o["Players"]))]
            rows.append(row)
        state, candidates = torch.tensor(f, dtype=torch.float32), torch.tensor(rows, dtype=torch.float32)
        if not torch.isfinite(state).all() or not torch.isfinite(candidates).all():
            raise ValueError("nonfinite features")
        return state, candidates


class CandidateNetwork(nn.Module):
    def __init__(self, state_dim, action_dim, hidden=64):
        super().__init__()
        self.shape = dict(state_dim=state_dim, action_dim=action_dim, hidden=hidden)
        self.actor = nn.Sequential(nn.Linear(state_dim + action_dim, hidden), nn.Tanh(), nn.Linear(hidden, 1))
        self.critic = nn.Sequential(nn.Linear(state_dim, hidden), nn.Tanh(), nn.Linear(hidden, 1))

    def forward(self, state, candidates):
        # There are no padded/illegal actions. All and only legal candidates enter the distribution.
        logits = self.actor(torch.cat((state.expand(len(candidates), -1), candidates), dim=1)).squeeze(-1)
        return torch.distributions.Categorical(logits=logits), self.critic(state).squeeze(-1)


def advantages(rewards, values, next_values, boundaries, gamma=.99, lam=.95):
    """next_values is zero ONLY on true termination; truncations retain their bootstrap."""
    result = [0.] * len(rewards)
    carry = 0.
    for i in range(len(rewards) - 1, -1, -1):
        delta = rewards[i] + gamma * next_values[i] - values[i]
        carry = delta + gamma * lam * (0. if boundaries[i] else carry)
        result[i] = carry
    return result


def update(model, optimizer, records, device, epochs=3, batch_size=32):
    adv = torch.tensor(advantages([r[4] for r in records], [r[5] for r in records],
                                 [r[6] for r in records], [r[7] for r in records]), device=device)
    returns = adv + torch.tensor([r[5] for r in records], device=device)
    adv = (adv - adv.mean()) / (adv.std(unbiased=False) + 1e-8)
    metrics = []
    for _ in range(epochs):
        for indexes in torch.randperm(len(records)).split(batch_size):
            new_logs, entropy, value = [], [], []
            for i in indexes.tolist():
                s, a, chosen = records[i][:3]
                dist, v = model(s.to(device), a.to(device))
                new_logs.append(dist.log_prob(torch.tensor(chosen, device=device)))
                entropy.append(dist.entropy()); value.append(v)
            ids = indexes.to(device)
            logs = torch.stack(new_logs)
            old = torch.tensor([records[i][3] for i in indexes.tolist()], device=device)
            log_ratio = logs - old
            ratio = log_ratio.exp()
            kl = ((ratio - 1) - log_ratio).mean()
            if kl.detach().item() > .03:
                return metrics  # Conservative early stop, not an environment failure.
            policy_loss = -torch.minimum(ratio * adv[ids], ratio.clamp(.8, 1.2) * adv[ids]).mean()
            value_loss = .5 * (torch.stack(value) - returns[ids]).square().mean()
            loss = policy_loss + value_loss - .01 * torch.stack(entropy).mean()
            if not torch.isfinite(loss):
                raise FloatingPointError("nonfinite PPO loss")
            optimizer.zero_grad(); loss.backward()
            norm = nn.utils.clip_grad_norm_(model.parameters(), .5, error_if_nonfinite=True)
            optimizer.step()
            metrics.append(dict(loss=loss.item(), policy_loss=policy_loss.item(), value_loss=value_loss.item(), kl=kl.item(), grad_norm=norm.item()))
    return metrics
