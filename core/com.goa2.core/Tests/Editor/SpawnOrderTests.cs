using System.Linq;
using Goa2.Domain;
using Goa2.Infrastructure;
using NUnit.Framework;
using static Goa2.Tests.BattlefieldTests;
using static Goa2.Tests.TurnFlowTests;

namespace Goa2.Tests
{
    public sealed class SpawnOrderTests
    {
        [Test]
        public void CaptainChoosesWhichOwnMinionSpawnsFirst()
        {
            var catalog = Catalog(); var game = Ready(catalog);
            Apply(game, 0, CommandKind.DebugTeleport, "hero:0", cell: new Hex(2,-7));
            Apply(game, 0, CommandKind.DebugTeleport, "hero:2", cell: new Hex(3,-7));
            Apply(game, 0, CommandKind.DebugRemoveMinion, Heavy(game, Team.Red));
            var pending = game.View(1).Pending!;
            Assert.That(pending.Kind, Is.EqualTo("minion_spawn")); Assert.That(pending.CandidateUnits.Count, Is.EqualTo(2));
            Assert.That(pending.ChooserSeat, Is.EqualTo(1));
            Apply(game, 1, CommandKind.ChooseMinionSpawn, "minion:1:3,-7", cell: new Hex(3,-8));
            Assert.That(game.View(null).Units.Any(u => u.Id == "minion:1:3,-7" && u.Position == new Hex(3,-8)), Is.True);
            Assert.That(game.View(null).Pending!.CandidateUnits, Is.EqualTo(new[] { "minion:1:2,-7" }));
            Assert.That(game.View(null).DecisionCoin, Is.EqualTo(Team.Blue));
            Assert.That(LocalGameFactory.Restore(catalog, game.ExportSave()).ExportSave(), Is.EqualTo(game.ExportSave()));
        }
        [TestCase(Team.Blue, 0, "minion:1:2,-6")]
        [TestCase(Team.Red, 1, "minion:1:3,-7")]
        public void CrossTeamSpawnOrderUsesCoinWithoutFlipping(Team coin, int chooser, string unit)
        {
            var catalog = Catalog(); var game = Ready(catalog);
            Apply(game, 0, CommandKind.DebugSetCoin, coin == Team.Blue ? "blue" : "red");
            Apply(game, 0, CommandKind.DebugTeleport, "hero:0", cell: new Hex(3,-7));
            Apply(game, 0, CommandKind.DebugTeleport, "hero:2", cell: new Hex(2,-6));
            Apply(game, 0, CommandKind.DebugRemoveMinion, Heavy(game, Team.Red));
            Assert.That(game.View(null).Pending!.Kind, Is.EqualTo("minion_spawn"));
            Assert.That(game.View(null).Pending!.ChooserSeat, Is.EqualTo(chooser));
            Assert.That(game.View(null).Pending!.CandidateUnits, Is.EqualTo(new[] { unit }));
            Apply(game, chooser, CommandKind.ChooseMinionSpawn, unit, cell: new Hex(3,-6));
            Assert.That(game.View(null).DecisionCoin, Is.EqualTo(coin));
            Assert.That(game.View(null).Pending!.ChooserSeat, Is.EqualTo(1-chooser));
            Assert.That(game.View(null).Pending!.CandidateCells, Has.No.Member(new Hex(3,-6)));
            Assert.That(LocalGameFactory.Restore(catalog, game.ExportSave()).ExportSave(), Is.EqualTo(game.ExportSave()));
        }
    }
}
