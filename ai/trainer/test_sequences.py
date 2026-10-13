"""Causal memory, candidate invariance and real optimizer/restore checks."""
import copy
from pathlib import Path
import tempfile
from types import SimpleNamespace
import unittest
import torch
from policy import CandidateNetwork, Graph, update
from sequences import Episode, replay_chunk, validate_episodes
from train import checkpoint, load_checkpoint, run_episode


def graph(value=1.):
    return Graph(torch.tensor([[value, 0.], [0., 1.], [.2, .3]]),
                 torch.tensor([[1], [2], [3]]), torch.tensor([[0, 2], [2, 1], [0, 1]])), torch.tensor([0, 1])


def network(architecture='entity-gru'):
    return CandidateNetwork(2, 1, 4, 2, hidden=8, architecture=architecture, latent_count=3, memory_dim=8)


def collect(model, key='game', seat=0, length=5):
    records = []
    memory = model.initial_memory('cpu')
    with torch.no_grad():
        for i in range(length):
            s, a = graph(i+1.)
            dist, v, memory = model.step(s, a, memory)
            records.append((s, a, i % 2, dist.log_prob(torch.tensor(i % 2)).item(),
                            1. if i == length-1 else 0., v.item(), 0., i == length-1))
    for i in range(length-1):
        r = list(records[i]); r[6] = records[i+1][5]; records[i] = tuple(r)
    return Episode(key, seat, records)


class FakeEncoder:
    def encode(self, decision):
        return graph(float(decision['Revision']))


class FakeHost:
    def __init__(self, truncated=False, foreign=False):
        self.truncated, self.foreign = truncated, foreign
        self.submitted = []

    def call(self, **request):
        if request['op'] == 'reset':
            self.index = 0; self.seat = request['learner']; self.submitted = []
        else:
            assert request['revision'] == self.index+1
            assert request['action'] in ('stable-a', 'stable-b')
            self.submitted.append(request['action']); self.index += 1
        ended = self.index == 2
        return dict(Terminated=ended and not self.truncated, Truncated=ended and self.truncated,
                    Reward=1. if ended and not self.truncated else 0., EnvironmentSteps=self.index*4,
                    Decision=dict(Revision=self.index+1, Observation=dict(Seat=1-self.seat if self.foreign and self.index else self.seat),
                                  Actions=[dict(Id='stable-a'), dict(Id='stable-b')]))


class SequenceTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        torch.set_num_threads(2)
        torch.use_deterministic_algorithms(True)

    def setUp(self):
        torch.manual_seed(714)

    def test_all_architectures_preserve_candidate_and_entity_permutations(self):
        s, a = graph()
        permutation = torch.tensor([2, 0, 1]); inverse = permutation.argsort()
        edges = s.edges.clone(); edges[:2] = inverse[edges[:2]]
        shuffled = Graph(s.numbers[permutation], s.categories[permutation], edges)
        for architecture in ('graph', 'entity', 'entity-gru'):
            with self.subTest(architecture=architecture):
                model = network(architecture); memory = model.initial_memory('cpu')
                d, v, m = model.step(s, a, memory)
                e, w, n = model.step(shuffled, inverse[a.flip(0)], memory)
                self.assertEqual(len(d.probs), 2)
                self.assertTrue(torch.allclose(d.logits, e.logits.flip(0), atol=1e-6))
                self.assertTrue(torch.allclose(v, w, atol=1e-6))
                if m is not None:
                    self.assertTrue(torch.allclose(m, n, atol=1e-6))

    def test_recurrent_requires_explicit_memory_and_does_not_mutate_it(self):
        model = network(); s, a = graph(); memory = model.initial_memory('cpu')
        before = memory.clone()
        _, _, following = model.step(s, a, memory)
        self.assertTrue(torch.equal(memory, before))
        self.assertFalse(torch.equal(following, before))
        with self.assertRaisesRegex(ValueError, 'sequence'):
            model(s, a)
        with self.assertRaisesRegex(ValueError, 'seat memory'):
            model.step(s, a)
        with self.assertRaisesRegex(ValueError, 'seat memory'):
            model.step(s, a, torch.zeros(1, 9))
        with self.assertRaisesRegex(ValueError, 'feedforward'):
            network('entity').step(s, a, memory)

    def test_interleaved_seats_do_not_modify_each_others_memory(self):
        model = network(); s, a = graph()
        m0 = model.initial_memory(); m1 = model.initial_memory()
        _, _, m0 = model.step(s, a, m0)
        expected, ev, _ = model.step(s, a, m0)
        for _ in range(4):
            _, _, m1 = model.step(*graph(9.), m1)
        actual, av, _ = model.step(s, a, m0)
        self.assertTrue(torch.equal(expected.logits, actual.logits))
        self.assertTrue(torch.equal(ev, av))

    def test_causal_prefix_replay_matches_online_before_and_after_weight_change(self):
        model = network(); episode = collect(model)
        for changed in (False, True):
            if changed:
                with torch.no_grad(): model.memory.weight_hh.add_(.017)
            memory = model.initial_memory(); logs, values = [], []
            for s, a, choice, *_ in episode.records:
                d, v, memory = model.step(s, a, memory)
                logs.append(d.log_prob(torch.tensor(choice))); values.append(v)
            part_logs, _, part_values = replay_chunk(model, episode, 3, 5, 'cpu')
            self.assertTrue(torch.equal(part_logs, torch.stack(logs)[3:]))
            self.assertTrue(torch.equal(part_values, torch.stack(values)[3:]))

    def test_tbptt_connects_past_inside_chunk_and_detaches_prefix(self):
        model = network(); episode = collect(model, length=3)
        first = episode.records[0][0].numbers
        first.requires_grad_(True)
        _, _, values = replay_chunk(model, episode, 0, 3, 'cpu')
        values[-1].backward()
        self.assertGreater(first.grad.abs().sum().item(), 0.)
        first.grad = None; model.zero_grad(set_to_none=True)
        _, _, values = replay_chunk(model, episode, 1, 3, 'cpu')
        values[-1].backward()
        self.assertIsNone(first.grad)
        self.assertGreater(model.memory.weight_hh.grad.abs().sum().item(), 0.)

    def test_history_changes_prediction_without_changing_current_input(self):
        model = network(); s, a = graph()
        _, _, left = model.step(*graph(1.), model.initial_memory())
        _, _, right = model.step(*graph(8.), model.initial_memory())
        ld, lv, _ = model.step(s, a, left); rd, rv, _ = model.step(s, a, right)
        self.assertFalse(torch.equal(left, right))
        self.assertTrue(not torch.equal(ld.logits, rd.logits) or not torch.equal(lv, rv))

    def test_episode_boundaries_and_shuffled_individual_steps_are_rejected(self):
        episode = collect(network())
        validate_episodes([episode])
        with self.assertRaisesRegex(ValueError, 'ordered Episode'):
            validate_episodes(episode.records)
        with self.assertRaisesRegex(ValueError, 'duplicate'):
            validate_episodes([episode, episode])
        broken = copy.deepcopy(episode)
        broken.records[0] = (*broken.records[0][:7], True)
        with self.assertRaisesRegex(ValueError, 'final boundary'):
            validate_episodes([broken])

    def test_rollout_bootstrap_does_not_double_advance_memory(self):
        model = network(); episodes = []
        result = run_episode(FakeHost(truncated=True), model, FakeEncoder(), 'cpu', 1, 0, 8, episodes)
        self.assertTrue(result['truncated']); self.assertFalse(result['terminated'])
        self.assertEqual(result['reward'], 0.)
        self.assertEqual(len(episodes), 1)
        rows = episodes[0].records; memory = model.initial_memory()
        with torch.no_grad():
            for i in range(3):
                _, value, memory = model.step(*graph(float(i+1)), memory)
                if i < 2: self.assertEqual(rows[i][5], value.item())
                if i > 0: self.assertEqual(rows[i-1][6], value.item())
        self.assertTrue(rows[-1][7])
        self.assertNotEqual(rows[-1][6], 0.)

    def test_terminal_bootstrap_and_new_game_reset(self):
        model = network(); episodes = []; host = FakeHost()
        one = run_episode(host, model, FakeEncoder(), 'cpu', 1, 0, 8, episodes)
        self.assertTrue(one['terminated']); self.assertEqual(episodes[0].records[-1][6], 0.)
        other = []; run_episode(host, model, FakeEncoder(), 'cpu', 2, 1, 8, other)
        self.assertEqual(episodes[0].records[0][5], other[0].records[0][5])
        self.assertEqual(other[0].seat, 1)
        with self.assertRaisesRegex(ValueError, 'foreign-seat'):
            run_episode(FakeHost(foreign=True), model, FakeEncoder(), 'cpu', 1, 0, 8, [])

    def test_ppo_checkpoint_resume_reproduces_sequence_optimizer_and_rng(self):
        enc = SimpleNamespace(signature='test-fixture', contract={'Rules': 'test-only'})
        model = network(); opt = torch.optim.Adam(model.parameters(), lr=.0003)
        episodes = [collect(model, 'a'), collect(model, 'b', seat=1)]
        # Populate Adam moments before saving, then collect on-policy records again.
        self.assertTrue(update(model, opt, episodes, 'cpu', epochs=1, batch_size=4, sequence_length=2))
        episodes = [collect(model, 'c'), collect(model, 'd', seat=1)]
        before = model.memory.weight_hh.detach().clone()
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory)/'checkpoint.pt'
            checkpoint(path, model, opt, enc, {}, 2, 1, [])
            saved_rng = torch.get_rng_state()
            metrics1 = update(model, opt, episodes, 'cpu', epochs=1, batch_size=4, sequence_length=2)
            restored, data = load_checkpoint(path, enc, 'cpu', 'entity-gru')
            opt2 = torch.optim.Adam(restored.parameters(), lr=.0003); opt2.load_state_dict(data['optimizer'])
            torch.set_rng_state(data['torch_rng'])
            self.assertTrue(torch.equal(saved_rng, torch.get_rng_state()))
            metrics2 = update(restored, opt2, episodes, 'cpu', epochs=1, batch_size=4, sequence_length=2)
            self.assertEqual(metrics1, metrics2)
            self.assertTrue(metrics1)
            self.assertFalse(torch.equal(before, model.memory.weight_hh))
            for key, value in model.state_dict().items():
                self.assertTrue(torch.equal(value, restored.state_dict()[key]), key)
            with self.assertRaisesRegex(ValueError, 'architecture'):
                load_checkpoint(path, enc, 'cpu', 'entity')
            data['format'] = 1; torch.save(data, path)
            with self.assertRaisesRegex(ValueError, 'incompatible'):
                load_checkpoint(path, enc, 'cpu')

    def test_guard_interrupts_before_submitting_and_preserves_no_fake_episode(self):
        def stop(): raise TimeoutError('test deadline')
        rows = []; host = FakeHost()
        with self.assertRaises(TimeoutError):
            run_episode(host, network(), FakeEncoder(), 'cpu', 1, 0, 8, rows, guard=stop)
        self.assertEqual(rows, []); self.assertEqual(host.submitted, [])


if __name__ == '__main__':
    unittest.main(verbosity=2)
