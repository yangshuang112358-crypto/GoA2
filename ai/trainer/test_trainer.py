import copy
import json
import os
from pathlib import Path
import tempfile
import unittest
import torch
from bridge import Bridge
from policy import CandidateNetwork, Encoder, Graph, advantages, update
from train import checkpoint, load_checkpoint
from curriculum import examples, imitation_loss
from defense import defense_ranking_loss, load_data, teaching_family, fine_tune_batch

ROOT = Path(__file__).resolve().parents[2]
DOTNET = Path(os.environ["LOCALAPPDATA"]) / "Goa2V1Toolchain/dotnet/dotnet.exe"

def bandit():
    return Graph(torch.eye(2),torch.tensor([[1],[2]]),torch.empty((3,0),dtype=torch.long)),torch.arange(2)

def equal_graph(a,b):
    return all(torch.equal(getattr(a,k),getattr(b,k)) for k in ('numbers','categories','edges'))


class TrainerTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        torch.set_num_threads(2)
        torch.use_deterministic_algorithms(True)
        cls.temp = tempfile.TemporaryDirectory()
        with Bridge(ROOT, DOTNET, Path(cls.temp.name) / "probe") as b:
            cls.description = b.call(op="describe")
            cls.reply = b.call(op="reset", seed=19, learner=0, swap=0, limit=1)
            planning = b.call(op="reset", seed=19, learner=0, swap=0, limit=50)
            while planning['Decision']['Observation']['Phase']!='Planning':
                d=planning['Decision'];planning=b.call(op='step',revision=d['Revision'],action=d['Actions'][0]['Id'])
            cls.planning=planning['Decision']

    @classmethod
    def tearDownClass(cls):
        cls.temp.cleanup()

    def test_truncation_bootstraps_and_does_not_cross_reset(self):
        self.assertTrue(self.reply["Truncated"])
        self.assertFalse(self.reply["Terminated"])
        self.assertEqual(self.reply["Reward"], 0)
        self.assertEqual(self.reply["Decision"]["Observation"]["Seat"], 0)
        got = advantages([0., 1.], [.5, .2], [.8, 0.], [True, True], gamma=1)
        self.assertAlmostEqual(got[0], .3); self.assertAlmostEqual(got[1], .8)

    def test_defense_contrast_gradient_and_permutation(self):
        logits = torch.tensor([2., -1., 1.], requires_grad=True)
        good = torch.tensor([False, True, False])
        loss = defense_ranking_loss(logits, good)
        self.assertTrue(torch.equal(loss, defense_ranking_loss(logits.flip(0), good.flip(0))))
        loss.backward()
        self.assertLess(logits.grad[1].item(), 0.)
        self.assertTrue((logits.grad[~good] > 0).all())
        with self.assertRaisesRegex(ValueError, "both"):
            defense_ranking_loss(logits, torch.zeros(3, dtype=torch.bool))

    def test_granular_action_sampling_preserves_ties_and_all_candidates(self):
        d = dict(Observation=dict(Decision="Action"), Actions=[
            dict(Id="m1", Kind="Move"), dict(Id="m2", Kind="Move"),
            dict(Id="p", Kind="BeginPrimary"), dict(Id="skip", Kind="Pass")])
        before = copy.deepcopy(d)
        self.assertEqual(teaching_family(d, ["m2", "m1"], True), "Action/Move")
        self.assertEqual(teaching_family(d, ["p"], True), "Action/BeginPrimary")
        self.assertEqual(teaching_family(d, ["p", "m1"], True), "Action/BeginPrimary+Move")
        self.assertEqual(teaching_family(d, ["m1"]), "Action")
        self.assertEqual(d, before)

    def test_navigation_batch_updates_actor_without_fake_value_targets(self):
        model = CandidateNetwork(2, 1, 3, 2, hidden=8)
        s, a = bandit()
        row = (s, a, torch.tensor([0]), "Action/Move", torch.tensor([True, False]), "train")
        families = {"Action/Move": [row], "Planning": [row]}
        loss = fine_tune_batch(model, families, [row], "navigation")
        loss.backward()
        self.assertTrue(torch.isfinite(loss))
        self.assertTrue(any(p.grad is not None and p.grad.abs().sum() > 0 for p in model.actor.parameters()))
        self.assertTrue(all(p.grad is None for p in model.critic.parameters()))
        with self.assertRaisesRegex(ValueError, "missing navigation"):
            fine_tune_batch(model, {"Action/Move": [row]}, [row], "navigation")

    def test_combined_teaching_rejects_cross_directory_leakage(self):
        enc = Encoder(self.description); d = self.reply["Decision"]
        directories = [Path(self.temp.name) / name for name in ("data-a", "data-b")]
        for path, split in zip(directories, ("train", "holdout")):
            path.mkdir()
            (path / "contract.json").write_text(json.dumps(enc.contract))
            row = dict(Group="same-game", SourceHash="same-file", Split=split, Decision=d, Preferred=[d["Actions"][0]["Id"]])
            (path / "policy.jsonl").write_text(json.dumps(row))
        with self.assertRaisesRegex(ValueError, "cross-dataset"):
            load_data(directories, enc)

    def test_candidate_permutation_preserves_logits_by_stable_id(self):
        enc = Encoder(self.description)
        d = copy.deepcopy(self.reply["Decision"])
        s, a = enc.encode(d)
        model = CandidateNetwork(**enc.model_kwargs)
        original, _ = model(s, a)
        d["Actions"].reverse()
        s2, a2 = enc.encode(d)
        reversed_dist, _ = model(s2, a2)
        self.assertTrue(torch.allclose(original.logits, reversed_dist.logits.flip(0)))
        self.assertEqual(len(original.probs), len(d["Actions"]))

    def test_explicit_capacity_and_profile_rejection(self):
        enc = Encoder(self.description)
        d = copy.deepcopy(self.reply["Decision"])
        d["Observation"]["Rules"]["VictoryMarksRequired"] += 1
        with self.assertRaisesRegex(ValueError, "profile"):
            enc.encode(d)
        d = copy.deepcopy(self.reply["Decision"])
        enc.max_nodes = 2
        with self.assertRaisesRegex(ValueError, "capacity"):
            enc.encode(d)
        old = copy.deepcopy(self.description); old["Contract"]["ObservationVersion"] = 2
        with self.assertRaisesRegex(ValueError, "unsupported"):
            Encoder(old)

    def test_teaching_groups_cannot_cross_splits_and_tied_labels_are_valid(self):
        enc = Encoder(self.description); d = self.reply["Decision"]
        row = dict(Group="one-game", SourceHash="same-source", Split="train", Decision=d, Preferred=[d["Actions"][0]["Id"]])
        other = dict(row, Split="holdout")
        path = Path(self.temp.name) / "rows.jsonl"
        path.write_text(json.dumps(row) + "\n" + json.dumps(other))
        with self.assertRaisesRegex(ValueError, "leakage"):
            examples(path, enc)
        other.update(Group="separate-game", SourceHash="different-source")
        path.write_text(json.dumps(row) + "\n" + json.dumps(other))
        rows = examples(path, enc); s, a, _, family = rows["train"][0]
        model = CandidateNetwork(**enc.model_kwargs)
        # All tied candidates carry probability one; adding labels never increases the loss.
        all_tied = (s, a, torch.arange(len(a)), family)
        self.assertAlmostEqual(imitation_loss(model, all_tied).item(), 0., places=5)
        self.assertGreater(imitation_loss(model, rows["train"][0]).item(), 0.)

    def test_public_features_respond_to_individual_status_equipment_and_times(self):
        enc = Encoder(self.description); d = copy.deepcopy(self.reply["Decision"])
        original, _ = enc.encode(d)
        opponent = next(p for p in d["Observation"]["Players"] if p["Seat"] == 1)
        opponent["Cards"][0]["Zone"] = "Discarded"
        opponent["Cards"][0]["PlayedRound"] = 3
        opponent['Purple'] = enc.card_ids[0]
        opponent['PoisonDefense'] = True
        changed, _ = enc.encode(d)
        self.assertFalse(equal_graph(original, changed))

    def test_checkpoint_restores_rng_optimizer_and_rejects_contract(self):
        enc = Encoder(self.description)
        s, a = enc.encode(self.reply["Decision"])
        model = CandidateNetwork(**enc.model_kwargs); opt = torch.optim.Adam(model.parameters())
        d, v = model(s, a); (v.square() - d.log_prob(torch.tensor(0))).backward(); opt.step()
        path = Path(self.temp.name) / "checkpoint.pt"
        cuda_initialized = torch.cuda.is_initialized()
        checkpoint(path, model, opt, enc, {}, 1, 1, [])
        self.assertEqual(torch.cuda.is_initialized(), cuda_initialized, "CPU checkpoint must not initialize CUDA")
        expected = torch.rand(4)
        restored, data = load_checkpoint(path, enc, "cpu")
        opt2 = torch.optim.Adam(restored.parameters()); opt2.load_state_dict(data["optimizer"])
        torch.set_rng_state(data["torch_rng"])
        self.assertTrue(torch.equal(expected, torch.rand(4)))
        for net, optimizer in ((model, opt), (restored, opt2)):
            optimizer.zero_grad(); d, v = net(s, a)
            (v.square() - d.log_prob(torch.tensor(0))).backward(); optimizer.step()
        for k, value in model.state_dict().items():
            self.assertTrue(torch.equal(value, restored.state_dict()[k]), k)
        enc.contract = copy.deepcopy(enc.contract); enc.contract["ContentHash"] = "wrong"
        with self.assertRaisesRegex(ValueError, "incompatible"):
            load_checkpoint(path, enc, "cpu")

    def test_individual_redistribution_and_exact_hero_positions_are_distinguishable(self):
        enc=Encoder(self.description);original,_=enc.encode(self.planning)
        mutations={}
        d=copy.deepcopy(self.planning);d['Observation']['Players'][0]['Gold']+=1;d['Observation']['Players'][2]['Gold']-=1
        mutations['same-team gold redistribution']=d
        d=copy.deepcopy(self.planning)
        heroes=[u for u in d['Observation']['Units'] if u['Kind']=='hero' and u['Team']=='Red']
        self.assertEqual(len(heroes),2)
        heroes[0]['Position'],heroes[1]['Position']=heroes[1]['Position'],heroes[0]['Position']
        mutations['same-team same-kind position swap']=d
        for name,d in mutations.items():
            with self.subTest(name=name): self.assertFalse(equal_graph(original,enc.encode(d)[0]))

    def test_previously_ignored_fields_each_reach_encoder(self):
        enc=Encoder(self.description);original,_=enc.encode(self.planning)
        changes=[('purple',lambda o:o['Players'][1].update(Purple=enc.card_ids[-1])),
            ('poison defense',lambda o:o['Players'][1].update(PoisonDefense=True)),
            ('played round',lambda o:o['Players'][1]['Cards'][0].update(PlayedRound=2)),
            ('played turn',lambda o:o['Players'][1]['Cards'][0].update(PlayedTurn=3)),
            ('effective bonus',lambda o:o['Players'][1]['Effective'].update(Attack=-3)),
            ('permanent bonus',lambda o:o['Players'][1]['Permanent'].update(Attack=3)),
            ('active actor',lambda o:o.update(ActiveSeat=2)),
            ('decision kind',lambda o:o.update(Decision='defense')),
            ('public event time',lambda o:o['PublicHistory'][0].update(Round=2))]
        for name,fn in changes:
            d=copy.deepcopy(self.planning);fn(d['Observation'])
            with self.subTest(name=name):self.assertFalse(equal_graph(original,enc.encode(d)[0]))
        effect=dict(Key=0,Kind='MovementBoundary',Card=enc.card_ids[0],SourceUnit='hero:1',ProtectedUnit='hero:2',
            Controller=1,CreatedRound=1,CreatedTurn=1,Order=0,StartRound=1,StartTurn=1,EndRound=1,EndTurn=2,
            BaseRadius=2,PersistsThroughDefeat=False,ExemptSeat=None,Duration='ThisTurn',AreaKind='SkillRange',Area=[dict(X=0,Y=0)])
        d=copy.deepcopy(self.planning);d['Observation']['Effects']=[effect];base=enc.encode(d)[0]
        for key,value in [('EndTurn',3),('ExemptSeat',2),('BaseRadius',3),('PersistsThroughDefeat',True),('SourceUnit','hero:3'),('Area',[dict(X=1,Y=0)])]:
            changed=copy.deepcopy(d);changed['Observation']['Effects'][0][key]=value
            with self.subTest(effect_field=key):self.assertFalse(equal_graph(base,enc.encode(changed)[0]))

    def test_entity_order_and_references_are_not_learned_array_indexes(self):
        enc=Encoder(self.description);s,a=enc.encode(self.planning)
        d=copy.deepcopy(self.planning)
        d['Observation']['Players'].reverse();d['Observation']['Units'].reverse()
        for p in d['Observation']['Players']:p['Cards'].reverse()
        other,b=enc.encode(d);self.assertTrue(equal_graph(s,other));self.assertTrue(torch.equal(a,b))
        net=CandidateNetwork(**enc.model_kwargs)
        self.assertTrue(torch.equal(net(s,a)[0].logits,net(other,b)[0].logits))

    def test_null_is_not_zero_and_unknown_fields_fail_instead_of_disappearing(self):
        enc=Encoder(self.description);s,_=enc.encode(self.planning)
        d=copy.deepcopy(self.planning);d['Observation']['Players'][0]['Cards'][0]['PlayedRound']=0
        self.assertFalse(equal_graph(s,enc.encode(d)[0]))
        for key in ('FutureMechanic','DebugField'):
            d=copy.deepcopy(self.planning);d['Observation'][key]=1
            with self.assertRaisesRegex(ValueError,'unconsumed'):enc.encode(d)
        d=copy.deepcopy(self.planning);d['Observation']['Players'][1]['Cards'][0]['Zone']='Selected'
        with self.assertRaisesRegex(ValueError,'hidden'):enc.encode(d)

    def test_static_map_spawn_and_lane_reach_the_network(self):
        enc=Encoder(self.description);s,_=enc.encode(self.planning)
        for key,value in [('Lane',True),('Spawn','blueHeroSpawn')]:
            description=copy.deepcopy(self.description);description['Cells'][0][key]=value
            changed=Encoder(description);s2,_=changed.encode(self.planning)
            self.assertNotEqual(enc.signature,changed.signature)
            self.assertFalse(equal_graph(s,s2))

    def test_ppo_improves_synthetic_bandit_separate_from_game_strength(self):
        torch.manual_seed(321)
        s, a = bandit()
        model = CandidateNetwork(2, 1, 3, 2, hidden=16)
        opt = torch.optim.Adam(model.parameters(), lr=.01)
        initial = model(s, a)[0].probs[1].item()
        for _ in range(12):
            records = []
            with torch.no_grad():
                dist, value = model(s, a)
                for _ in range(64):
                    choice = dist.sample()
                    records.append((s, a, choice.item(), dist.log_prob(choice).item(), 1. if choice == 1 else -1., value.item(), 0., True))
            update(model, opt, records, "cpu", epochs=3)
        final = model(s, a)[0].probs[1].item()
        self.assertGreater(final, .85)
        self.assertGreater(final, initial + .25)

    def test_card_program_steps_parameters_and_effect_meaning_reach_model(self):
        enc=Encoder(self.description); original=enc.encode(self.planning)[0]
        for part in ('parameter','steps','effect','step_definition'):
            d=copy.deepcopy(self.description)
            if part=='parameter':
                c=next(c for c in d['Cards'] if c['Id']=='wasp-17')
                next(p for p in c['Mechanics']['Parameters'] if p['Key']=='TextMoveDistance')['Number']=2
            elif part=='steps':
                c=next(c for c in d['Cards'] if c['Id']=='wasp-17');c['Mechanics']['Steps'].pop(3)
            elif part=='effect':d['EffectDefinitions'][0]['Amount']=7
            else:d['StepDefinitions'][0]['Optional']=not d['StepDefinitions'][0]['Optional']
            changed=Encoder(d)
            with self.subTest(part=part):
                self.assertNotEqual(enc.signature,changed.signature)
                self.assertFalse(equal_graph(original,changed.encode(self.planning)[0]))
        self.assertEqual(len(self.description['Cards']),108)
        self.assertEqual(len(self.description['EffectDefinitions']),24)
        d=copy.deepcopy(self.description);d['SecretState']={}
        with self.assertRaisesRegex(ValueError,'catalog fields'):Encoder(d)

    def test_personal_history_public_position_and_restriction_are_not_dropped(self):
        enc=Encoder(self.description);base=enc.encode(self.planning)[0]
        d=copy.deepcopy(self.planning)
        event=copy.deepcopy(d['Observation']['PublicHistory'][0]);event.update(Kind='DefenseCalculated',Card=enc.card_ids[0],Seat=d['Observation']['Seat'],Value='block',Amount=None,Amount2=None,Amount3=None,Ordinal=0)
        d['Observation']['OwnHistory'].append(event)
        self.assertFalse(equal_graph(base,enc.encode(d)[0]))
        one=enc.encode(d)[0];d['Observation']['OwnHistory'][-1]['AtPublicOrdinal']+=1
        self.assertFalse(equal_graph(one,enc.encode(d)[0]))
        d=copy.deepcopy(self.planning);d['Observation']['Restrictions']=[dict(Action='Defend',Card=enc.card_ids[0],SourceCard='',Reason='unblockable')]
        self.assertFalse(equal_graph(base,enc.encode(d)[0]))

    def test_static_catalog_order_is_irrelevant(self):
        d=copy.deepcopy(self.description)
        d['Cards'].reverse();d['StepDefinitions'].reverse();d['EffectDefinitions'].reverse()
        for c in d['Cards']:c['Mechanics']['Parameters'].reverse()
        one=Encoder(self.description).encode(self.planning)[0];two=Encoder(d).encode(self.planning)[0]
        self.assertTrue(equal_graph(one,two))

    def test_bad_action_fails_and_preserves_authority(self):
        audit = Path(self.temp.name) / "bad"
        with Bridge(ROOT, DOTNET, audit) as b:
            reply = b.call(op="reset", seed=19, learner=0, swap=0, limit=50)
            with self.assertRaisesRegex(RuntimeError, "unknown_action"):
                b.call(op="step", revision=reply["Decision"]["Revision"], action="invalid")
        self.assertEqual(len(list(audit.glob("*/failure-save.json"))), 1)
        self.assertFalse(list(audit.glob("*/result.json")))


if __name__ == "__main__":
    unittest.main(verbosity=2)
