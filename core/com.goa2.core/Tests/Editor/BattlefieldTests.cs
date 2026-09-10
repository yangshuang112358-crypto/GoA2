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
    public sealed class BattlefieldTests
    {
        internal static ContentCatalog Catalog() => ContentLoader.LoadDirectory(ContentTests.Root());
        internal static GameSession Ready(ContentCatalog catalog, int engineVersion = GameState.CurrentEngineVersion)
        {
            var game = LocalGameFactory.Create(catalog, "battle", new[] { "A", "B", "C", "D" }, 42, true, engineVersion);
            Apply(game, 0, CommandKind.DebugPrepare, "wasp,shargatha,brogan,arien");
            return game;
        }
        internal static string Heavy(GameSession game, Team team) => game.View(null).Units.Single(u => u.Kind == "heavy" && u.Team == team).Id;
        [Test]
        public void ProtectionUsesAllOtherFriendlyMinionsButDebugCanBypass()
        {
            var catalog = Catalog(); var game = Ready(catalog); var codec = new JsonStateCodec(); var state = codec.Read(game.ExportSave());
            string heavy = Heavy(game, Team.Red);
            Assert.That(GameRules.LegalMinionRemovals(state), Does.Not.Contain(heavy));
            Assert.That(GameRules.LegalMinionRemovals(state, true), Does.Contain(heavy));
            foreach (var other in state.Units.Where(u => u.Team == Team.Red && u.Kind != "hero" && u.Kind != "heavy").ToList())
                Apply(game, 0, CommandKind.DebugRemoveMinion, other.Id);
            Assert.That(GameRules.LegalMinionRemovals(codec.Read(game.ExportSave())), Does.Contain(heavy));
            Assert.That(game.View(null).Players.All(p => p.Gold == 0), Is.True);
        }
        [Test]
        public void DefeatPaysCorrectGoldAndFriendlyOrInvalidTargetsAreAtomic()
        {
            var catalog = Catalog(); var game = Ready(catalog);
            string before = game.ExportSave();
            foreach (string id in new[] { "absent", "hero:1", Heavy(game, Team.Blue) })
                Assert.That(game.Execute(0, Cmd(game, 0, CommandKind.DebugDefeatMinion, id, target: 0)).Accepted, Is.False);
            Assert.That(game.ExportSave(), Is.EqualTo(before));
            var red = game.View(null).Units.Where(u => u.Team == Team.Red && u.Kind != "hero").ToList();
            foreach (string kind in new[] { "melee", "ranged", "heavy" })
                Apply(game, 0, CommandKind.DebugDefeatMinion, red.First(u => u.Kind == kind).Id, target: 0);
            Assert.That(game.View(null).Players[0].Gold, Is.EqualTo(8));
            Assert.That(game.View(null).CombatRegion, Is.EqualTo("redNear"));
            Assert.That(LocalGameFactory.Restore(catalog, game.ExportSave()).ExportSave(), Is.EqualTo(game.ExportSave()));
        }
        [TestCase(Team.Red, "redNear", 5, 6, 1, 0)]
        [TestCase(Team.Blue, "blueNear", 6, 5, 0, 1)]
        public void RemovingHeavyImmediatelyAdvancesAndUsesActualAsymmetricSpawns(Team removed, string region, int blue, int red, int blueMarks, int redMarks)
        {
            var catalog = Catalog(); var game = Ready(catalog); var heroes = game.View(null).Units.Where(u => u.Kind == "hero").Select(u => u.Position).ToArray();
            Apply(game, 0, CommandKind.DebugRemoveMinion, Heavy(game, removed));
            var view = game.View(null);
            Assert.That(view.CombatRegion, Is.EqualTo(region));
            Assert.That(view.Units.Count(u => u.Kind != "hero" && u.Team == Team.Blue), Is.EqualTo(blue));
            Assert.That(view.Units.Count(u => u.Kind != "hero" && u.Team == Team.Red), Is.EqualTo(red));
            Assert.That(view.Units.Where(u => u.Kind == "hero").Select(u => u.Position), Is.EqualTo(heroes));
            Assert.That(view.BlueMarks, Is.EqualTo(blueMarks)); Assert.That(view.RedMarks, Is.EqualTo(redMarks));
            Assert.That(view.Players.All(p => p.Gold == 0), Is.True); Assert.That(view.Phase, Is.EqualTo(Phase.Planning));
            Assert.That(view.Units.Select(u => u.Id).Distinct().Count(), Is.EqualTo(view.Units.Count));
        }
        [Test]
        public void MarkVictoryIsCumulativeAndChecksBeforeSpawning()
        {
            var catalog = Catalog(); var game = Ready(catalog);
            foreach (Team defeated in new[] { Team.Red, Team.Blue, Team.Red, Team.Blue, Team.Red })
                Apply(game, 0, CommandKind.DebugRemoveMinion, Heavy(game, defeated));
            var view = game.View(null);
            Assert.That(view.Winner, Is.EqualTo(Team.Blue)); Assert.That(view.VictoryReason, Is.EqualTo("frontline_marks"));
            Assert.That(view.BlueMarks, Is.EqualTo(3)); Assert.That(view.RedMarks, Is.EqualTo(2));
            Assert.That(view.CombatRegion, Is.EqualTo("redNear")); Assert.That(view.Units.All(u => u.Kind == "hero"), Is.True);
            Assert.That(view.Phase, Is.EqualTo(Phase.Finished));
            string before = game.ExportSave();
            Assert.That(game.Execute(0, Cmd(game, 0, CommandKind.DebugGold, "1", target: 0)).Code, Is.EqualTo("match_finished"));
            Assert.That(game.ExportSave(), Is.EqualTo(before));
        }
        [TestCase(Team.Red, Team.Blue, "redFountain")]
        [TestCase(Team.Blue, Team.Red, "blueFountain")]
        public void ReachingFountainWinsBeforeThirdMark(Team removed, Team winner, string region)
        {
            var catalog = Catalog(); var game = Ready(catalog);
            Apply(game, 0, CommandKind.DebugRemoveMinion, Heavy(game, removed));
            Apply(game, 0, CommandKind.DebugRemoveMinion, Heavy(game, removed));
            var view = game.View(null);
            Assert.That(view.Winner, Is.EqualTo(winner)); Assert.That(view.VictoryReason, Is.EqualTo("fountain"));
            Assert.That(view.CombatRegion, Is.EqualTo(region)); Assert.That(view.Units.Count, Is.EqualTo(4));
            Assert.That(LocalGameFactory.Restore(catalog, game.ExportSave()).ExportSave(), Is.EqualTo(game.ExportSave()));
        }
        [Test]
        public void OccupiedSpawnRequiresCorrectCaptainAndRestoresInterruptedInitiative()
        {
            var catalog = Catalog(); var game = Ready(catalog);
            var spawn = catalog.Cells.First(c => c.Region == "redNear" && c.Spawn == "redRangedSpawn");
            Apply(game, 0, CommandKind.DebugTeleport, "hero:0", cell: spawn.Position);
            Apply(game, 0, CommandKind.DebugSelectAll);
            while (game.View(null).Phase != Phase.InitiativeChoice)
            {
                var active = game.View(null).ActiveSeat;
                Assert.That(active.HasValue, Is.True); Apply(game, active!.Value, CommandKind.Pass);
            }
            var prior = game.View(null).Pending!;
            Apply(game, 0, CommandKind.DebugRemoveMinion, Heavy(game, Team.Red));
            var view = game.View(1);
            Assert.That(view.Phase, Is.EqualTo(Phase.EffectChoice)); Assert.That(view.Pending!.Kind, Is.EqualTo("minion_spawn"));
            Assert.That(view.Pending.ChooserSeat, Is.EqualTo(1)); Assert.That(view.Pending.Source, Is.EqualTo("debug"));
            Assert.That(view.Pending.CandidateCells.All(h => h.Distance(spawn.Position) == 1), Is.True);
            Assert.That(view.Pending.CandidateCells, Is.Not.Empty);
            string saved = game.ExportSave(); game = LocalGameFactory.Restore(catalog, saved);
            var choice = view.Pending.CandidateCells.First();
            Assert.That(game.Execute(0, Cmd(game, 0, CommandKind.ChooseMinionSpawn, destination: choice)).Accepted, Is.False);
            Assert.That(game.Execute(1, Cmd(game, 1, CommandKind.ChooseMinionSpawn, destination: spawn.Position)).Accepted, Is.False);
            Assert.That(game.ExportSave(), Is.EqualTo(saved));
            Apply(game, 1, CommandKind.ChooseMinionSpawn, cell: choice);
            view = game.View(null);
            Assert.That(view.Units.Any(u => u.Team == Team.Red && u.Kind == "ranged" && u.Position == choice), Is.True);
            Assert.That(view.Phase, Is.EqualTo(Phase.InitiativeChoice));
            Assert.That(view.Pending!.Id, Is.EqualTo(prior.Id)); Assert.That(view.Pending.CandidateSeats, Is.EqualTo(prior.CandidateSeats));
            Assert.That(LocalGameFactory.Restore(catalog, game.ExportSave()).ExportSave(), Is.EqualTo(game.ExportSave()));
        }
        [TestCase(0, Team.Red)]
        [TestCase(1, Team.Blue)]
        public void ZeroCrystalTriggersVictoryWithoutGold(int target, Team winner)
        {
            var catalog = Catalog(); var game = Ready(catalog); string before = game.ExportSave();
            Assert.That(game.Execute(0, Cmd(game, 0, CommandKind.DebugSetCrystal, "-1", target: target)).Accepted, Is.False);
            Assert.That(game.ExportSave(), Is.EqualTo(before));
            Apply(game, 0, CommandKind.DebugSetCrystal, "0", target: target);
            Assert.That(game.View(null).Winner, Is.EqualTo(winner)); Assert.That(game.View(null).VictoryReason, Is.EqualTo("crystal"));
            Assert.That(game.View(null).Players.All(p => p.Gold == 0), Is.True);
        }
        [Test]
        public void FullyBlockedSpawnKeepsTaskAcrossRestoreAndCanBeFreedInSandbox()
        {
            var catalog = Catalog(); var game = Ready(catalog);
            var positions = new[] { new Hex(2,-7), new Hex(1,-7), new Hex(2,-8), new Hex(3,-8) };
            var original = game.View(null).Units.Single(u => u.Seat == 0).Position;
            for (int seat = 0; seat < 4; seat++) Apply(game, 0, CommandKind.DebugTeleport, "hero:" + seat, cell: positions[seat]);
            Apply(game, 0, CommandKind.DebugRemoveMinion, Heavy(game, Team.Red));
            Assert.That(game.View(1).Pending!.Kind, Is.EqualTo("minion_spawn"));
            Assert.That(game.View(1).Pending!.CandidateCells, Is.Empty);
            game = LocalGameFactory.Restore(catalog, game.ExportSave());
            string before = game.ExportSave();
            Assert.That(game.Execute(1, Cmd(game, 1, CommandKind.ChooseMinionSpawn, destination: positions[0])).Accepted, Is.False);
            Assert.That(game.Execute(0, Cmd(game, 0, CommandKind.DebugAdvance, "turn")).Accepted, Is.False);
            Assert.That(game.ExportSave(), Is.EqualTo(before));
            Apply(game, 0, CommandKind.DebugTeleport, "hero:0", cell: original);
            Assert.That(game.View(1).Pending!.CandidateCells, Is.EqualTo(new[] { positions[0] }));
            Apply(game, 1, CommandKind.ChooseMinionSpawn, cell: positions[0]);
            Assert.That(game.View(null).Phase, Is.EqualTo(Phase.Planning));
            Assert.That(game.View(null).Units.Count, Is.EqualTo(15));
        }
        [Test]
        public void LegacyConflictingSpawnsPreserveTheirHistoricalPauseDuringReplay()
        {
            var catalog = Catalog(); var game = Ready(catalog, 0);
            Apply(game, 0, CommandKind.DebugTeleport, "hero:0", cell: new Hex(2,-7));
            Apply(game, 0, CommandKind.DebugTeleport, "hero:2", cell: new Hex(3,-7));
            Apply(game, 0, CommandKind.DebugRemoveMinion, Heavy(game, Team.Red));
            Assert.That(game.View(null).Pending!.Kind, Is.EqualTo("spawn_order_unresolved"));
            Assert.That(game.View(null).Pending!.CandidateCells, Is.Empty);
            var state = new JsonStateCodec().Read(game.ExportSave());
            Assert.That(state.Frontline!.Remaining.Count, Is.EqualTo(2));
            Assert.That(state.Units.Count(u => u.Kind != "hero"), Is.EqualTo(9));
            Assert.That(LocalGameFactory.Restore(catalog, game.ExportSave()).ExportSave(), Is.EqualTo(game.ExportSave()));
        }
        [Test]
        public void FormalGameRejectsBattlefieldDebugCommands()
        {
            var game = NewSession(); string before = game.ExportSave();
            foreach (var kind in new[] { CommandKind.DebugRemoveMinion, CommandKind.DebugDefeatMinion, CommandKind.DebugSetCrystal })
                Assert.That(game.Execute(0, Cmd(game, 0, kind, "0", target: 0)).Code, Is.EqualTo("debug_disabled"));
            Assert.That(game.ExportSave(), Is.EqualTo(before));
        }
    }
}
