"""Bounded data/model validation. Baseline traces are shadow-inferred, never relabeled as model games."""
import argparse
import gzip
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import time
import traceback

ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/'ai/trainer'))
import torch
from bridge import Bridge
from entities import Encoder, SPEC, RELATIONS, scale
from policy import CandidateNetwork
from resources import ResourceMonitor, require_interactive_memory
from train import checkpoint, load_checkpoint, write, sha


def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('--output',type=Path,required=True)
    parser.add_argument('--responses',type=Path,default=ROOT/'ai/Goa2.Ai.Tests/bin/Release/net10.0/response-v4.jsonl')
    parser.add_argument('--baseline',type=Path)
    args=parser.parse_args();args.output.mkdir(parents=True,exist_ok=False)
    torch.set_num_threads(2);torch.set_num_interop_threads(1);torch.use_deterministic_algorithms(True);torch.manual_seed(61008)
    monitor=ResourceMonitor();started=time.perf_counter();current=None
    config=dict(source_commit=subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip(),
        source_status=subprocess.check_output(['git','status','--porcelain'],cwd=ROOT,text=True),seed=61008,threads=2,
        python_files={p.name:sha(p) for p in (ROOT/'ai/trainer').glob('*.py')},symbols_sha256=sha(ROOT/'ai/trainer/public-symbols-v1.json'),
        purpose='input/forward/backward/checkpoint validation; baseline games are shadow inference only',
        optimizer_updates=2,opponent_pool=['simple-v1','random-v1'],model_games=0)
    write(args.output/'manifest.json',config)
    try:
        require_interactive_memory()
        with Bridge(ROOT,Path(os.environ['LOCALAPPDATA'])/'Goa2V1Toolchain/dotnet/dotnet.exe',args.output/'probe-host') as host:
            description=host.call(op='describe');write(args.output/'public-catalog.json',description)
            probe=host.call(op='reset',seed=19,learner=0,swap=0,limit=1)
            assert probe['Truncated'] and not probe['Terminated']
            write(args.output/'sample-input.json',probe['Decision'])
        enc=Encoder(description);model=CandidateNetwork(**enc.model_kwargs)
        opt=torch.optim.Adam(model.parameters(),lr=.001)
        checkpoint(args.output/'initial.pt',model,opt,enc,config,0,0,[])
        rows=[json.loads(l) for l in args.responses.read_text(encoding='utf-8-sig').splitlines()]
        (args.output/'response-inputs.jsonl').write_text(args.responses.read_text(encoding='utf-8-sig'),encoding='utf-8')
        timings=[];encode_times=[];sizes=[];contrasts=[];window_counts={}
        def check(decision, label):
            nonlocal current
            current=decision
            t=time.perf_counter();s,a=enc.encode(decision);encode_times.append(time.perf_counter()-t)
            t=time.perf_counter()
            with torch.no_grad(): dist,value=model(s,a)
            timings.append(time.perf_counter()-t);sizes.append([len(s.numbers),s.edges.shape[1],len(a)])
            assert len(dist.probs)==len(decision['Actions']) and torch.isfinite(dist.probs).all()
            assert abs(dist.probs.sum().item()-1)<1e-5
            window_counts[label]=window_counts.get(label,0)+1
            return s,a
        for r in rows:
            s,a=check(r['Decision'],'response/'+r['Window'])
            good=torch.tensor([a['Kind']=='Defend' and a['SuccessfulDefense'] for a in r['Decision']['Actions']])
            if good.any() and not good.all():contrasts.append((s,a,good))
        baseline_decisions=0
        if args.baseline:
            write(args.output/'baseline-summary.json',json.loads((args.baseline/'summary.json').read_text(encoding='utf-8-sig')))
            for file in sorted(args.baseline.glob('ai-*/policy-trace.jsonl.gz')):
                for line in gzip.decompress(file.read_bytes()).decode('utf-8-sig').splitlines():
                    r=json.loads(line);d={k:r[k] for k in ('Revision','Observation','Actions')};check(d,'shadow/'+d['Observation']['Decision']);baseline_decisions+=1
                require_interactive_memory()
        if not contrasts:raise ValueError('no real defense contrast to validate learning')
        def loss():
            return torch.stack([-torch.logsumexp(model(s,a)[0].logits[good],dim=0) for s,a,good in contrasts]).mean()
        before=loss().item();previous={k:v.detach().clone() for k,v in model.state_dict().items()}
        for _ in range(2):
            opt.zero_grad();objective=loss();objective.backward();torch.nn.utils.clip_grad_norm_(model.parameters(),1.,error_if_nonfinite=True);opt.step()
        after=loss().item();delta=sum((v-previous[k]).abs().sum().item() for k,v in model.state_dict().items())
        assert delta>0
        checkpoint(args.output/'smoke.pt',model,opt,enc,config,0,1,[dict(loss_before=before,loss_after=after)])
        restored,_=load_checkpoint(args.output/'smoke.pt',enc,'cpu');s,a,_=contrasts[0]
        with torch.no_grad():
            p,v=model(s,a);q,w=restored(s,a)
            assert torch.equal(p.logits,q.logits) and torch.equal(v,w)
        dictionary={kind:dict(numeric=[dict(slot=i,field=k,scale=scale(k),presence_slot=len(s.numbers[0])//2+i,
             boolean_encoding='0/1',missing='value 0, presence 0; a real zero has presence 1') for i,k in enumerate(nums)],
             categorical=[dict(slot=i+1,field=k,domain=domain) for i,(k,domain) in enumerate(cats.items())]) for kind,(nums,cats) in SPEC.items()}
        write(args.output/'input-dictionary.json',dict(records=dictionary,vocabulary=enc.vocab,relations=RELATIONS,model_shape=model.shape,
             excluded_from_features=['Revision','command/candidate IDs','entity primary/foreign keys as ordinal scalars','rule-profile Id'],
             representation='entity keys resolve typed graph edges; semantic card IDs use categorical embeddings'))
        def percentiles(xs):
            v=torch.tensor(xs);return dict(p50=float(v.quantile(.5)),p95=float(v.quantile(.95)),maximum=max(xs))
        report=dict(complete_information=False,known_gaps=json.loads((ROOT/'ai/observation-coverage.json').read_text())['known_gaps'],
            response_windows=len(rows),response_candidates=sum(len(r['Decision']['Actions']) for r in rows),baseline_shadow_decisions=baseline_decisions,
            decision_kinds=window_counts,encoder_signature=enc.signature,model_shape=model.shape,parameter_count=sum(p.numel() for p in model.parameters()),
            max_nodes=max(n[0] for n in sizes),max_directed_edges=max(n[1] for n in sizes),max_candidates=max(n[2] for n in sizes),
            encode_seconds=percentiles(encode_times),forward_seconds=percentiles(timings),
            two_update_smoke=dict(real_defense_windows=len(contrasts),loss_before=before,loss_after=after,parameter_l1_change=delta,checkpoint_exact=True),
            total_seconds=time.perf_counter()-started,conclusion='Entity input/model plumbing passed; no model strength result and no complete-information claim.')
        write(args.output/'report.json',report);print(json.dumps(report,ensure_ascii=True),flush=True)
    except Exception:
        write(args.output/'failure.json',dict(error=traceback.format_exc(),input=current));raise
    finally:write(args.output/'resources.json',monitor.report())


if __name__=='__main__':main()
