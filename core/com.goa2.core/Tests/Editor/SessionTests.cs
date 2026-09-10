using System;
using Goa2.Application;
using Goa2.Domain;
using Goa2.Infrastructure;
using Goa2.Rules;
using NUnit.Framework;

namespace Goa2.Tests
{
    public sealed class SessionTests
    {
        internal static GameSession NewSession(ContentCatalog? content = null)
        {
            var catalog = content ?? FoundationTests.Fixture();
            return new GameSession(catalog, new JsonStateCodec(), new GameRules().Create(catalog, "match", new[] { "A", "B", "C", "D" }, 42));
        }
        internal static Command Cmd(GameSession session, int seat, CommandKind kind, string value = "", int target = -1, Hex destination = default, MoveMode mode = MoveMode.Secondary) =>
            new Command { Id = Guid.NewGuid().ToString("N"), MatchId = session.View(null).MatchId, ExpectedRevision = session.View(seat).Revision, ActorSeat = seat, Kind = kind, Value = value, TargetSeat = target, Destination = destination, MoveMode = mode };
        [Test]
        public void InvalidIdentityAndStaleCommandsLeaveWholeStateUnchanged()
        {
            var session = NewSession(); string before = session.ExportSave();
            var command = Cmd(session, 0, CommandKind.ChooseHero, "hero0");
            Assert.That(session.Execute(1, command).Code, Is.EqualTo("unauthorized"));
            Assert.That(session.ExportSave(), Is.EqualTo(before));
            command.ExpectedRevision = 3;
            Assert.That(session.Execute(0, command).Code, Is.EqualTo("stale_revision"));
            Assert.That(session.ExportSave(), Is.EqualTo(before));
        }
        [Test]
        public void DuplicateIsIdempotentAndConflictIsRejectedAfterRestore()
        {
            var session = NewSession(); var command = Cmd(session, 0, CommandKind.ChooseHero, "hero0");
            Assert.That(session.Execute(0, command).Accepted, Is.True);
            string saved = session.ExportSave();
            var restored = new GameSession(FoundationTests.Fixture(), new JsonStateCodec(), new JsonStateCodec().Read(saved));
            Assert.That(restored.Execute(0, command).Duplicate, Is.True);
            Assert.That(restored.ExportSave(), Is.EqualTo(saved));
            command.Value = "hero1";
            Assert.That(restored.Execute(0, command).Code, Is.EqualTo("command_id_conflict"));
            Assert.That(restored.ExportSave(), Is.EqualTo(saved));
        }
        [Test]
        public void InvalidRuleAndMutatedViewCannotChangeAuthority()
        {
            var session = NewSession(); var first = Cmd(session, 0, CommandKind.ChooseHero, "hero0");
            Assert.That(session.Execute(0, first).Accepted, Is.True);
            string before = session.ExportSave();
            Assert.That(session.Execute(1, Cmd(session, 1, CommandKind.ChooseHero, "hero0")).Code, Is.EqualTo("hero_taken"));
            var view = session.View(0); view.OwnCards.Clear(); view.Players.Clear(); view.Events.Clear();
            Assert.That(session.ExportSave(), Is.EqualTo(before));
        }
        [Test]
        public void SaveRejectsDifferentContentAndDuplicateJsonKeys()
        {
            var session = NewSession(); var codec = new JsonStateCodec(); var saved = codec.Read(session.ExportSave());
            saved.ContentHash = "changed";
            Assert.Throws<RuleViolation>(() => new GameSession(FoundationTests.Fixture(), codec, saved));
            Assert.Throws<Newtonsoft.Json.JsonReaderException>(() => codec.Read("{\"Revision\":1,\"Revision\":2}"));
        }
    }
}
