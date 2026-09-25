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
    public sealed class ThunderstormTests
    {
        internal const string Card = "wasp-12-电闪雷鸣";
        internal const string Basic = "wasp-06-静电封锁";

        internal static GameSession Setup(ContentCatalog catalog, bool owned = true, int engine = GameState.CurrentEngineVersion,
            string heroes = "wasp,brogan,sabina,arien", string played = Basic)
        {
            var game = LocalGameFactory.Create(catalog, "thunderstorm", new[] { "A", "B", "C", "D" }, 42, true, engine);
            Apply(game, 0, CommandKind.DebugPrepare, heroes);
            if (owned)
            {
                Apply(game, 0, CommandKind.DebugSetGold, "28", target: 0);
                Apply(game, 0, CommandKind.DebugAdvance, "round");
                Apply(game, 0, CommandKind.ResolveRoundEnd);
                for (int i = 0; i < 7; i++)
                    Apply(game, 0, CommandKind.ChooseUpgrade, game.View(0).UpgradeOptions.First().CardId);
                Assert.That(game.View(0).Players[0].PurpleCardId, Is.EqualTo(Card));
            }
            Apply(game, 0, CommandKind.DebugTeleport, "hero:0", cell: new Hex(-2, 0));
            Apply(game, 0, CommandKind.DebugTeleport, "hero:1", cell: new Hex(-2, 1));
            Apply(game, 0, CommandKind.DebugTeleport, "hero:2", cell: new Hex(6, -9));
            Apply(game, 0, CommandKind.DebugTeleport, "hero:3", cell: new Hex(7, -9));
            if (!game.View(0).OwnCards.Any(c => c.CardId == played)) Apply(game, 0, CommandKind.DebugEquipCard, played, target: 0);
            string[] cards = new[] { played }.Concat(heroes.Split(',').Skip(1).Select(h => catalog.Cards.Single(c => c.HeroId == h && c.Color == "gold").Id)).ToArray();
            for (int i = 0; i < 4; i++) Apply(game, i, CommandKind.SelectCard, cards[i]);
            OpportuneMomentTests.AdvanceTo(game, 0);
            return game;
        }

        private static void Trigger(GameSession game)
        {
            Apply(game, 0, CommandKind.BeginPrimary);
            Assert.That(game.View(0).Pending?.Kind, Is.EqualTo("effect_target"));
            Assert.That(game.View(0).Pending?.Source, Is.EqualTo(Card));
        }

        [Test]
        public void BasicSkillCompletesIntoOnePublicGlobalTargetChoice()
        {
            var game = Setup(BattlefieldTests.Catalog());
            int resolved = game.View(0).Events.Count(e => e.Kind == "CardResolved" && e.CardId == Basic);
            Trigger(game);
            Assert.That(game.View(0).EffectTargets, Is.EquivalentTo(new[] { "hero:1", "hero:3" }));
            Assert.That(game.View(1).EffectTargets, Is.Empty);
            Assert.That(game.View(null).EffectTargets, Is.Empty);
            Assert.That(game.View(0).Effects.Any(e => e.SourceCardId == Basic), Is.True);
            Assert.That(game.View(0).Events.Count(e => e.Kind == "CardResolved" && e.CardId == Basic), Is.EqualTo(resolved));
        }

        [Test]
        public void BothChoicesRestoreAndOnlyVictimCanPayThenParentResolvesOnce()
        {
            var catalog = BattlefieldTests.Catalog(); var game = Setup(catalog);
            int resolved = game.View(0).Events.Count(e => e.Kind == "CardResolved" && e.CardId == Basic);
            Trigger(game); game = ChargeTests.Restore(catalog, game);
            Apply(game, 0, CommandKind.ChooseEffectTarget, "hero:3");
            Assert.That(game.View(3).Pending?.Kind, Is.EqualTo("forced_discard"));
            Assert.That(game.View(3).Pending?.ChooserSeat, Is.EqualTo(3));
            Assert.That(game.View(3).Pending?.Source, Is.EqualTo(Card));
            game = ChargeTests.Restore(catalog, game);
            var payment = game.View(3).ForcedDiscardCards.First();
            var save = game.ExportSave();
            Assert.That(game.Execute(0, Cmd(game, 0, CommandKind.ForcedDiscard, payment)).Accepted, Is.False);
            Assert.That(game.ExportSave(), Is.EqualTo(save));
            Apply(game, 3, CommandKind.ForcedDiscard, payment);
            Assert.That(game.View(0).Events.Count(e => e.Kind == "CardResolved" && e.CardId == Basic), Is.EqualTo(resolved + 1));
            Assert.That(game.View(null).Events.Any(e => e.Kind == "CardDiscarded" && e.CardId == payment), Is.False);
            Assert.That(game.View(3).Events.Any(e => e.Kind == "CardDiscarded" && e.CardId == payment), Is.True);
            Assert.That(game.View(0).Players[0].PurpleCardId, Is.EqualTo(Card));
            ChargeTests.Restore(catalog, game);
        }

        [Test]
        public void TargetSelectionRejectsWrongOwnerFriendlyMinionAndSkipAtomically()
        {
            var game = Setup(BattlefieldTests.Catalog()); Trigger(game);
            foreach (var target in new[] { "skip", "hero:0", "hero:2", game.View(0).Units.First(u => u.Kind == "melee").Id })
            {
                var before = game.ExportSave();
                Assert.That(game.Execute(0, Cmd(game, 0, CommandKind.ChooseEffectTarget, target)).Accepted, Is.False);
                Assert.That(game.ExportSave(), Is.EqualTo(before));
            }
            var saved = game.ExportSave();
            Assert.That(game.Execute(1, Cmd(game, 1, CommandKind.ChooseEffectTarget, "hero:3")).Accepted, Is.False);
            Assert.That(game.ExportSave(), Is.EqualTo(saved));
        }

        [Test]
        public void DuplicateTargetAndPaymentDoNotRepeatEffectsOrResolution()
        {
            var game = Setup(BattlefieldTests.Catalog()); Trigger(game);
            var choose = Cmd(game, 0, CommandKind.ChooseEffectTarget, "hero:1");
            Assert.That(game.Execute(0, choose).Accepted, Is.True);
            var before = game.ExportSave(); Assert.That(game.Execute(0, choose).Duplicate, Is.True);
            Assert.That(game.ExportSave(), Is.EqualTo(before));
            var pay = Cmd(game, 1, CommandKind.ForcedDiscard, game.View(1).ForcedDiscardCards.First());
            Assert.That(game.Execute(1, pay).Accepted, Is.True);
            before = game.ExportSave(); Assert.That(game.Execute(1, pay).Duplicate, Is.True);
            Assert.That(game.ExportSave(), Is.EqualTo(before));
        }

        [TestCase(false, 54)] [TestCase(true, 54)] [TestCase(false, GameState.CurrentEngineVersion)]
        public void NoOwnershipOrOldEngineDoesNotTrigger(bool owned, int engine)
        {
            var game = Setup(BattlefieldTests.Catalog(), owned, engine);
            int resolved = game.View(0).Events.Count(e => e.Kind == "CardResolved" && e.CardId == Basic);
            Apply(game, 0, CommandKind.BeginPrimary);
            Assert.That(game.View(0).Pending?.Source, Is.Not.EqualTo(Card));
            Assert.That(game.View(0).Events.Count(e => e.Kind == "CardResolved" && e.CardId == Basic), Is.EqualTo(resolved + 1));
        }

        [Test]
        public void PassingSilverCardDoesNotTriggerItsBasicSkillAbility()
        {
            var game = Setup(BattlefieldTests.Catalog());
            Apply(game, 0, CommandKind.Pass);
            Assert.That(game.View(0).Pending?.Source, Is.Not.EqualTo(Card));
        }

        [TestCase(false)] [TestCase(true)]
        public void EmptyEnemyRemainsSelectableEvenWhenAnotherEnemyHasCards(bool allEmpty)
        {
            var catalog = BattlefieldTests.Catalog(); var game = Setup(catalog);
            foreach (int seat in allEmpty ? new[] { 1, 3 } : new[] { 1 })
                foreach (var card in game.View(seat).OwnCards.Where(c => c.Zone == CardZone.InHand).ToList())
                    Apply(game, 0, CommandKind.DebugDiscard, card.CardId, target: seat);
            Trigger(game);
            Assert.That(game.View(0).EffectTargets, Does.Contain("hero:1"));
            Apply(game, 0, CommandKind.ChooseEffectTarget, "hero:1");
            Assert.That(game.View(0).Events.Count(e => e.Kind == "UltimateTriggered"), Is.EqualTo(1));
            Assert.That(game.View(0).Events.Count(e => e.Kind == "UltimateCompleted"), Is.EqualTo(1));
            Assert.That(game.View(0).Events.Any(e => e.Kind == "ForcedDiscardSkipped" && e.CardId == Card), Is.True);
            Assert.That(game.View(0).Pending?.Kind, Is.Not.EqualTo("forced_discard"));
            ChargeTests.Restore(catalog, game);
        }

        [Test]
        public void NoLivingEnemiesSkipsChoiceAndPreservesOriginalEffect()
        {
            var catalog = BattlefieldTests.Catalog(); var game = Setup(catalog);
            Apply(game, 0, CommandKind.DebugDefeatHero, "hero:1", target: 0);
            Apply(game, 0, CommandKind.DebugDefeatHero, "hero:3", target: 0);
            Apply(game, 0, CommandKind.BeginPrimary);
            Assert.That(game.View(0).Events.Any(e => e.Kind == "UltimateCompleted" && e.Detail == "no_targets"), Is.True);
            Assert.That(game.View(0).Effects.Any(e => e.SourceCardId == Basic), Is.True);
            Assert.That(game.View(0).Pending?.Source, Is.Not.EqualTo(Card));
            ChargeTests.Restore(catalog, game);
        }

        [Test]
        public void MandatoryPaymentCannotSkipOrUseRevealedCard()
        {
            var game = Setup(BattlefieldTests.Catalog()); Trigger(game);
            Apply(game, 0, CommandKind.ChooseEffectTarget, "hero:1");
            foreach (string value in new[] { "skip", "brogan-00-猛攻", Basic })
            {
                var before = game.ExportSave();
                Assert.That(game.Execute(1, Cmd(game, 1, CommandKind.ForcedDiscard, value)).Accepted, Is.False);
                Assert.That(game.ExportSave(), Is.EqualTo(before));
            }
        }

        [TestCase(false)] [TestCase(true)]
        public void DebugMutationCannotDestroyEitherPendingWindow(bool payment)
        {
            var game = Setup(BattlefieldTests.Catalog()); Trigger(game);
            if (payment) Apply(game, 0, CommandKind.ChooseEffectTarget, "hero:1");
            var before = game.ExportSave();
            Assert.That(game.Execute(0, Cmd(game, 0, CommandKind.DebugDiscard, "brogan-13-冲拳", target: 1)).Accepted, Is.False);
            Assert.That(game.ExportSave(), Is.EqualTo(before));
        }

        [Test]
        public void UltimateBindingIsExactVersionedAndSeparateFromPlayablePrograms()
        {
            var catalog = BattlefieldTests.Catalog(); var card = catalog.Card(Card);
            Assert.That(UltimateRules.HasProgram(card), Is.True);
            Assert.That(UltimateRules.HasProgram(card, 54), Is.False);
            Assert.That(CombatRules.HasPrimaryProgram(card), Is.False);
            var game = Setup(catalog);
            Assert.That(game.View(0).SupportedUltimateCards, Does.Contain(Card));
            Assert.That(game.View(0).OwnCards.Any(c => c.CardId == Card), Is.False);
            card.Text += "可选";
            Assert.That(UltimateRules.HasProgram(card), Is.False);
        }

        [Test]
        public void OrdinarySkillAndGoldCardSecondaryMovementDoNotTrigger()
        {
            var catalog = BattlefieldTests.Catalog();
            var skill = Setup(catalog, played: "wasp-14-动力助推");
            Apply(skill, 0, CommandKind.DebugTeleport, "hero:1", cell: new Hex(-4, 1));
            Apply(skill, 0, CommandKind.BeginPrimary);
            Assert.That(skill.View(0).Events.Any(e => e.Kind == "UltimateTriggered"), Is.False);
            var move = Setup(catalog, played: "wasp-00-闪耀之刃");
            Apply(move, 0, CommandKind.Move, cell: move.View(0).SecondaryMoves.First().Destination);
            Assert.That(move.View(0).Events.Any(e => e.Kind == "UltimateTriggered"), Is.False);
        }

        [Test]
        public void FullImmunityAndOtherEnemyImmunityFilterByActualSource()
        {
            // Isolated legality checks. These snapshots are not claimed as replayed gameplay scenarios.
            var catalog = BattlefieldTests.Catalog();
            var game = Setup(catalog, heroes: "wasp,brogan,sabina,tigerclaw"); Trigger(game);
            var state = new JsonStateCodec().Read(game.ExportSave());
            state.Effects.Add(new ActiveEffect { SourceCardId = "tigerclaw-07-伺机待发", SourceUnitId = "hero:3", ControllerSeat = 3,
                Kind = EffectKind.ImmunityAndUnitTraversal, Window = new EffectWindow { StartRound = state.Round, EndRound = state.Round, StartTurn = state.Turn, EndTurn = state.Turn } });
            Assert.That(GameRules.LegalEffectTargets(catalog, state, 0), Does.Not.Contain("hero:3"));
            game = Setup(catalog, heroes: "wasp,sabina,brogan,arien"); Trigger(game);
            state = new JsonStateCodec().Read(game.ExportSave());
            var immunity = new ActiveEffect { SourceCardId = "sabina-10-武装密谋", SourceUnitId = "hero:1", ProtectedUnitId = "hero:1", ControllerSeat = 1,
                Kind = EffectKind.OtherEnemyActionImmunity, ExemptControllerSeat = 2,
                Window = new EffectWindow { StartRound = state.Round, EndRound = state.Round, StartTurn = state.Turn, EndTurn = state.Turn } };
            state.Effects.Add(immunity);
            Assert.That(GameRules.LegalEffectTargets(catalog, state, 0), Does.Not.Contain("hero:1"));
            immunity.ExemptControllerSeat = 0;
            Assert.That(GameRules.LegalEffectTargets(catalog, state, 0), Does.Contain("hero:1"));
        }

        [Test]
        public void DeathDoesNotRemoveUltimateOwnershipOrCreateATrigger()
        {
            var catalog = BattlefieldTests.Catalog(); var game = Setup(catalog);
            Apply(game, 0, CommandKind.DebugSetCrystal, "30", target: 0);
            Apply(game, 0, CommandKind.DebugDefeatHero, "hero:0", target: 1);
            Assert.That(game.View(0).Players[0].PurpleCardId, Is.EqualTo(Card));
            Assert.That(game.View(0).Events.Any(e => e.Kind == "UltimateTriggered"), Is.False);
            ChargeTests.Restore(catalog, game);
        }
    }
}
