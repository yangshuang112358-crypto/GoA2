using System.Linq;
using Goa2.Application;
using Goa2.Domain;
using Goa2.Infrastructure;
using NUnit.Framework;
using static Goa2.Tests.SessionTests;
using static Goa2.Tests.TurnFlowTests;

namespace Goa2.Tests
{
    public sealed class HeroDefeatTests
    {
        [TestCase(1,1)][TestCase(2,1)][TestCase(3,1)][TestCase(4,2)]
        [TestCase(5,2)][TestCase(6,2)][TestCase(7,3)][TestCase(8,3)]
        public void RewardsAndCrystalLossUseVictimLevel(int level, int assist)
        {
            var catalog = BattlefieldTests.Catalog(); var original = BattlefieldTests.Ready(catalog);
            // A focused level fixture; full command replay is covered once progression creates these levels.
            var state = new JsonStateCodec().Read(original.ExportSave()); state.Players[1].Level=level; state.Players[1].Gold=10; state.RedCrystal=99;
            var game = new GameSession(catalog,new JsonStateCodec(),state);
            Apply(game,0,CommandKind.DebugDefeatHero,"hero:1",target:0);
            var view = game.View(null);
            Assert.That(view.Players[0].Gold, Is.EqualTo(level)); Assert.That(view.Players[2].Gold, Is.EqualTo(assist));
            Assert.That(view.Players[1].Gold, Is.EqualTo(10)); Assert.That(view.Players[3].Gold, Is.Zero);
            Assert.That(view.RedCrystal, Is.EqualTo(99-level)); Assert.That(view.Players[1].AwaitingRespawn, Is.True);
            string before = game.ExportSave();
            Assert.That(game.Execute(0,Cmd(game,0,CommandKind.DebugDefeatHero,"hero:1",target:0)).Accepted, Is.False);
            Assert.That(game.ExportSave(), Is.EqualTo(before));
        }
        [Test]
        public void DefeatingAHighLevelHeroCanImmediatelyEndTheMatch()
        {
            var catalog = BattlefieldTests.Catalog(); var initial = BattlefieldTests.Ready(catalog);
            var state = new JsonStateCodec().Read(initial.ExportSave()); state.Players[1].Level=8;
            var game = new GameSession(catalog,new JsonStateCodec(),state);
            string before = game.ExportSave();
            Assert.That(game.Execute(0,Cmd(game,0,CommandKind.DebugDefeatHero,"hero:2",target:0)).Accepted, Is.False);
            Assert.That(game.ExportSave(), Is.EqualTo(before));
            Apply(game,0,CommandKind.DebugDefeatHero,"hero:1",target:0);
            var view = game.View(null);
            Assert.That(view.Winner, Is.EqualTo(Team.Blue)); Assert.That(view.RedCrystal, Is.EqualTo(-1));
            Assert.That(view.Phase, Is.EqualTo(Phase.Finished)); Assert.That(view.ActiveSeat, Is.Null); Assert.That(view.Pending, Is.Null);
            Assert.That(view.Units.Count(u => u.Kind == "hero"), Is.EqualTo(3));
        }
    }
}
