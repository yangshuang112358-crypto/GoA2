using System.Linq;
using Goa2.Application;
using Goa2.Domain;
using Goa2.Infrastructure;
using NUnit.Framework;
using static Goa2.Tests.SessionTests;
using static Goa2.Tests.TurnFlowTests;

namespace Goa2.Tests
{
    public sealed class SandboxTests
    {
        internal static GameSession Sandbox(bool prepare = true)
        {
            var game = LocalGameFactory.Create(FoundationTests.Fixture(), "match", new[] { "A", "B", "C", "D" }, 42, true);
            if (prepare) Apply(game, 0, CommandKind.DebugPrepare);
            return game;
        }
        [Test]
        public void SandboxQuickSelectionAllowsChangesThenRevealsWithoutConfirming()
        {
            var game = Sandbox();
            Assert.That(game.View(null).Sandbox, Is.True);
            for (int seat = 0; seat < 3; seat++) Apply(game, seat, CommandKind.SelectCard, "hero" + seat + "-gold");
            Assert.That(game.View(null).Phase, Is.EqualTo(Phase.Planning));
            Assert.That(game.View(null).Players.All(p => !p.Confirmed && p.Plays.Count == 0), Is.True);
            Apply(game, 0, CommandKind.SelectCard, "hero0-red");
            Assert.That(game.View(1).Events.Any(e => e.CardId == "hero0-red"), Is.False);
            Apply(game, 3, CommandKind.SelectCard, "hero3-gold");
            Assert.That(game.View(null).Phase, Is.EqualTo(Phase.InitiativeChoice));
            Assert.That(game.View(null).Players.Sum(p => p.Plays.Count), Is.EqualTo(4));
            Assert.That(game.View(null).Players[0].Plays.Single().Color, Is.EqualTo("red"));
        }
        [Test]
        public void FormalModeStillRequiresFourConfirmationsAndModeChangesReplay()
        {
            var game = Sandbox();
            Apply(game, 0, CommandKind.SetQuickSelection, "off");
            for (int seat = 0; seat < 4; seat++) Apply(game, seat, CommandKind.SelectCard, "hero" + seat + "-gold");
            for (int seat = 0; seat < 3; seat++) Apply(game, seat, CommandKind.ConfirmCard);
            Assert.That(game.View(null).Phase, Is.EqualTo(Phase.Planning));
            Apply(game, 3, CommandKind.ConfirmCard);
            Assert.That(game.View(null).Phase, Is.Not.EqualTo(Phase.Planning));
            Assert.That(LocalGameFactory.Restore(FoundationTests.Fixture(), game.ExportSave()).ExportSave(), Is.EqualTo(game.ExportSave()));
        }
        [Test]
        public void NormalGameRejectsDebugCommandsAndMalformedGoldIsAtomic()
        {
            var formal = NewSession(); var before = formal.ExportSave();
            Assert.That(formal.Execute(0, Cmd(formal, 0, CommandKind.DebugGold, "5", target: 0)).Code, Is.EqualTo("debug_disabled"));
            Assert.That(formal.ExportSave(), Is.EqualTo(before));
            var game = Sandbox(); before = game.ExportSave();
            foreach (string amount in new[] { "not-a-number", "-1", "10000" })
                Assert.That(game.Execute(0, Cmd(game, 0, CommandKind.DebugGold, amount, target: 2)).Accepted, Is.False);
            Assert.That(game.ExportSave(), Is.EqualTo(before));
            var command = Cmd(game, 0, CommandKind.DebugGold, "10", target: 2);
            Assert.That(game.Execute(0, command).Accepted, Is.True);
            Assert.That(game.Execute(0, command).Duplicate, Is.True);
            Assert.That(game.View(null).Players[2].Gold, Is.EqualTo(10));
        }
        [Test]
        public void DebugDiscardPublishesColorWithoutPrivateCardIdAndRecoveryRemovesDot()
        {
            var game = Sandbox();
            Apply(game, 0, CommandKind.DebugDiscard, "hero2-red", target: 2);
            var observer = game.View(null);
            Assert.That(observer.Players[2].DiscardColors, Is.EqualTo(new[] { "red" }));
            Assert.That(observer.Events.Any(e => e.CardId == "hero2-red"), Is.False);
            Assert.That(observer.Players[2].Plays, Is.Empty);
            Assert.That(game.View(2).Events.Any(e => e.CardId == "hero2-red"), Is.True);
            Apply(game, 0, CommandKind.DebugRecover, "hero2-red", target: 2);
            Assert.That(game.View(null).Players[2].DiscardColors, Is.Empty);
        }
        [Test]
        public void DebugTeleportUsesFreeMapCellsAndChangesOnlyChosenUnit()
        {
            var game = Sandbox(); var before = game.ExportSave();
            Assert.That(game.Execute(0, Cmd(game, 0, CommandKind.DebugTeleport, "hero:0", destination: new Hex(99, 99))).Accepted, Is.False);
            var occupied = game.View(null).Units.Single(u => u.Seat == 1).Position;
            Assert.That(game.Execute(0, Cmd(game, 0, CommandKind.DebugTeleport, "hero:0", destination: occupied)).Accepted, Is.False);
            Assert.That(game.ExportSave(), Is.EqualTo(before));
            Apply(game, 0, CommandKind.DebugTeleport, "hero:0", cell: new Hex(-2, 0));
            Assert.That(game.View(null).Units.Single(u => u.Seat == 0).Position, Is.EqualTo(new Hex(-2, 0)));
            Assert.That(game.View(null).Units.Single(u => u.Seat == 1).Position, Is.EqualTo(occupied));
            Assert.That(LocalGameFactory.Restore(FoundationTests.Fixture(), game.ExportSave()).ExportSave(), Is.EqualTo(game.ExportSave()));
        }
        [Test]
        public void PlayedTurnDotsRemainAfterCardLeavesPlayedZone()
        {
            var game = Sandbox(); Apply(game, 0, CommandKind.DebugSelectAll);
            while (game.View(null).Turn == 1)
            {
                var view = game.View(null);
                if (view.Pending != null) Apply(game, view.Pending.ChooserSeat, CommandKind.ChooseInitiative, target: view.Pending.CandidateSeats[0]);
                else Apply(game, view.ActiveSeat!.Value, CommandKind.Pass);
            }
            var prior = game.View(null).Players[0].Plays.Single();
            Assert.That(prior.Round, Is.EqualTo(1)); Assert.That(prior.Turn, Is.EqualTo(1));
            Assert.That(game.View(null).Turn, Is.EqualTo(2));
            var copy = new JsonStateCodec().Read(game.ExportSave());
            copy.Players[0].Cards.Single(c => c.CardId == prior.CardId).Zone = CardZone.InHand;
            var recovered = new GameSession(FoundationTests.Fixture(), new JsonStateCodec(), copy);
            Assert.That(recovered.View(null).Players[0].Plays.Single().CardId, Is.EqualTo(prior.CardId));
        }
    }
}
