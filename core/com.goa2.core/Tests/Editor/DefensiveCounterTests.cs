using System.IO;
using System.Linq;
using Goa2.Application;
using Goa2.Domain;
using Goa2.Infrastructure;
using Goa2.Rules;
using NUnit.Framework;
using static Goa2.Tests.SessionTests;
using static Goa2.Tests.TurnFlowTests;

namespace Goa2.Tests
{
    public sealed class DefensiveCounterTests
    {
        internal const string Card = "brogan-17-防守反击";
        internal static GameSession Ready(ContentCatalog catalog, bool attacks = true)
        {
            var game = LocalGameFactory.Create(catalog, "defensive-counter", new[] { "A", "B", "C", "D" }, 42, true);
            Apply(game, 0, CommandKind.DebugPrepare, "brogan,sabina,tigerclaw,wasp");
            Apply(game, 0, CommandKind.DebugEquipCard, Card, target: 0);
            foreach (var p in new[] { (0, new Hex(6,-8)), (1, new Hex(7,-8)), (2, new Hex(5,-8)), (3, new Hex(6,-9)) })
                Apply(game, 0, CommandKind.DebugTeleport, "hero:" + p.Item1, cell: p.Item2);
            string[] cards = { Card, attacks ? "sabina-01-拔枪" : "sabina-07-指挥", "tigerclaw-07-伺机待发", attacks ? "wasp-00-闪耀之刃" : "wasp-07-抵挡屏障" };
            for (int seat = 0; seat < 4; seat++) Apply(game, seat, CommandKind.SelectCard, cards[seat]);
            OpportuneMomentTests.AdvanceTo(game, 0);
            return game;
        }
        private static void Choose(GameSession game, int seat = 1)
        { Apply(game, 0, CommandKind.BeginPrimary); Apply(game, 0, CommandKind.ChooseEffectTarget, "hero:" + seat); }

        [Test] public void ExactContractAndGate()
        {
            var card = BattlefieldTests.Catalog().Card(Card);
            Assert.That(card.PrimaryFamily, Is.EqualTo("skill")); Assert.That(card.Initiative, Is.EqualTo(10));
            Assert.That(card.SecondaryDefense, Is.EqualTo(7)); Assert.That(card.SecondaryMovement, Is.EqualTo(2));
            Assert.That(CombatRules.HasPrimaryProgram(card), Is.True); Assert.That(CombatRules.HasPrimaryProgram(card, 61), Is.False);
            card.Text += "可选"; Assert.That(CombatRules.HasPrimaryProgram(card), Is.False);
        }
        [Test] public void BothResolvedAndUnresolvedAttacksQualifyButOnlyAdjacentEnemyHeroes()
        {
            var cat = BattlefieldTests.Catalog(); var game = Ready(cat);
            Apply(game, 0, CommandKind.BeginPrimary);
            Assert.That(game.View(0).EffectTargets, Is.EquivalentTo(new[] { "hero:1", "hero:3" }));
            ChargeTests.Restore(cat, game);
            game = Ready(cat); Apply(game, 0, CommandKind.DebugTeleport, "hero:3", cell: new Hex(4,-9));
            Apply(game, 0, CommandKind.BeginPrimary); Assert.That(game.View(0).EffectTargets, Is.EqualTo(new[] { "hero:1" }));
        }
        [Test] public void NonAttackEnemiesDoNotQualify()
        {
            var cat = BattlefieldTests.Catalog(); var game = Ready(cat, false); Apply(game, 0, CommandKind.BeginPrimary);
            Assert.That(game.View(0).Pending?.Kind, Is.Not.EqualTo("forced_discard"));
            Assert.That(game.View(0).Events.Any(e => e.Kind == "HeroDefeated" || e.Kind == "ForcedDiscardRequired"), Is.False);
            ChargeTests.Restore(cat, game);
        }
        [TestCase(1)] [TestCase(3)] public void TargetPaysPrivatelyAndParentResolvesOnce(int target)
        {
            var cat = BattlefieldTests.Catalog(); var game = Ready(cat); Choose(game, target);
            Assert.That(game.View(target).CanDeclineRetaliationDiscard, Is.True);
            Assert.That(game.View(target).Pending.Source, Is.EqualTo(Card)); game = ChargeTests.Restore(cat, game);
            string payment = game.View(target).ForcedDiscardCards.First();
            foreach (int? viewer in new int?[] { null, 0, 1, 2, 3 })
                if (viewer != target) Assert.That(game.View(viewer).ForcedDiscardCards, Is.Empty);
            Apply(game, target, CommandKind.ForcedDiscard, payment);
            Assert.That(game.View(target).OwnCards.Single(c => c.CardId == payment).Zone, Is.EqualTo(CardZone.Discarded));
            Assert.That(game.View(null).Events.Any(e => e.Kind == "CardDiscarded"), Is.False);
            Assert.That(game.View(0).Events.Count(e => e.Kind == "CardResolved" && e.CardId == Card), Is.EqualTo(1));
            Assert.That(game.View(0).Events.Any(e => e.Kind == "AttackCalculated" || e.Kind == "HeroDefeated"), Is.False);
            ChargeTests.Restore(cat, game);
        }
        [TestCase(false, false)] [TestCase(true, false)] [TestCase(false, true)] [TestCase(true, true)]
        public void RefusalOrEmptyHandDefeatsWithRewardsAndVictoryStopsParent(bool empty, bool victory)
        {
            var cat = BattlefieldTests.Catalog(); var game = Ready(cat);
            if (victory) Apply(game, 0, CommandKind.DebugSetCrystal, "1", target: 1);
            if (empty) foreach (var card in game.View(1).OwnCards.Where(c => c.Zone == CardZone.InHand).ToArray())
                Apply(game, 0, CommandKind.DebugDiscard, card.CardId, target: 1);
            Choose(game);
            if (!empty) { game = ChargeTests.Restore(cat, game); Apply(game, 1, CommandKind.DeclineRetaliationDiscard); }
            Assert.That(game.View(0).Units.Any(u => u.Seat == 1), Is.False);
            Assert.That(game.View(0).Players[0].Gold, Is.EqualTo(1)); Assert.That(game.View(0).Players[2].Gold, Is.EqualTo(1));
            Assert.That(game.View(0).Events.Last(e => e.Kind == "HeroDefeated").CardId, Is.EqualTo(Card));
            Assert.That(game.View(0).Phase == Phase.Finished, Is.EqualTo(victory));
            Assert.That(game.View(0).Events.Count(e => e.Kind == "CardResolved" && e.CardId == Card), Is.EqualTo(victory ? 0 : 1));
            ChargeTests.Restore(cat, game);
        }
        [Test] public void InvalidPaymentsAreAtomicAndDuplicateIsIdempotent()
        {
            var cat = BattlefieldTests.Catalog(); var game = Ready(cat); Choose(game);
            foreach (var command in new[] { Cmd(game, 0, CommandKind.ForcedDiscard, "sabina-07-指挥"), Cmd(game, 0, CommandKind.DeclineRetaliationDiscard), Cmd(game, 1, CommandKind.ForcedDiscard, "skip"), Cmd(game, 1, CommandKind.ForcedDiscard, "sabina-01-拔枪") })
            { string before = game.ExportSave(); Assert.That(game.Execute(command.ActorSeat, command).Accepted, Is.False); Assert.That(game.ExportSave(), Is.EqualTo(before)); }
            var pay = Cmd(game, 1, CommandKind.ForcedDiscard, game.View(1).ForcedDiscardCards.First());
            Assert.That(game.Execute(1, pay).Accepted, Is.True); string after = game.ExportSave();
            Assert.That(game.Execute(1, pay).Duplicate, Is.True); Assert.That(game.ExportSave(), Is.EqualTo(after));
            ChargeTests.Restore(cat, game);
        }
        [Test] public void ImmuneTargetIsExcludedWithoutChangingTheOtherCandidate()
        {
            var cat = BattlefieldTests.Catalog(); var game = Ready(cat); Apply(game, 0, CommandKind.BeginPrimary);
            var state = new JsonStateCodec().Read(game.ExportSave());
            state.Effects.Add(new ActiveEffect { Id = "isolated-immunity", ControllerSeat = 1, SourceUnitId = "hero:1", SourceCardId = "tigerclaw-07-伺机待发", Kind = EffectKind.ImmunityAndUnitTraversal, Window = EffectTimeline.Create(1,1,4,EffectDuration.ThisTurn) });
            Assert.That(GameRules.LegalEffectTargets(cat, state, 0), Is.EqualTo(new[] { "hero:3" }));
        }
        [Test] public void Frozen61WindowRetainsBytesAndDoesNotExposeNewSkill()
        {
            var cat = BattlefieldTests.Catalog(); string save = File.ReadAllText(Path.Combine(ContentTests.Root(), "tests/fixtures/engine61-fortify-heavy-payment.json"));
            var game = LocalGameFactory.Restore(cat, save); Assert.That(game.ExportSave(), Is.EqualTo(save));
            Assert.That(game.View(0).SupportedPrimaryCards, Does.Not.Contain(Card));
        }
    }
}
