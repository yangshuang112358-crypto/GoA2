#nullable enable
using System.Linq;

namespace Goa2.Domain
{
    public static class ContentSnapshot
    {
        public static ContentCatalog Copy(ContentCatalog source) => new ContentCatalog
        {
            Version = source.Version, Hash = source.Hash,
            Heroes = source.Heroes.Select(h => new HeroDefinition { Id = h.Id, Name = h.Name }).ToList(),
            Cells = source.Cells.Select(c => new CellDefinition { Position = c.Position, Region = c.Region, Obstacle = c.Obstacle, Lane = c.Lane, Base = c.Base, Spawn = c.Spawn }).ToList(),
            Cards = source.Cards.Select(c => new CardDefinition
            {
                Id = c.Id, HeroId = c.HeroId, Name = c.Name, Color = c.Color, Level = c.Level, Initiative = c.Initiative,
                PrimaryCategory = c.PrimaryCategory, PrimaryFamily = c.PrimaryFamily, Text = c.Text, PrimaryValue = c.PrimaryValue,
                Exclamation = c.Exclamation, Subtype = c.Subtype, SubtypeValue = c.SubtypeValue, SecondaryMovement = c.SecondaryMovement,
                SecondaryDefense = c.SecondaryDefense, Passive = c.Passive
            }).ToList(),
            Rules = new RuleSettings
            {
                Version = source.Rules.Version, StartingCrystalLife = source.Rules.StartingCrystalLife, FrontlineVictoryMarks = source.Rules.FrontlineVictoryMarks,
                TurnsPerRound = source.Rules.TurnsPerRound, HandSize = source.Rules.HandSize, InitialCombatRegion = source.Rules.InitialCombatRegion
            }
        };
    }
}
