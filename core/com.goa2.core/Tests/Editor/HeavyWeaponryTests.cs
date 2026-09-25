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
    public sealed class HeavyWeaponryTests
    {
        internal const string Card = "sabina-12-重型枪械", Shot = "sabina-00-近身射击", Dodge = "tigerclaw-18-躲闪";
        internal static GameSession Setup(ContentCatalog catalog, bool owned = true, int engine = GameState.CurrentEngineVersion, string defense = Dodge, bool enemyBrogan = false)
        {
            var game = LocalGameFactory.Create(catalog, "heavy-weaponry", new[] { "A", "B", "C", "D" }, 42, true, engine);
            Apply(game, 0, CommandKind.DebugPrepare, enemyBrogan ? "sabina,tigerclaw,arien,brogan" : "sabina,tigerclaw,brogan,arien");
            if (owned)
            {
                Apply(game, 0, CommandKind.DebugSetGold, "28", target: 0);
                Apply(game, 0, CommandKind.DebugAdvance, "round");
                Apply(game, 0, CommandKind.ResolveRoundEnd);
                foreach (string id in new[] { "sabina-02-交叉火力", "sabina-08-带头冲锋", "sabina-13-近身支援", "sabina-04-枪林弹雨", "sabina-10-武装密谋", "sabina-15-火力掩护", Card })
                    Apply(game, 0, CommandKind.ChooseUpgrade, id);
            }
            Apply(game, 0, CommandKind.DebugEquipCard, defense, target: 1);
            Apply(game, 0, CommandKind.DebugTeleport, "hero:0", cell: new Hex(6, -8));
            Apply(game, 0, CommandKind.DebugTeleport, "hero:1", cell: new Hex(7, -8));
            string[] cards = { Shot, "tigerclaw-07-伺机待发", enemyBrogan ? "arien-07-潮水" : "brogan-06-铜墙铁壁", enemyBrogan ? "brogan-06-铜墙铁壁" : "arien-07-潮水" };
            for (int seat = 0; seat < 4; seat++) Apply(game, seat, CommandKind.SelectCard, cards[seat]);
            Assert.That(game.View(0).ActiveSeat, Is.EqualTo(0));
            return game;
        }
        private static void Attack(GameSession game)
        { Apply(game, 0, CommandKind.BeginPrimary); Apply(game, 0, CommandKind.ChooseAttackTarget, "hero:1"); }
        private static void Push(GameSession game)
        { Attack(game); Apply(game, 1, CommandKind.Defend, Dodge); }

        [Test]
        public void BasicAttackAddsTwoAttackAndTwoRangeWithoutChangingUpgradeIcons()
        {
            var game = Setup(BattlefieldTests.Catalog());
            Assert.That(game.View(0).Players[0].PermanentBonuses.ContainsKey("攻击"), Is.False);
            Assert.That(game.View(0).AttackRange, Is.EqualTo(4));
            Attack(game);
            Assert.That(game.View(0).Attack.BaseAttack, Is.EqualTo(2));
            Assert.That(game.View(0).Attack.AttackBonus, Is.EqualTo(2));
            Assert.That(game.View(0).Attack.CardTextBonus, Is.EqualTo(0));
        }

        [Test]
        public void PushedEnemyPaysPrivatelyThenParentFinishesExactlyOnce()
        {
            var catalog = BattlefieldTests.Catalog(); var game = Setup(catalog);
            int resolved = game.View(0).Events.Count(e => e.Kind == "CardResolved" && e.CardId == Shot);
            Push(game);
            Assert.That(game.View(1).Pending?.Source, Is.EqualTo(Card));
            Assert.That(game.View(1).Pending?.Kind, Is.EqualTo("forced_discard"));
            Assert.That(game.View(1).CanDeclineRetaliationDiscard, Is.True);
            Assert.That(game.View(0).Events.Count(e => e.Kind == "CardResolved" && e.CardId == Shot), Is.EqualTo(resolved));
            game = ChargeTests.Restore(catalog, game);
            string pay = game.View(1).ForcedDiscardCards.First();
            Apply(game, 1, CommandKind.ForcedDiscard, pay);
            Assert.That(game.View(0).Events.Count(e => e.Kind == "CardResolved" && e.CardId == Shot), Is.EqualTo(resolved + 1));
            Assert.That(game.View(null).Events.Any(e => e.Kind == "CardDiscarded" && e.CardId == pay), Is.False);
            Assert.That(game.View(1).Events.Any(e => e.Kind == "CardDiscarded" && e.CardId == pay), Is.True);
            ChargeTests.Restore(catalog, game);
        }

        [Test]
        public void LegalPushBlockedAtZeroStillAllowsVoluntaryDefeat()
        {
            var catalog = BattlefieldTests.Catalog(); var game = Setup(catalog);
            Apply(game, 0, CommandKind.DebugTeleport, "hero:2", cell: new Hex(8, -8));
            Push(game);
            Assert.That(game.View(0).Units.Single(u => u.Seat == 1).Position, Is.EqualTo(new Hex(7, -8)));
            Assert.That(game.View(1).Pending?.Source, Is.EqualTo(Card));
            Apply(game, 1, CommandKind.DeclineRetaliationDiscard);
            Assert.That(game.View(0).Units.Any(u => u.Seat == 1), Is.False);
            Assert.That(game.View(0).Events.Last(e => e.Kind == "AttackResolved").Detail, Is.EqualTo("defended:hero:1"));
            ChargeTests.Restore(catalog, game);
        }

        [TestCase(false, 55)] [TestCase(true, 55)] [TestCase(false, GameState.CurrentEngineVersion)]
        public void UnownedOrOldEngineDoesNotTrigger(bool owned, int engine)
        {
            var game = Setup(BattlefieldTests.Catalog(), owned, engine);
            Push(game);
            Assert.That(game.View(1).Pending?.Source, Is.Not.EqualTo(Card));
            Assert.That(game.View(0).Units.Any(u => u.Seat == 1), Is.True);
        }

        [Test]
        public void RangeBoundaryUsesUpgradesPlusUltimateAndDistantDefenseDoesNotPush()
        {
            var catalog = BattlefieldTests.Catalog(); var game = Setup(catalog);
            var origin = game.View(0).Units.Single(u => u.Seat == 0).Position;
            foreach (int distance in new[] { 5, 4 })
            {
                var cell = catalog.Cells.First(c => !c.Obstacle && c.Position.Distance(origin) == distance && !game.View(0).Units.Any(u => u.Position == c.Position));
                Apply(game, 0, CommandKind.DebugTeleport, "hero:1", cell: cell.Position);
                Assert.That(game.View(0).AttackTargets.Contains("hero:1"), Is.EqualTo(distance == 4));
            }
            Push(game);
            Assert.That(game.View(0).Events.Any(e => e.Kind == "UnitPushed"), Is.False);
            Assert.That(game.View(0).Events.Any(e => e.Kind == "UltimateTriggered" && e.CardId == Card), Is.False);
            ChargeTests.Restore(catalog, game);
        }

        [Test]
        public void BasicOnlyModifiersAreProjectedSeparatelyAndBindingIsExact()
        {
            var catalog = BattlefieldTests.Catalog(); var game = Setup(catalog);
            var state = new JsonStateCodec().Read(game.ExportSave());
            Assert.That(game.View(null).Players[0].BasicAttackBonus, Is.EqualTo(2));
            Assert.That(game.View(null).Players[0].BasicAttackRangeBonus, Is.EqualTo(2));
            Assert.That(UltimateRules.AttackBonus(catalog, state, catalog.Card("sabina-04-枪林弹雨"), 0), Is.Zero);
            Assert.That(UltimateRules.AttackBonus(catalog, state, catalog.Card("sabina-06-并肩作战"), 0), Is.Zero);
            Assert.That(UltimateRules.BasicAttackBonus(catalog, state, 1), Is.Zero);
            Assert.That(UltimateRules.HasProgram(catalog.Card(Card), 55), Is.False);
            catalog.Card(Card).Text += "可选";
            Assert.That(UltimateRules.BasicAttackBonus(catalog, state, 0), Is.Zero);
        }

        [TestCase(false)] [TestCase(true)]
        public void WallOrEdgeBlockedLegalPushStillRequiresPayment(bool edge)
        {
            var catalog = BattlefieldTests.Catalog(); var game = Setup(catalog);
            var occupied = game.View(0).Units.Select(u => u.Position).ToList();
            var pair = catalog.Cells.Where(c => !c.Obstacle && !occupied.Contains(c.Position))
                .SelectMany(c => c.Position.Neighbors().Select(t => new { Source = c.Position, Target = t, End = new Hex(2*t.X-c.Position.X, 2*t.Y-c.Position.Y) }))
                .First(p => catalog.Cell(p.Target)?.Obstacle == false && !occupied.Contains(p.Target) && (edge ? catalog.Cell(p.End) == null : catalog.Cell(p.End)?.Obstacle == true));
            Apply(game, 0, CommandKind.DebugTeleport, "hero:0", cell: pair.Source);
            Apply(game, 0, CommandKind.DebugTeleport, "hero:1", cell: pair.Target);
            Push(game);
            Assert.That(game.View(1).Pending?.Source, Is.EqualTo(Card));
            Assert.That(game.View(0).Events.Last(e => e.Kind == "UnitPushed").Path.Count, Is.EqualTo(1));
            Apply(game, 1, CommandKind.ForcedDiscard, game.View(1).ForcedDiscardCards.First());
            ChargeTests.Restore(catalog, game);
        }

        [TestCase(false)] [TestCase(true)]
        public void EmptyHandAfterDefenseAutomaticallyDefeatsAndVictoryStopsParent(bool victory)
        {
            var catalog = BattlefieldTests.Catalog(); var game = Setup(catalog);
            if (victory) Apply(game, 0, CommandKind.DebugSetCrystal, "1", target: 1);
            foreach (var card in game.View(1).OwnCards.Where(c => c.Zone == CardZone.InHand && c.CardId != Dodge).ToList())
                Apply(game, 0, CommandKind.DebugDiscard, card.CardId, target: 1);
            int resolved = game.View(0).Events.Count(e => e.Kind == "CardResolved" && e.CardId == Shot);
            Push(game);
            Assert.That(game.View(0).Units.Any(u => u.Seat == 1), Is.False);
            Assert.That(game.View(0).Players[0].Gold, Is.EqualTo(1));
            Assert.That(game.View(0).Events.Last(e => e.Kind == "HeroDefeated").CardId, Is.EqualTo(Card));
            Assert.That(game.View(0).Events.Last(e => e.Kind == "AttackResolved").Detail, Is.EqualTo("defended:hero:1"));
            Assert.That(game.View(0).Phase == Phase.Finished, Is.EqualTo(victory));
            Assert.That(game.View(0).Events.Count(e => e.Kind == "CardResolved" && e.CardId == Shot), Is.EqualTo(resolved + (victory ? 0 : 1)));
            ChargeTests.Restore(catalog, game);
        }

        [Test]
        public void InvalidPaymentAndDebugAreAtomicAndDuplicatePaymentIsIdempotent()
        {
            var catalog = BattlefieldTests.Catalog(); var game = Setup(catalog); Push(game);
            foreach (var command in new[] { Cmd(game, 0, CommandKind.ForcedDiscard, Shot), Cmd(game, 0, CommandKind.DeclineRetaliationDiscard), Cmd(game, 1, CommandKind.ForcedDiscard, "skip"), Cmd(game, 1, CommandKind.ForcedDiscard, Dodge), Cmd(game, 0, CommandKind.DebugTeleport, "hero:1", destination: new Hex(7,-8)) })
            {
                string before = game.ExportSave();
                Assert.That(game.Execute(command.ActorSeat, command).Accepted, Is.False);
                Assert.That(game.ExportSave(), Is.EqualTo(before));
            }
            var pay = Cmd(game, 1, CommandKind.ForcedDiscard, game.View(1).ForcedDiscardCards.First());
            Assert.That(game.Execute(1, pay).Accepted, Is.True);
            string after = game.ExportSave(); Assert.That(game.Execute(1, pay).Duplicate, Is.True);
            Assert.That(game.ExportSave(), Is.EqualTo(after)); ChargeTests.Restore(catalog, game);
        }

        [TestCase(false)] [TestCase(true)]
        public void DefenseMovementResumesAfterPaymentAndCannotMoveDefeatedHero(bool decline)
        {
            var catalog = BattlefieldTests.Catalog(); var game = Setup(catalog, defense: "tigerclaw-15-侧步");
            Attack(game); Apply(game, 1, CommandKind.Defend, "tigerclaw-15-侧步");
            Assert.That(game.View(1).Pending?.Source, Is.EqualTo(Card));
            game = ChargeTests.Restore(catalog, game);
            if (decline) Apply(game, 1, CommandKind.DeclineRetaliationDiscard);
            else
            {
                Apply(game, 1, CommandKind.ForcedDiscard, game.View(1).ForcedDiscardCards.First());
                Assert.That(game.View(1).Pending?.Source, Is.EqualTo("tigerclaw-15-侧步"));
                Apply(game, 1, CommandKind.ChooseEffectMove, "skip");
            }
            Assert.That(game.View(0).Units.Any(u => u.Seat == 1), Is.EqualTo(!decline));
            Assert.That(game.View(1).Events.Count(e => e.Kind == "DefenseResponseCompleted"), Is.EqualTo(1));
            ChargeTests.Restore(catalog, game);
        }

        [Test]
        public void DisplacementProtectionCancelsPushAndUltimateTogether()
        {
            var catalog = BattlefieldTests.Catalog(); var game = Setup(catalog, enemyBrogan: true);
            Apply(game, 0, CommandKind.DebugTeleport, "hero:3", cell: new Hex(6,-7));
            OpportuneMomentTests.AdvanceTo(game, 3);
            Apply(game, 3, CommandKind.BeginPrimary); Apply(game, 3, CommandKind.ChoosePrimaryOption, "protect");
            IronWallTests.FinishTurn(game);
            Apply(game, 0, CommandKind.DebugEquipCard, Shot, target: 0);
            string[] cards = { Shot, "tigerclaw-00-瞬闪打击", "arien-00-华丽刀锋", "brogan-00-猛攻" };
            for (int seat = 0; seat < 4; seat++) Apply(game, seat, CommandKind.SelectCard, cards[seat]);
            OpportuneMomentTests.AdvanceTo(game, 0); Push(game);
            Assert.That(game.View(0).Events.Any(e => e.Kind == "UnitDisplacementPrevented"), Is.True);
            Assert.That(game.View(0).Events.Any(e => e.Kind == "UltimateTriggered" && e.CardId == Card), Is.False);
            Assert.That(game.View(0).Units.Single(u => u.Seat == 1).Position, Is.EqualTo(new Hex(7,-8)));
            ChargeTests.Restore(catalog, game);
        }

        [Test]
        public void Frozen55CompletionKeepsExactBytesAndItsOriginalAbility()
        {
            var catalog = BattlefieldTests.Catalog();
            string save = System.IO.File.ReadAllText(System.IO.Path.Combine(ContentTests.Root(), "tests/fixtures/engine55-thunderstorm-discard.json"));
            var game = LocalGameFactory.Restore(catalog, save);
            Assert.That(game.ExportSave(), Is.EqualTo(save));
            Assert.That(game.View(0).SupportedUltimateCards, Does.Not.Contain(Card));
            Apply(game, 3, CommandKind.ForcedDiscard, "arien-13-挑战者");
            Assert.That(game.View(0).Events.Count(e => e.Kind == "UltimateCompleted"), Is.EqualTo(1));
            ChargeTests.Restore(catalog, game);
        }

        [TestCase(false)] [TestCase(true)]
        public void AttackDefeatDoesNotPushOrAddAnotherDefeatReward(bool minion)
        {
            var catalog = BattlefieldTests.Catalog(); var game = Setup(catalog);
            string target = "hero:1";
            if (minion)
            {
                target = game.View(0).Units.First(u => u.Team == Team.Red && u.Kind == "melee").Id;
                Apply(game, 0, CommandKind.DebugTeleport, target, cell: new Hex(5,-8));
            }
            Apply(game, 0, CommandKind.BeginPrimary); Apply(game, 0, CommandKind.ChooseAttackTarget, target);
            if (!minion) Apply(game, 1, CommandKind.DeclineDefense);
            Assert.That(game.View(0).Events.Any(e => e.Kind == "UltimateTriggered" && e.CardId == Card), Is.False);
            Assert.That(game.View(0).Events.Any(e => e.Kind == "UnitPushed"), Is.False);
            Assert.That(game.View(0).Units.Any(u => u.Id == target), Is.False);
            Assert.That(game.View(0).Players[0].Gold, Is.EqualTo(minion ? 2 : 1));
            ChargeTests.Restore(catalog, game);
        }
    }
}
