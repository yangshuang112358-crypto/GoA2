using System.Linq;
using Goa2.Application;
using Goa2.Domain;
using Goa2.Infrastructure;
using NUnit.Framework;
using static Goa2.Tests.SessionTests;
using static Goa2.Tests.TurnFlowTests;

namespace Goa2.Tests
{
    public sealed class RecoveryTests
    {
        [Test]
        public void SaveTamperingCannotInjectGoldOrLosePendingCandidate()
        {
            var session = Planning(); Reveal(session);
            var codec = new JsonStateCodec();
            var changed = codec.Read(session.ExportSave()); changed.Players[0].Gold = 999;
            Assert.Throws<RuleViolation>(() => LocalGameFactory.Restore(FoundationTests.Fixture(), codec.Write(changed)));
            changed = codec.Read(session.ExportSave()); changed.Pending!.CandidateSeats.RemoveAt(0);
            Assert.Throws<RuleViolation>(() => LocalGameFactory.Restore(FoundationTests.Fixture(), codec.Write(changed)));
        }
        [Test]
        public void AcceptedCommandReplayRestoresPendingAndResumesSameResult()
        {
            var session = Planning(); Reveal(session);
            var restored = LocalGameFactory.Restore(FoundationTests.Fixture(), session.ExportSave());
            var command = Cmd(session, 0, CommandKind.ChooseInitiative, target: 2);
            Assert.That(session.Execute(0, command).Accepted, Is.True);
            Assert.That(restored.Execute(0, command).Accepted, Is.True);
            Assert.That(restored.ExportSave(), Is.EqualTo(session.ExportSave()));
        }
        [Test]
        public void CallerCannotChangeRulesThroughOriginalContentReference()
        {
            var content = FoundationTests.Fixture(); var session = Planning(content);
            content.Cards.Clear();
            Assert.DoesNotThrow(() => Reveal(session));
        }
        [Test]
        public void SameTeamTieDoesNotFlipCoin()
        {
            var content = FoundationTests.Fixture();
            content.Card("hero1-gold").Initiative = 2;
            content.Card("hero3-gold").Initiative = 1;
            var session = Planning(content); Reveal(session);
            Assert.That(session.View(0).Pending!.CandidateSeats, Is.EqualTo(new[] { 0, 2 }));
            Assert.That(session.View(0).DecisionCoin, Is.EqualTo(Team.Blue));
        }
        [Test]
        public void InitiativeIsRecomputedAfterEachCompletedCard()
        {
            var session = Planning(); Reveal(session);
            Apply(session, 0, CommandKind.ChooseInitiative, target: 0);
            var codec = new JsonStateCodec(); var snapshot = codec.Read(session.ExportSave());
            snapshot.Players[3].InitiativeBonus = 3;
            var modified = new GameSession(FoundationTests.Fixture(), codec, snapshot);
            Apply(modified, 0, CommandKind.Pass);
            Assert.That(modified.View(0).ActiveSeat, Is.EqualTo(3));
            Assert.That(modified.View(0).Pending, Is.Null);
        }
        [Test]
        public void EmptyHandsAutoSkipWithoutFakeCardAndAllEmptyTurnsReachRoundEnd()
        {
            var session = Planning(); Reveal(session);
            Apply(session, 0, CommandKind.ChooseInitiative, target: 0);
            var codec = new JsonStateCodec(); var snapshot = codec.Read(session.ExportSave());
            foreach (var player in snapshot.Players)
                foreach (var card in player.Cards)
                    if (card.Zone == CardZone.InHand || (player.Seat != 0 && card.Zone == CardZone.PlayedUnresolved)) card.Zone = CardZone.Discarded;
            var modified = new GameSession(FoundationTests.Fixture(), codec, snapshot);
            Apply(modified, 0, CommandKind.Pass);
            Assert.That(modified.View(0).Phase, Is.EqualTo(Phase.RoundEnd));
            Assert.That(modified.View(0).Events.Count(e => e.Kind == "EmptyHandSkipped"), Is.EqualTo(12));
        }
        [Test]
        public void OneEmptySeatDoesNotBlockRemainingThreePlayersFromRevealing()
        {
            var codec = new JsonStateCodec();
            var snapshot = codec.Read(Deployment().ExportSave());
            foreach (var card in snapshot.Players[3].Cards) card.Zone = CardZone.Discarded;
            var game = new GameSession(FoundationTests.Fixture(), codec, snapshot);
            Apply(game, 0, CommandKind.DeployHero, target: 0, cell: new Hex(-4, -1));
            Apply(game, 0, CommandKind.DeployHero, target: 2, cell: new Hex(-4, 1));
            Apply(game, 1, CommandKind.DeployHero, target: 1, cell: new Hex(4, -1));
            Apply(game, 1, CommandKind.DeployHero, target: 3, cell: new Hex(4, 1));
            Assert.That(game.View(3).Players[3].Confirmed, Is.True);
            for (int seat = 0; seat < 3; seat++)
            {
                Apply(game, seat, CommandKind.SelectCard, "hero" + seat + "-gold");
                Apply(game, seat, CommandKind.ConfirmCard);
            }
            Assert.That(game.View(null).Phase, Is.Not.EqualTo(Phase.Planning));
            Assert.That(game.View(null).Players.Sum(player => player.Revealed.Count), Is.EqualTo(3));
            Assert.That(game.View(null).Events.Any(entry => entry.Kind == "CardRevealed" && entry.Seat == 3), Is.False);
        }
    }
}
