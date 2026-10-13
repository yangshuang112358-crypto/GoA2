"""Episode-owned recurrent PPO. Entire causal prefixes are rebuilt at current weights.

Only chunks are shuffled, never time within a chunk. Prefix replay is no-grad
burn-in; gradients span sequence_length decisions (truncated BPTT). Keeping full
prefixes avoids stale rollout hidden states after optimizer steps. This costs
extra compute and is deliberately a correctness-first short-run implementation.
"""
from dataclasses import dataclass
import torch
from torch import nn


@dataclass
class Episode:
    key: str
    seat: int
    records: list


def sample_count(records, recurrent):
    return sum(len(e.records) for e in records) if recurrent else len(records)


def validate_episodes(episodes):
    keys = set()
    if not episodes:
        raise ValueError('empty recurrent rollout')
    for episode in episodes:
        if not isinstance(episode, Episode):
            raise ValueError('recurrent PPO requires complete ordered Episode objects')
        if (episode.key, episode.seat) in keys or not 0 <= episode.seat < 4:
            raise ValueError('duplicate episode/seat or invalid seat')
        keys.add((episode.key, episode.seat))
        if not episode.records or not episode.records[-1][7] or any(r[7] for r in episode.records[:-1]):
            raise ValueError('episode must have exactly one final boundary')


def replay_chunk(model, episode, start, end, device, guard=None):
    if not 0 <= start < end <= len(episode.records):
        raise ValueError('invalid sequence bounds')
    memory = model.initial_memory(device)
    with torch.no_grad():
        for i in range(start):
            if guard and i % 16 == 0:
                guard()
            state, actions = episode.records[i][:2]
            _, _, memory = model.step(state.to(device), actions.to(device), memory)
    if memory is not None:
        memory = memory.detach()
    logs, entropies, values = [], [], []
    for i in range(start, end):
        state, actions, chosen = episode.records[i][:3]
        dist, value, memory = model.step(state.to(device), actions.to(device), memory)
        logs.append(dist.log_prob(torch.tensor(chosen, device=device)))
        entropies.append(dist.entropy())
        values.append(value)
    return torch.stack(logs), torch.stack(entropies), torch.stack(values)


def update_recurrent(model, optimizer, episodes, device, epochs=3, batch_size=32, sequence_length=16, guard=None):
    from policy import advantages
    validate_episodes(episodes)
    if not model.recurrent or epochs < 1 or not 1 <= sequence_length <= batch_size:
        raise ValueError('invalid recurrent update configuration')
    advs, returns, offsets, chunks = [], [], [], []
    for ei, episode in enumerate(episodes):
        rows = episode.records
        offsets.append(len(advs))
        adv = advantages([r[4] for r in rows], [r[5] for r in rows], [r[6] for r in rows], [r[7] for r in rows])
        advs.extend(adv)
        returns.extend(a + r[5] for a, r in zip(adv, rows))
        chunks.extend((ei, start, min(start+sequence_length, len(rows))) for start in range(0, len(rows), sequence_length))
    advs = torch.tensor(advs, device=device)
    returns = torch.tensor(returns, device=device)
    advs = (advs-advs.mean())/(advs.std(unbiased=False)+1e-8)
    metrics = []
    for _ in range(epochs):
        for indexes in torch.randperm(len(chunks)).split(max(1, batch_size//sequence_length)):
            selected = [chunks[i] for i in indexes.tolist()]
            total = sum(end-start for _, start, end in selected)
            optimizer.zero_grad(set_to_none=True)
            sums = dict(loss=0., policy_loss=0., value_loss=0., kl=0.)
            prefix_steps = 0
            for ei, start, end in selected:
                if guard:
                    guard()
                logs, entropy, values = replay_chunk(model, episodes[ei], start, end, device, guard)
                rows = episodes[ei].records[start:end]
                old = torch.tensor([r[3] for r in rows], device=device)
                log_ratio = logs-old
                ratio = log_ratio.exp()
                kl = ((ratio-1)-log_ratio).mean()
                if not torch.isfinite(kl):
                    raise FloatingPointError('nonfinite recurrent PPO ratio')
                if kl.detach().item() > .03:
                    optimizer.zero_grad(set_to_none=True)
                    return metrics
                ids = slice(offsets[ei]+start, offsets[ei]+end)
                policy_loss = -torch.minimum(ratio*advs[ids], ratio.clamp(.8, 1.2)*advs[ids]).mean()
                value_loss = .5*(values-returns[ids]).square().mean()
                loss = policy_loss+value_loss-.01*entropy.mean()
                if not torch.isfinite(loss):
                    raise FloatingPointError('nonfinite recurrent PPO loss')
                # Accumulate gradients, releasing each chunk's graph before the next.
                weight = (end-start)/total
                (loss*weight).backward()
                for name, v in [('loss', loss), ('policy_loss', policy_loss), ('value_loss', value_loss), ('kl', kl)]:
                    sums[name] += v.detach().item()*weight
                prefix_steps += start
            norm = nn.utils.clip_grad_norm_(model.parameters(), .5, error_if_nonfinite=True)
            optimizer.step()
            metrics.append(dict(**sums, grad_norm=norm.item(), samples=total,
                                chunks=len(selected), prefix_replay_steps=prefix_steps))
    return metrics
