import copy
import json
import os
from pathlib import Path
import tempfile
import unittest
import torch
from bridge import Bridge
from policy import CandidateNetwork, Encoder, advantages, update
from train import checkpoint, load_checkpoint
from curriculum import examples, imitation_loss
from defense import defense_ranking_loss, load_data

ROOT = Path(__file__).resolve().parents[2]
DOTNET = Path(os.environ["LOCALAPPDATA"]) / "Goa2V1Toolchain/dotnet/dotnet.exe"


class TrainerTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        torch.set_num_threads(2)
        cls.temp = tempfile.TemporaryDirectory()
        with Bridge(ROOT, DOTNET, Path(cls.temp.name) / "probe") as b:
            cls.description = b.call(op="describe")
            cls.reply = b.call(op="reset", seed=19, learner=0, swap=0, limit=1)

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
        model = CandidateNetwork(len(s), a.shape[1])
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
        d["Actions"][0]["Value"] = "x" * 65
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
        model = CandidateNetwork(len(s), a.shape[1])
        # All tied candidates carry probability one; adding labels never increases the loss.
        all_tied = (s, a, torch.arange(len(a)), family)
        self.assertAlmostEqual(imitation_loss(model, all_tied).item(), 0., places=5)
        self.assertGreater(imitation_loss(model, rows["train"][0]).item(), 0.)

    def test_public_features_respond_to_current_card_attack_and_enemy_equipment(self):
        enc = Encoder(self.description); d = copy.deepcopy(self.reply["Decision"])
        original, _ = enc.encode(d)
        d["Observation"]["CurrentCard"] = enc.card_ids[0]
        d["Observation"]["Attack"] = dict(Final=8, Attacker=2, Defender=0, Ranged=True)
        opponent = next(p for p in d["Observation"]["Players"] if p["Seat"] == 1)
        opponent["Cards"][0]["Zone"] = "Discarded"
        changed, _ = enc.encode(d)
        self.assertFalse(torch.equal(original, changed))

    def test_checkpoint_restores_rng_optimizer_and_rejects_contract(self):
        enc = Encoder(self.description)
        s, a = enc.encode(self.reply["Decision"])
        model = CandidateNetwork(len(s), a.shape[1]); opt = torch.optim.Adam(model.parameters())
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
            self.assertTrue(torch.equal(value, restored.state_dict()[k]))
        enc.contract = copy.deepcopy(enc.contract); enc.contract["ContentHash"] = "wrong"
        with self.assertRaisesRegex(ValueError, "incompatible"):
            load_checkpoint(path, enc, "cpu")

    def test_ppo_improves_synthetic_bandit_separate_from_game_strength(self):
        torch.manual_seed(321)
        s, a = torch.zeros(3), torch.eye(2)
        model = CandidateNetwork(3, 2, hidden=16)
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
