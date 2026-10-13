"""Relational entity actor/critic, observation 5/action 2/encoder 4.
PPO and imitation losses are independent of the observation representation.
"""
import torch
from torch import nn
from entities import Encoder, Graph, ENCODER_VERSION, PHASES, ZONES, KINDS

MODEL_VERSION = 2
ARCHITECTURES = ('graph', 'entity', 'entity-gru')


class LatentBlock(nn.Module):
    """Global interactions among a bounded number of slots, never N x N nodes."""
    def __init__(self, hidden):
        super().__init__()
        self.attention = nn.MultiheadAttention(hidden, 4, dropout=0., batch_first=True)
        self.norm1 = nn.LayerNorm(hidden)
        self.norm2 = nn.LayerNorm(hidden)
        self.ff = nn.Sequential(nn.Linear(hidden, hidden*2), nn.SiLU(), nn.Linear(hidden*2, hidden))

    def forward(self, x):
        q = self.norm1(x)
        x = x + self.attention(q, q, q, need_weights=False)[0]
        return x + self.ff(self.norm2(x))


class CandidateNetwork(nn.Module):
    def __init__(self, numeric_dim, category_dim, vocabulary_size, relation_count, hidden=64,
                 architecture='graph', latent_count=32, memory_dim=128):
        super().__init__()
        if architecture not in ARCHITECTURES or hidden < 8 or hidden % 4 or not 1 <= latent_count <= 128 or not 4 <= memory_dim <= 512:
            raise ValueError('invalid model architecture/capacity')
        self.architecture = architecture
        self.recurrent = architecture == 'entity-gru'
        self.shape = dict(numeric_dim=numeric_dim, category_dim=category_dim,
                          vocabulary_size=vocabulary_size, relation_count=relation_count, hidden=hidden,
                          architecture=architecture, latent_count=latent_count, memory_dim=memory_dim)
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
        if architecture != 'graph':
            self.latents = nn.Parameter(torch.randn(1, latent_count, hidden) * .02)
            self.read = nn.MultiheadAttention(hidden, 4, dropout=0., batch_first=True)
            self.read_norm = nn.LayerNorm(hidden)
            self.global_blocks = nn.Sequential(LatentBlock(hidden), LatentBlock(hidden))
        if self.recurrent:
            self.memory = nn.GRUCell(hidden, memory_dim)
            self.memory_actor = nn.Linear(memory_dim, hidden)
            self.memory_value = nn.Linear(memory_dim, hidden)

    def encode_nodes(self, state, candidates):
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
        return h

    def initial_memory(self, device=None):
        if not self.recurrent:
            return None
        return torch.zeros(1, self.shape['memory_dim'], device=device or next(self.parameters()).device)

    def step(self, state, candidates, memory=None):
        """Functional transition: caller commits returned memory once, after action acceptance.

        Bootstrap probes may call step without changing the caller's memory. No hidden
        state is stored on the shared module, so seats/matches cannot share it implicitly.
        """
        h = self.encode_nodes(state, candidates)
        context = h.unsqueeze(0)
        if self.architecture != 'graph':
            # Every entity is read; no top-k entities or legal actions are discarded.
            context = self.read_norm(self.latents + self.read(self.latents, context, context, need_weights=False)[0])
            context = self.global_blocks(context)
        queries = h[candidates].unsqueeze(0)
        pooled, _ = self.value_attention(self.value_query, context, context, need_weights=False)
        next_memory = None
        if self.recurrent:
            if memory is None or memory.shape != (1, self.shape['memory_dim']) or memory.device != h.device or not torch.isfinite(memory).all():
                raise ValueError('explicit valid seat memory required')
            next_memory = self.memory(pooled.squeeze(1), memory)
            queries = queries + self.memory_actor(next_memory).unsqueeze(1)
            pooled = pooled + self.memory_value(next_memory).unsqueeze(1)
        elif memory is not None:
            raise ValueError('memory supplied to feedforward model')
        attended, _ = self.actor['attention'](queries, context, context, need_weights=False)
        # Direct candidate/target relation path bypasses the global bottleneck.
        logits = self.actor['score'](torch.cat((h[candidates].unsqueeze(0), attended), dim=-1)).squeeze(0).squeeze(-1)
        value = self.critic(pooled).reshape(())
        if not torch.isfinite(logits).all() or not torch.isfinite(value):
            raise FloatingPointError("nonfinite entity model output")
        return torch.distributions.Categorical(logits=logits), value, next_memory

    def forward(self, state, candidates):
        if self.recurrent:
            raise ValueError('recurrent model requires explicit step/sequence training; no implicit memory reset')
        dist, value, _ = self.step(state, candidates)
        return dist, value



def advantages(rewards, values, next_values, boundaries, gamma=.99, lam=.95):
    """next_values is zero ONLY on true termination; truncations retain their bootstrap."""
    result = [0.] * len(rewards)
    carry = 0.
    for i in range(len(rewards) - 1, -1, -1):
        delta = rewards[i] + gamma * next_values[i] - values[i]
        carry = delta + gamma * lam * (0. if boundaries[i] else carry)
        result[i] = carry
    return result


def update(model, optimizer, records, device, epochs=3, batch_size=32, sequence_length=16, guard=None):
    if model.recurrent:
        from sequences import update_recurrent
        return update_recurrent(model, optimizer, records, device, epochs, batch_size, sequence_length, guard)
    if not records:
        raise ValueError('empty rollout')
    adv = torch.tensor(advantages([r[4] for r in records], [r[5] for r in records],
                                 [r[6] for r in records], [r[7] for r in records]), device=device)
    returns = adv + torch.tensor([r[5] for r in records], device=device)
    adv = (adv - adv.mean()) / (adv.std(unbiased=False) + 1e-8)
    metrics = []
    for _ in range(epochs):
        for indexes in torch.randperm(len(records)).split(batch_size):
            if guard:
                guard()
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
