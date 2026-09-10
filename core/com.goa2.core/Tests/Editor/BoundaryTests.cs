using System;
using System.Collections.Generic;
using System.Linq;
using Goa2.Domain;
using Goa2.Infrastructure;
using NUnit.Framework;
using static Goa2.Tests.SessionTests;
using static Goa2.Tests.TurnFlowTests;

namespace Goa2.Tests
{
    public sealed class BoundaryTests
    {
        [TestCase(-1)]
        [TestCase(4)]
        [TestCase(99)]
        public void InvalidIdentityReceivesNoPrivateHand(int attacker)
        {
            var session = Planning();
            var result = session.Execute(attacker, Cmd(session, 0, CommandKind.SelectCard, "hero0-gold"));
            Assert.That(result.Code, Is.EqualTo("unauthorized"));
            Assert.That(result.View.OwnCards, Is.Empty);
        }
        [Test]
        public void RevealWaitsForEveryRequiredSeatAndKeepsEarlierTurnNumbers()
        {
            var session = Planning();
            for (int seat = 0; seat < 3; seat++)
            {
                Apply(session, seat, CommandKind.SelectCard, "hero" + seat + "-gold");
                Apply(session, seat, CommandKind.ConfirmCard);
            }
            Assert.That(session.View(3).Phase, Is.EqualTo(Phase.Planning));
            Assert.That(session.View(3).Events.Any(e => e.Kind == "CardRevealed"), Is.False);
            Apply(session, 3, CommandKind.SelectCard, "hero3-gold");
            Apply(session, 3, CommandKind.ConfirmCard);
            for (int i = 0; i < 4; i++)
            {
                var view = session.View(0);
                if (view.Pending != null) Apply(session, view.Pending.ChooserSeat, CommandKind.ChooseInitiative, target: view.Pending.CandidateSeats[0]);
                Apply(session, session.View(0).ActiveSeat!.Value, CommandKind.Pass);
            }
            Assert.That(session.View(0).OwnCards.Single(c => c.CardId == "hero0-gold").PlayedTurn, Is.EqualTo(1));
            Assert.That(session.View(0).Turn, Is.EqualTo(2));
            string before = session.ExportSave();
            Assert.That(session.Execute(0, Cmd(session, 0, CommandKind.SelectCard, "hero0-gold")).Code, Is.EqualTo("invalid_card"));
            Assert.That(session.ExportSave(), Is.EqualTo(before));
        }
        [Test]
        public void ActualCatalogSupportsCompleteFourTurnReplay()
        {
            var content = ContentLoader.LoadDirectory(ContentTests.Root());
            var game = LocalGameFactory.Create(content, "match", new[] { "A", "B", "C", "D" }, 3);
            for (int seat = 0; seat < 4; seat++) Apply(game, seat, CommandKind.ChooseHero, content.Heroes[seat].Id);
            foreach (int captain in new[] { 0, 1 })
                foreach (int target in new[] { captain, captain + 2 })
                    Apply(game, captain, CommandKind.DeployHero, target: target, cell: game.View(captain).Deployments[target][0]);
            int moved = 0;
            for (int turn = 0; turn < 4; turn++)
            {
                for (int seat = 0; seat < 4; seat++)
                {
                    var card = game.View(seat).OwnCards.First(c => c.Zone == CardZone.InHand);
                    Apply(game, seat, CommandKind.SelectCard, card.CardId);
                    Apply(game, seat, CommandKind.ConfirmCard);
                }
                for (int action = 0; action < 4; action++)
                {
                    var shared = game.View(null);
                    if (shared.Pending != null) Apply(game, shared.Pending.ChooserSeat, CommandKind.ChooseInitiative, target: shared.Pending.CandidateSeats[0]);
                    int seat = game.View(null).ActiveSeat!.Value;
                    var own = game.View(seat);
                    if (own.SecondaryMoves.Count > 0) { Apply(game, seat, CommandKind.Move, cell: own.SecondaryMoves[0].Destination); moved++; }
                    else Apply(game, seat, CommandKind.Pass);
                }
            }
            Assert.That(moved, Is.GreaterThanOrEqualTo(8));
            Assert.That(game.View(null).Phase, Is.EqualTo(Phase.RoundEnd));
            Assert.That(LocalGameFactory.Restore(content, game.ExportSave()).ExportSave(), Is.EqualTo(game.ExportSave()));
        }
        [Test]
        public void PureCoreAssembliesHaveNoUnityReferences()
        {
            foreach (var assembly in new[] { typeof(GameState).Assembly, typeof(Goa2.Rules.GameRules).Assembly, typeof(Goa2.Application.GameSession).Assembly })
                Assert.That(assembly.GetReferencedAssemblies().Any(a => a.Name!.StartsWith("Unity", StringComparison.Ordinal)), Is.False, assembly.GetName().Name);
        }
    }
}
