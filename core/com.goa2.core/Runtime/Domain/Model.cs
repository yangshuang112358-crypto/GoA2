#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Goa2.Domain
{
    public enum Team { Blue, Red }
    public enum Phase { HeroSelection, Deployment, Planning, InitiativeChoice, Action, RoundEnd }
    public enum CardZone { InHand, Selected, PlayedUnresolved, PlayedResolved, Discarded }
    public enum CommandKind { ChooseHero, DeployHero, SelectCard, ConfirmCard, ChooseInitiative, Move, Pass }
    public enum MoveMode { Secondary, Fast }

    [Serializable]
    public struct Hex : IEquatable<Hex>
    {
        public int X;
        public int Y;
        public Hex(int x, int y) { X = x; Y = y; }
        public int Distance(Hex other) => Math.Max(Math.Abs(X - other.X), Math.Max(Math.Abs(Y - other.Y), Math.Abs((X - other.X) + (Y - other.Y))));
        public IEnumerable<Hex> Neighbors()
        {
            yield return new Hex(X + 1, Y); yield return new Hex(X, Y + 1);
            yield return new Hex(X - 1, Y + 1); yield return new Hex(X - 1, Y);
            yield return new Hex(X, Y - 1); yield return new Hex(X + 1, Y - 1);
        }
        public bool Equals(Hex other) => X == other.X && Y == other.Y;
        public override bool Equals(object? obj) => obj is Hex hex && Equals(hex);
        public override int GetHashCode() => unchecked(X * 397 ^ Y);
        public override string ToString() => $"{X},{Y}";
        public static bool operator ==(Hex a, Hex b) => a.Equals(b);
        public static bool operator !=(Hex a, Hex b) => !a.Equals(b);
    }

    [Serializable]
    public sealed class HeroDefinition
    {
        public string Id = "";
        public string Name = "";
    }
    [Serializable]
    public sealed class CardDefinition
    {
        public string Id = "";
        public string HeroId = "";
        public string Name = "";
        public string Color = "";
        public int? Level;
        public int Initiative;
        public string PrimaryCategory = "";
        public string PrimaryFamily = "";
        public string Text = "";
        public int PrimaryValue;
        public bool Exclamation;
        public string? Subtype;
        public int? SubtypeValue;
        public int? SecondaryMovement;
        public int? SecondaryDefense;
        public string? Passive;
    }
    [Serializable]
    public sealed class CellDefinition
    {
        public Hex Position;
        public string Region = "";
        public bool Obstacle;
        public bool Lane;
        public string? Base;
        public string Spawn = "";
    }
    [Serializable]
    public sealed class RuleSettings
    {
        public string Version = "";
        public int StartingCrystalLife;
        public int FrontlineVictoryMarks;
        public int TurnsPerRound;
        public int HandSize;
        public string InitialCombatRegion = "";
    }
    public sealed class ContentCatalog
    {
        public string Version = "";
        public string Hash = "";
        public List<HeroDefinition> Heroes = new List<HeroDefinition>();
        public List<CardDefinition> Cards = new List<CardDefinition>();
        public List<CellDefinition> Cells = new List<CellDefinition>();
        public RuleSettings Rules = new RuleSettings();
        public CardDefinition Card(string id) => Cards.Single(c => c.Id == id);
        public CellDefinition? Cell(Hex at) => Cells.FirstOrDefault(c => c.Position == at);
    }
    [Serializable]
    public sealed class CardInstance
    {
        public string CardId = "";
        public CardZone Zone;
        public int? PlayedRound;
        public int? PlayedTurn;
    }
    [Serializable]
    public sealed class PlayerState
    {
        public int Seat;
        public Team Team;
        public string Name = "";
        public string? HeroId;
        public int Level = 1;
        public int Gold;
        public int InitiativeBonus;
        public int MovementBonus;
        public bool Confirmed;
        public List<CardInstance> Cards = new List<CardInstance>();
    }
    [Serializable]
    public sealed class UnitState
    {
        public string Id = "";
        public string Kind = "";
        public Team Team;
        public int? Seat;
        public Hex Position;
    }
    [Serializable]
    public sealed class PendingChoice
    {
        public string Id = "";
        public string Kind = "";
        public int ChooserSeat;
        public List<int> CandidateSeats = new List<int>();
        public string Source = "";
        public string ResumeAt = "";
        public bool Optional;
    }
    [Serializable]
    public sealed class GameEvent
    {
        public long Sequence;
        public long Revision;
        public string CommandId = "";
        public string Kind = "";
        public int? Seat;
        public string? CardId;
        public int? PrivateTo;
        public Hex? From;
        public Hex? To;
        public List<Hex> Path = new List<Hex>();
        public string Detail = "";
    }
    [Serializable]
    public sealed class Command
    {
        public string Id = "";
        public string MatchId = "";
        public long ExpectedRevision;
        public int ActorSeat;
        public CommandKind Kind;
        public string Value = "";
        public int TargetSeat = -1;
        public Hex Destination;
        public MoveMode MoveMode;
    }
    [Serializable]
    public sealed class CommandReceipt
    {
        public string Id = "";
        public int ActorSeat;
        public string Fingerprint = "";
        public long Revision;
    }
    [Serializable]
    public sealed class GameState
    {
        public const string CurrentProtocol = "1.0.0";
        public string ProtocolVersion = CurrentProtocol;
        public string MatchId = "";
        public string ContentVersion = "";
        public string ContentHash = "";
        public string RulesVersion = "";
        public long Revision;
        public int Seed;
        public Phase Phase;
        public int Round = 1;
        public int Turn = 1;
        public Team DecisionCoin;
        public int? ActiveSeat;
        public int BlueCaptain;
        public int RedCaptain = 1;
        public int BlueCrystal;
        public int RedCrystal;
        public int VictoryMarksRequired;
        public string CombatRegion = "";
        public List<PlayerState> Players = new List<PlayerState>();
        public List<UnitState> Units = new List<UnitState>();
        public PendingChoice? Pending;
        public List<GameEvent> Events = new List<GameEvent>();
        public List<CommandReceipt> Receipts = new List<CommandReceipt>();
        public List<Command> AcceptedCommands = new List<Command>();
    }
    public interface IStateCodec
    {
        string Write(GameState state);
        GameState Read(string json);
        string WriteCommand(Command command);
    }
    public sealed class RuleViolation : Exception
    {
        public string Code { get; }
        public RuleViolation(string code, string message) : base(message) { Code = code; }
    }
}
