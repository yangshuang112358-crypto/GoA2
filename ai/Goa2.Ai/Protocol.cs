using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Goa2.Domain;

namespace Goa2.Ai
{
    // No GameSession, GameState, Command, debug fields, RNG or private event payloads cross this boundary.
    public sealed class Observation
    {
        public const int Format = 3;
        public PublicRuleProfile Rules = new PublicRuleProfile();
        public int Schema = Format, Seat, Round, Turn, BlueCrystal, RedCrystal, BlueMarks, RedMarks;
        public string Phase = "", Decision = "", CombatRegion = "", Coin = "";
        public int? ActiveSeat;
        public string CurrentCard = "";
        public ObservedAttack? Attack;
        public List<ObservedPlayer> Players = new List<ObservedPlayer>();
        public List<ObservedUnit> Units = new List<ObservedUnit>();
        public List<ObservedCard> OwnCards = new List<ObservedCard>();
        public List<ObservedEvent> PublicHistory = new List<ObservedEvent>();
        public List<ObservedEffect> Effects = new List<ObservedEffect>();
    }
    public sealed class ObservedAttack
    {
        public string Card = "", Target = "";
        public int Attacker, Defender, Base, Bonus, Support, Guard, Final, TextBonus, UltimateBonus;
        public bool Ranged, Unblockable;
    }
    public sealed class ObservedPlayer
    {
        public int Seat, Level, Gold, HandCount;
        public string Team = "", Hero = "", Purple = "";
        public bool Confirmed, AwaitingRespawn, Poisoned, PoisonDefense, Petrified;
        public List<ObservedCard> Cards = new List<ObservedCard>();
    }
    public sealed class ObservedCard { public string Id = "", Zone = ""; public int? PlayedRound, PlayedTurn; }
    public sealed class ObservedUnit { public string Id = "", Kind = "", Team = ""; public int? Seat; public Hex Position; }
    public sealed class ObservedEvent { public string Kind = "", Card = ""; public int? Seat; public Hex? From, To; }
    public sealed class ObservedEffect
    {
        public string Kind = "", Card = "", SourceUnit = "", ProtectedUnit = "";
        public int Controller, StartRound, StartTurn, EndRound, EndTurn;
    }
    public sealed class Candidate
    {
        public const int Format = 1;
        public string Id = "", Kind = "", Value = "", Mode = "";
        public int TargetSeat = -1;
        public Hex Destination;
        public bool HasDestination, SuccessfulDefense, ImmediateSkip;
    }
    public sealed class Decision
    {
        public long Revision;
        public Observation Observation = new Observation();
        public List<Candidate> Actions = new List<Candidate>();
    }
    public interface IPolicy { string Choose(Observation observation, IReadOnlyList<Candidate> actions); }
    public sealed class StableIds
    {
        private readonly Dictionary<string,string> cards;
        public StableIds(ContentCatalog catalog)
        {
            cards = catalog.Cards.ToDictionary(c => c.Id, c => Regex.Match(c.Id, @"\A[a-z]+-[0-9]+(?=-)").Value, StringComparer.Ordinal);
            if (cards.Values.Any(string.IsNullOrEmpty) || cards.Values.Distinct(StringComparer.Ordinal).Count() != cards.Count)
                throw new InvalidOperationException("Unsupported or colliding card IDs; extend the versioned ID registry explicitly.");
        }
        public string Card(string? value) => value == null || value == "" ? "" : cards.TryGetValue(value, out var id) ? id : throw new InvalidOperationException("Unknown card: " + value);
        public string Value(string value) => cards.TryGetValue(value, out var id) ? id : value;
        public static string Action(CommandKind kind, string value, int target, Hex at, MoveMode mode) =>
            string.Join("|", kind.ToString(), Uri.EscapeDataString(value), target.ToString(System.Globalization.CultureInfo.InvariantCulture), at.X.ToString(System.Globalization.CultureInfo.InvariantCulture), at.Y.ToString(System.Globalization.CultureInfo.InvariantCulture), mode.ToString());
    }
    public sealed class StableRandom
    {
        private uint state;
        public StableRandom(int seed) { state = unchecked((uint)seed) ^ 0x9e3779b9u; if(state == 0) state = 1; }
        public int Next(int count)
        {
            if(count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
            uint bound = (uint)count, threshold = unchecked(0u - bound) % bound, x;
            do { state ^= state << 13; state ^= state >> 17; state ^= state << 5; x = state; } while(x < threshold);
            return (int)(x % bound);
        }
    }
}
