using System.Linq;
using Goa2.Application;
using Goa2.Domain;
using Goa2.Infrastructure;
using NUnit.Framework;
using static Goa2.Tests.SessionTests;

namespace Goa2.Tests
{
    public sealed class TurnFlowTests
    {
        internal static GameSession Deployment(ContentCatalog? catalog = null)
        {
            var session = NewSession(catalog);
            for (int seat = 0; seat < 4; seat++) Apply(session, seat, CommandKind.ChooseHero, "hero" + seat);
            return session;
        }
        internal static GameSession Planning(ContentCatalog? catalog = null)
        {
            var session = Deployment(catalog);
            Apply(session, 0, CommandKind.DeployHero, target: 0, cell: new Hex(-4, -1));
            Apply(session, 0, CommandKind.DeployHero, target: 2, cell: new Hex(-4, 1));
            Apply(session, 1, CommandKind.DeployHero, target: 1, cell: new Hex(4, -1));
            Apply(session, 1, CommandKind.DeployHero, target: 3, cell: new Hex(4, 1));
            return session;
        }
        internal static void Apply(GameSession session, int seat, CommandKind kind, string value = "", int target = -1, Hex cell = default, MoveMode mode = MoveMode.Secondary)
        {
            var result = session.Execute(seat, Cmd(session, seat, kind, value, target, cell, mode));
            Assert.That(result.Accepted, Is.True, result.Code + ": " + result.Message);
        }
        internal static void Reveal(GameSession session, string color = "gold")
        {
            for (int seat = 0; seat < 4; seat++)
            {
                Apply(session, seat, CommandKind.SelectCard, "hero" + seat + "-" + color);
                Apply(session, seat, CommandKind.ConfirmCard);
            }
        }
        [Test]
        public void OnlyOwnCaptainCanPlaceOnUnoccupiedOwnSpawns()
        {
            var session = Deployment();
            string before = session.ExportSave();
            Assert.That(session.Execute(2, Cmd(session, 2, CommandKind.DeployHero, target: 2, destination: new Hex(-4, 1))).Accepted, Is.False);
            Assert.That(session.Execute(0, Cmd(session, 0, CommandKind.DeployHero, target: 1, destination: new Hex(4, 1))).Accepted, Is.False);
            Assert.That(session.ExportSave(), Is.EqualTo(before));
            Apply(session, 0, CommandKind.DeployHero, target: 0, cell: new Hex(-4, -1));
            before = session.ExportSave();
            Assert.That(session.Execute(0, Cmd(session, 0, CommandKind.DeployHero, target: 2, destination: new Hex(-4, -1))).Accepted, Is.False);
            Assert.That(session.ExportSave(), Is.EqualTo(before));
            Assert.That(Planning().View(0).Phase, Is.EqualTo(Phase.Planning));
        }
        [Test]
        public void SelectionCanChangeAndNeverLeaksBeforeReveal()
        {
            var session = Planning();
            Apply(session, 0, CommandKind.SelectCard, "hero0-gold");
            Apply(session, 0, CommandKind.SelectCard, "hero0-red");
            Assert.That(session.View(0).OwnCards.Single(c => c.Zone == CardZone.Selected).CardId, Is.EqualTo("hero0-red"));
            Assert.That(session.View(1).Players[0].Revealed, Is.Empty);
            Assert.That(session.View(1).Events.Any(e => e.CardId == "hero0-red"), Is.False);
            Assert.That(session.View(null).OwnCards, Is.Empty);
            Apply(session, 0, CommandKind.ConfirmCard);
            string before = session.ExportSave();
            Assert.That(session.Execute(0, Cmd(session, 0, CommandKind.SelectCard, "hero0-blue")).Accepted, Is.False);
            Assert.That(session.ExportSave(), Is.EqualTo(before));
        }
        [Test]
        public void FourWayTieUsesDynamicCoinAndCaptainOwnership()
        {
            var session = Planning(); Reveal(session);
            Assert.That(session.View(0).Phase, Is.EqualTo(Phase.InitiativeChoice));
            Assert.That(session.View(0).Pending!.CandidateSeats, Is.EqualTo(new[] { 0, 2 }));
            Assert.That(session.View(0).DecisionCoin, Is.EqualTo(Team.Red));
            string pendingSave = session.ExportSave();
            session = new GameSession(FoundationTests.Fixture(), new JsonStateCodec(), new JsonStateCodec().Read(pendingSave));
            Assert.That(session.Execute(2, Cmd(session, 2, CommandKind.ChooseInitiative, target: 2)).Accepted, Is.False);
            Apply(session, 0, CommandKind.ChooseInitiative, target: 2);
            Apply(session, 2, CommandKind.Pass);
            Assert.That(session.View(1).Pending!.CandidateSeats, Is.EqualTo(new[] { 1, 3 }));
            Assert.That(session.View(1).DecisionCoin, Is.EqualTo(Team.Blue));
            Apply(session, 1, CommandKind.ChooseInitiative, target: 3);
            Apply(session, 3, CommandKind.Pass);
            Assert.That(session.View(0).ActiveSeat, Is.EqualTo(0));
            Assert.That(session.View(0).DecisionCoin, Is.EqualTo(Team.Red));
            Apply(session, 0, CommandKind.Pass);
            Assert.That(session.View(1).ActiveSeat, Is.EqualTo(1));
            Assert.That(session.View(1).DecisionCoin, Is.EqualTo(Team.Red));
            Apply(session, 1, CommandKind.Pass);
            Assert.That(session.View(0).Turn, Is.EqualTo(2));
        }
        [Test]
        public void FourTurnsAutomaticallyReachExplicitRoundEndBoundary()
        {
            var session = Planning();
            foreach (string color in new[] { "gold", "red", "green", "blue" })
            {
                Reveal(session, color);
                for (int i = 0; i < 4; i++)
                {
                    var view = session.View(0);
                    if (view.Pending != null) Apply(session, view.Pending.ChooserSeat, CommandKind.ChooseInitiative, target: view.Pending.CandidateSeats[0]);
                    Apply(session, session.View(0).ActiveSeat!.Value, CommandKind.Pass);
                }
            }
            Assert.That(session.View(0).Phase, Is.EqualTo(Phase.RoundEnd));
            Assert.That(session.View(0).Round, Is.EqualTo(1));
            Assert.That(session.View(0).OwnCards.Count(c => c.Zone == CardZone.InHand), Is.EqualTo(1));
        }
    }
}
