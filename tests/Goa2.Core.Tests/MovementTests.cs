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
    public sealed class MovementTests
    {
        internal static GameSession Active(ContentCatalog? content = null)
        {
            var session = Planning(content); Reveal(session);
            Apply(session, 0, CommandKind.ChooseInitiative, target: 0);
            return session;
        }
        [Test]
        public void OrdinaryPathHonorsExactBudgetObstaclesAndFriendlyUnits()
        {
            var catalog = FoundationTests.Fixture();
            catalog.Cell(new Hex(-3, -1))!.Obstacle = true;
            var session = Active(catalog); var state = new JsonStateCodec().Read(session.ExportSave());
            var moves = MovementRules.LegalMoves(catalog, state, 0, MoveMode.Secondary);
            Assert.That(moves.Any(m => m.Destination == new Hex(-2, -1)), Is.False, "blocked direct path needs three steps");
            Assert.That(moves.Any(m => m.Destination == new Hex(-4, 1)), Is.False, "teammate occupies destination");
            Assert.That(moves.Any(m => m.Destination == new Hex(-3, 0)), Is.True, "two-step route around obstacle");
            Assert.That(moves.All(m => m.Path.Count <= 3 && m.Path[0] == new Hex(-4, -1)), Is.True);
            Assert.That(moves.Any(m => m.Destination == new Hex(-3, -1)), Is.False);
        }
        [Test]
        public void MovementCommitsOnceAndCannotResetBudgetWithSecondCommand()
        {
            var session = Active();
            Apply(session, 0, CommandKind.Move, cell: new Hex(-2, -1));
            var view = session.View(0);
            Assert.That(view.Units.Single(u => u.Seat == 0).Position, Is.EqualTo(new Hex(-2, -1)));
            Assert.That(view.Events.Last(e => e.Kind == "UnitMoved").Path.Count, Is.EqualTo(3));
            Assert.That(view.OwnCards.Single(c => c.CardId == "hero0-gold").Zone, Is.EqualTo(CardZone.PlayedResolved));
            string before = session.ExportSave();
            Assert.That(session.Execute(0, Cmd(session, 0, CommandKind.Move, destination: new Hex(0, -1))).Accepted, Is.False);
            Assert.That(session.ExportSave(), Is.EqualTo(before));
        }
        [Test]
        public void FastMovementUsesSafeAdjacentRegionsNotPointBudget()
        {
            var catalog = FoundationTests.Fixture();
            var session = Active(catalog); var state = new JsonStateCodec().Read(session.ExportSave());
            state.Units.RemoveAll(u => u.Kind != "hero");
            var fast = MovementRules.LegalMoves(catalog, state, 0, MoveMode.Fast);
            Assert.That(fast.Any(m => m.Destination == new Hex(1, 0)), Is.True, "safe adjacent mid exceeds ordinary budget");
            Assert.That(fast.Any(m => m.Destination == new Hex(3, 0)), Is.False, "red fountain is not adjacent");
            state.Units.Add(new UnitState { Id = "enemy", Kind = "ranged", Team = Team.Red, Position = new Hex(0, 2) });
            Assert.That(MovementRules.LegalMoves(catalog, state, 0, MoveMode.Fast).Any(m => catalog.Cell(m.Destination)!.Region == "mid"), Is.False);
            state.Units.Last().Position = new Hex(-3, 2);
            Assert.That(MovementRules.LegalMoves(catalog, state, 0, MoveMode.Fast), Is.Empty, "enemy in source region forbids all fast moves");
        }
        [Test]
        public void PrimaryMovementHasNoSecondaryOptionButAllowsFastReplacement()
        {
            var catalog = FoundationTests.Fixture();
            var card = catalog.Card("hero0-gold"); card.PrimaryFamily = "movement"; card.PrimaryValue = 4;
            var session = Active(catalog); var state = new JsonStateCodec().Read(session.ExportSave());
            Assert.That(MovementRules.LegalMoves(catalog, state, 0, MoveMode.Secondary), Is.Empty);
            Assert.That(MovementRules.LegalMoves(catalog, state, 0, MoveMode.Fast), Is.Not.Empty);
            card.PrimaryFamily = "skill"; card.SecondaryMovement = null;
            Assert.That(MovementRules.LegalMoves(catalog, state, 0, MoveMode.Fast), Is.Empty);
        }
        [Test]
        public void IllegalDestinationsAndWrongSeatAreAtomicAndReadOnlyQueriesStayReadOnly()
        {
            var session = Active(); var catalog = FoundationTests.Fixture();
            var codec = new JsonStateCodec(); var state = codec.Read(session.ExportSave()); string before = codec.Write(state);
            MovementRules.LegalMoves(catalog, state, 0, MoveMode.Secondary);
            Assert.That(codec.Write(state), Is.EqualTo(before));
            foreach (var cell in new[] { new Hex(100, 100), new Hex(4, -1), new Hex(-4, 1), new Hex(0, 0) })
            {
                string saved = session.ExportSave();
                Assert.That(session.Execute(0, Cmd(session, 0, CommandKind.Move, destination: cell)).Accepted, Is.False);
                Assert.That(session.ExportSave(), Is.EqualTo(saved));
            }
            Assert.That(session.Execute(1, Cmd(session, 1, CommandKind.Move, destination: new Hex(3, -1))).Accepted, Is.False);
        }
    }
}
