"""Relational entity actor/critic, observation 5/action 2/encoder 4.
PPO and imitation losses are independent of the observation representation.
"""
import torch
from torch import nn
from entities import Encoder, Graph, ENCODER_VERSION, PHASES, ZONES, KINDS


class CandidateNetwork(nn.Module):
    def __init__(self, numeric_dim, category_dim, vocabulary_size, relation_count, hidden=64):
        super().__init__()
        self.shape = dict(numeric_dim=numeric_dim, category_dim=category_dim,
                          vocabulary_size=vocabulary_size, relation_count=relation_count, hidden=hidden)
        self.symbols = nn.Embedding(vocabulary_size, 16, padding_idx=0)
        self.input = nn.Sequential(nn.Linear(numeric_dim + category_dim*16, hidden), nn.SiLU(), nn.LayerNorm(hidden))
        self.relations = nn.Embedding(relation_count, hidden)
        self.messages = nn.ModuleList([nn.Sequential(nn.Linear(hidden*2, hidden), nn.SiLU(), nn.Linear(hidden, hidden)) for _ in range(2)])
        self.norms = nn.ModuleList([nn.LayerNorm(hidden) for _ in range(2)])
        self.actor = nn.ModuleDict(dict(attention=nn.MultiheadAttention(hidden, 4, dropout=0., batch_first=True),
                                       score=nn.Sequential(nn.Linear(hidden*2, hidden), nn.SiLU(), nn.Linear(hidden, 1))))
        self.value_query = nn.Parameter(torch.zeros(1, 1, hidden))
        self.value_attention = nn.MultiheadAttention(hidden, 4, dropout=0., batch_first=True)
        self.critic = nn.Sequential(nn.Linear(hidden, hidden), nn.SiLU(), nn.Linear(hidden, 1))

    def forward(self, state, candidates):
        if not isinstance(state, Graph) or candidates.ndim != 1 or len(candidates) == 0:
            raise ValueError("expected entity graph and candidate node pointers")
        h = self.input(torch.cat((state.numbers, self.symbols(state.categories).flatten(1)), dim=1))
        source, target, relation = state.edges
        r = self.relations(relation)
        # Sum preserves multiplicity of supports/guards. No pooling occurs before
        # the unit->cell, owner->card and source->effect relations are processed.
        for message, norm in zip(self.messages, self.norms):
            m = message(torch.cat((h[source], r), dim=1))
            aggregate = torch.zeros_like(h).index_add(0, target, m)
            degree = torch.zeros(len(h), device=h.device).index_add(0, target, torch.ones(len(target), device=h.device))
            h = norm(h + aggregate / degree.clamp_min(1).sqrt().unsqueeze(1))
        queries = h[candidates].unsqueeze(0)
        attended, _ = self.actor['attention'](queries, h.unsqueeze(0), h.unsqueeze(0), need_weights=False)
        logits = self.actor['score'](torch.cat((queries, attended), dim=-1)).squeeze(0).squeeze(-1)
        pooled, _ = self.value_attention(self.value_query, h.unsqueeze(0), h.unsqueeze(0), need_weights=False)
        value = self.critic(pooled).reshape(())
        if not torch.isfinite(logits).all() or not torch.isfinite(value):
            raise FloatingPointError("nonfinite entity model output")
        return torch.distributions.Categorical(logits=logits), value



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
