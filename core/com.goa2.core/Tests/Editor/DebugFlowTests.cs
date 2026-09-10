using System.Linq;
using Goa2.Domain;
using Goa2.Infrastructure;
using NUnit.Framework;
using static Goa2.Tests.SessionTests;
using static Goa2.Tests.TurnFlowTests;
using static Goa2.Tests.SandboxTests;

namespace Goa2.Tests
{
    public sealed class DebugFlowTests
    {
        [Test]
        public void FastForwardCannotDiscardAnUnrelatedMandatoryChoice()
        {
            var original = Sandbox(); Apply(original, 0, CommandKind.DebugSelectAll);
            var codec = new JsonStateCodec(); var state = codec.Read(original.ExportSave());
            state.Phase = Phase.Action; state.ActiveSeat = 0;
            state.Pending = new PendingChoice { Kind = "card_choice", ChooserSeat = 0, Source = "future-effect", ResumeAt = "resolve" };
            var game = new Goa2.Application.GameSession(FoundationTests.Fixture(), codec, state);
            string before = game.ExportSave();
            Assert.That(game.Execute(0, Cmd(game, 0, CommandKind.DebugAdvance, "round")).Code, Is.EqualTo("wrong_phase"));
            Assert.That(game.ExportSave(), Is.EqualTo(before));
        }
        [Test]
        public void ConfirmAllRequiresSelectionsAndNeverPartiallyLocksAPlayer()
        {
            var game = Sandbox(); Apply(game, 0, CommandKind.SetQuickSelection, "off");
            Apply(game, 0, CommandKind.SelectCard, "hero0-gold");
            string before = game.ExportSave();
            Assert.That(game.Execute(0, Cmd(game, 0, CommandKind.DebugConfirmAll)).Code, Is.EqualTo("missing_selection"));
            Assert.That(game.ExportSave(), Is.EqualTo(before));
            Apply(game, 0, CommandKind.DebugSelectAll);
            Apply(game, 2, CommandKind.DebugConfirmAll);
            Assert.That(game.View(null).Players.All(p => p.Confirmed && p.Plays.Count == 1), Is.True);
            Assert.That(game.View(null).Phase, Is.EqualTo(Phase.InitiativeChoice));
        }
        [Test]
        public void SkipOneActionResolvesCaptainChoiceThenPassesOnlyOneCard()
        {
            var game = Sandbox(); string before = game.ExportSave();
            Assert.That(game.Execute(0, Cmd(game, 0, CommandKind.DebugAdvance, "action")).Code, Is.EqualTo("wrong_phase"));
            Assert.That(game.ExportSave(), Is.EqualTo(before));
            Apply(game, 0, CommandKind.DebugSelectAll);
            Assert.That(game.View(null).Pending, Is.Not.Null);
            Apply(game, 3, CommandKind.DebugAdvance, "action");
            var state = new JsonStateCodec().Read(game.ExportSave());
            Assert.That(state.Players.SelectMany(p => p.Cards).Count(c => c.Zone == CardZone.PlayedResolved), Is.EqualTo(1));
            Assert.That(state.Events.Count(e => e.Kind == "ActionPassed"), Is.EqualTo(1));
            Assert.That(state.Events.Any(e => e.Kind == "InitiativeChosen" && e.Seat == 0), Is.True);
            Assert.That(state.Turn, Is.EqualTo(1));
        }
        [Test]
        public void SkipTurnAndRoundUseNormalRevealPassAndStopBeforeRoundSettlement()
        {
            var game = Sandbox(); Apply(game, 0, CommandKind.SetQuickSelection, "off");
            var unitsBefore = game.View(null).Units.Select(u => u.Id+":"+u.Position).ToList();
            long revision = game.View(null).Revision;
            Apply(game, 0, CommandKind.DebugAdvance, "turn");
            Assert.That(game.View(null).Revision, Is.EqualTo(revision+1));
            Assert.That(game.View(null).Turn, Is.EqualTo(2));
            Assert.That(game.View(null).Phase, Is.EqualTo(Phase.Planning));
            Assert.That(game.View(null).Players.Sum(p => p.Plays.Count), Is.EqualTo(4));
            var command = Cmd(game, 1, CommandKind.DebugAdvance, "round");
            Assert.That(game.Execute(1, command).Accepted, Is.True);
            Assert.That(game.Execute(1, command).Duplicate, Is.True);
            var view = game.View(null);
            Assert.That(view.Phase, Is.EqualTo(Phase.RoundEnd)); Assert.That(view.Round, Is.EqualTo(1));
            Assert.That(view.Players.Sum(p => p.HandCount), Is.EqualTo(4));
            Assert.That(view.Players.Sum(p => p.Plays.Count), Is.EqualTo(16));
            Assert.That(view.Players.All(p => p.Gold == 0), Is.True);
            Assert.That(view.Units.Select(u => u.Id+":"+u.Position), Is.EqualTo(unitsBefore));
            Assert.That(LocalGameFactory.Restore(FoundationTests.Fixture(), game.ExportSave()).ExportSave(), Is.EqualTo(game.ExportSave()));
            string before = game.ExportSave();
            Assert.That(game.Execute(0, Cmd(game, 0, CommandKind.DebugAdvance, "round")).Accepted, Is.False);
            Assert.That(game.ExportSave(), Is.EqualTo(before));
        }
        [Test]
        public void SetGoldHasExactBoundsAndAllNewDebugCommandsRequireSandbox()
        {
            var game = Sandbox(); Apply(game, 1, CommandKind.DebugSetGold, "9999", target: 3);
            Assert.That(game.View(null).Players[3].Gold, Is.EqualTo(9999));
            Apply(game, 1, CommandKind.DebugSetGold, "0", target: 3);
            Assert.That(game.View(null).Players[3].Gold, Is.EqualTo(0));
            string before = game.ExportSave();
            foreach (string value in new[] { "-1", "10000", "2147483648", "1.5" })
                Assert.That(game.Execute(1, Cmd(game, 1, CommandKind.DebugSetGold, value, target: 3)).Accepted, Is.False);
            Assert.That(game.ExportSave(), Is.EqualTo(before));
            var formal = NewSession();
            foreach (var kind in new[] { CommandKind.DebugConfirmAll, CommandKind.DebugAdvance, CommandKind.DebugSetGold })
                Assert.That(formal.Execute(0, Cmd(formal, 0, kind)).Code, Is.EqualTo("debug_disabled"));
        }
    }
}
