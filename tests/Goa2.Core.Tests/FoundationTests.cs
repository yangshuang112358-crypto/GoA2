using System.Linq;
using Goa2.Domain;
using Goa2.Rules;
using NUnit.Framework;

namespace Goa2.Tests
{
    public sealed class FoundationTests
    {
        internal static ContentCatalog Fixture()
        {
            var catalog = new ContentCatalog { Version = "test", Hash = "test-hash", Rules = new RuleSettings { Version = "test", StartingCrystalLife = 7, FrontlineVictoryMarks = 3, TurnsPerRound = 4, HandSize = 5, InitialCombatRegion = "mid" } };
            for (int seat = 0; seat < 6; seat++)
            {
                string hero = "hero" + seat;
                catalog.Heroes.Add(new HeroDefinition { Id = hero, Name = hero });
                foreach (string color in new[] { "gold", "silver", "red", "green", "blue" })
                    catalog.Cards.Add(new CardDefinition { Id = hero + "-" + color, HeroId = hero, Name = color, Color = color, Level = color == "gold" || color == "silver" ? (int?)null : 1, Initiative = 11, PrimaryFamily = "attack", SecondaryMovement = 2 });
            }
            for (int x = -4; x <= 4; x++)
                for (int y = -3; y <= 3; y++)
                    catalog.Cells.Add(new CellDefinition { Position = new Hex(x, y), Region = x < -1 ? "blueFountain" : x > 1 ? "redFountain" : "mid", Spawn = "empty" });
            for (int y = -1; y <= 1; y++)
            {
                catalog.Cell(new Hex(-4, y))!.Spawn = "blueHeroSpawn";
                catalog.Cell(new Hex(4, y))!.Spawn = "redHeroSpawn";
            }
            catalog.Cell(new Hex(0, -2))!.Spawn = "redHeavySpawn";
            catalog.Cell(new Hex(0, 2))!.Spawn = "blueHeavySpawn";
            return catalog;
        }
        [Test]
        public void CreationUsesFourSeatsAndConfiguredRules()
        {
            var state = new GameRules().Create(Fixture(), "match", new[] { "A", "B", "C", "D" }, 42);
            Assert.That(state.Players.Select(p => p.Team), Is.EqualTo(new[] { Team.Blue, Team.Red, Team.Blue, Team.Red }));
            Assert.That(state.BlueCrystal, Is.EqualTo(7));
            Assert.That(state.VictoryMarksRequired, Is.EqualTo(3));
            Assert.That(state.Phase, Is.EqualTo(Phase.HeroSelection));
        }
        [Test]
        public void HeroChoiceGivesOnlyFiveStartingCardsAndRejectsDuplicate()
        {
            var rules = new GameRules(); var catalog = Fixture();
            var state = rules.Create(catalog, "match", new[] { "A", "B", "C", "D" }, 42);
            rules.Apply(catalog, state, new Command { Kind = CommandKind.ChooseHero, ActorSeat = 0, Value = "hero5" });
            Assert.That(state.Players[0].Cards.Count, Is.EqualTo(5));
            Assert.That(state.Players[0].Cards.All(c => c.Zone == CardZone.InHand), Is.True);
            Assert.Throws<RuleViolation>(() => rules.Apply(catalog, state, new Command { Kind = CommandKind.ChooseHero, ActorSeat = 1, Value = "hero5" }));
        }
    }
}
