import copy
import json
from pathlib import Path
import sys
import time

ROOT = Path(__file__).resolve().parents[3]
OUT = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT/'ai/trainer'))
import torch
from entities import Encoder
from resources import require_interactive_memory
from train import load_checkpoint, sha, write

torch.set_num_threads(2)
torch.use_deterministic_algorithms(True)
source = ROOT/'ai/Goa2.Ai.Tests/bin/Release/net10.0/response-v5.jsonl'
responses = [json.loads(line) for line in source.read_text(encoding='utf-8-sig').splitlines()]
(OUT/'response-inputs.jsonl').write_bytes(source.read_bytes())
description = json.loads((OUT/'entity-gru/public-catalog.json').read_text(encoding='utf-8'))
encoder = Encoder(description)
checks = []
for name in ('graph', 'entity', 'entity-gru'):
    require_interactive_memory()
    path = OUT/name/'checkpoint-001.pt'
    model, _ = load_checkpoint(path, encoder, 'cpu', name)
    started = time.perf_counter(); candidate_count = 0
    for row in responses:
        decision = row['Decision']; state, actions = encoder.encode(decision)
        with torch.no_grad():
            d, v, m = model.step(state, actions, model.initial_memory())
            permuted = copy.deepcopy(decision); permuted['Actions'].reverse()
            p, w, n = model.step(*encoder.encode(permuted), model.initial_memory())
        assert len(d.probs) == len(decision['Actions'])
        assert torch.isfinite(v) and torch.isfinite(d.logits).all()
        assert torch.allclose(d.logits, p.logits.flip(0), atol=1e-6)
        assert torch.allclose(v, w, atol=1e-6)
        if m is not None: assert torch.allclose(m, n, atol=1e-6)
        candidate_count += len(actions)
    checks.append(dict(architecture=name, windows=len(responses), candidates=candidate_count,
                       seconds=time.perf_counter()-started, checkpoint_sha256=sha(path)))
variant = Encoder(json.loads((OUT/'cpu-gru-variant/public-catalog.json').read_text(encoding='utf-8')))
rejections = []
for path, enc in [(OUT/'entity-gru/checkpoint-001.pt', variant), (OUT/'cpu-gru-variant/checkpoint-001.pt', encoder)]:
    try:
        load_checkpoint(path, enc, 'cpu')
    except ValueError as error:
        assert 'incompatible' in str(error)
        rejections.append(dict(checkpoint=str(path.relative_to(OUT)), error=str(error)))
    else:
        raise AssertionError('wrong rule profile accepted')
write(OUT/'model-checks.json', dict(checks=checks, cross_profile_rejections=rejections,
      response_inputs_sha256=sha(source), type='independent response-window forward checks, not complete games',
      recurrent_memory='zero for each independent window; causal memory separately tested in test_sequences'))
print(json.dumps(checks, indent=2))
