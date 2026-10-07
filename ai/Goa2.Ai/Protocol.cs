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
        public const int Format = 4;
        public PublicRuleProfile Rules = new PublicRuleProfile();
        public int Schema = Format, Seat, Round, Turn, BlueCrystal, RedCrystal, BlueMarks, RedMarks;
        public string Phase = "", Decision = "", CombatRegion = "", Coin = "";
        public int? ActiveSeat;
        public int? AttackRange;
        public ObservedResponse? Response;
        public ObservedSequence Sequence = new ObservedSequence();
        public ObservedOpening? Opening;
        public int BlueCaptain, RedCaptain, RemainingMinionRemovals;
        public string RoundEndStage = "";
        public List<int> UpgradingSeats = new List<int>();
        public ObservedAttack? Attack;
        public List<ObservedPlayer> Players = new List<ObservedPlayer>();
        public List<ObservedUnit> Units = new List<ObservedUnit>();
        public List<ObservedUnit> PendingSpawns = new List<ObservedUnit>();
        public List<ObservedUpgrade> OwnUpgrades = new List<ObservedUpgrade>();
        public List<ObservedEvent> PublicHistory = new List<ObservedEvent>();
        public List<ObservedEffect> Effects = new List<ObservedEffect>();
    }
    public sealed class ObservedAttack
    {
        public string Card = "", Target = "";
        public int Attacker, Defender, Base, Bonus, Support, Guard, Final, TextBonus, UltimateBonus;
        public bool Ranged, Unblockable;
        public List<string> TextSources = new List<string>(), SupportSources = new List<string>(), GuardSources = new List<string>();
    }
    public sealed class ObservedPlayer
    {
        public int Seat, Level, Gold, BasicAttackBonus, BasicAttackRangeBonus;
        public string Team = "", Hero = "", Purple = "";
        public bool Confirmed, AwaitingRespawn, Poisoned, PoisonDefense, Petrified;
        public List<ObservedCard> Cards = new List<ObservedCard>();
        public ObservedBonuses Permanent = new ObservedBonuses(), Effective = new ObservedBonuses();
    }
    public sealed class ObservedBonuses { public int Attack, Defense, Movement, Initiative, SkillRange, AttackRange; }
    public sealed class ObservedCard { public string Id = "", Zone = ""; public int? PlayedRound, PlayedTurn; }
    public sealed class ObservedUnit { public string Id = "", Kind = "", Team = ""; public int? Seat; public Hex Position; public bool Removable; }
    public sealed class ObservedEvent
    {
        // Ordinal is the PUBLIC event order, never the authority sequence (which includes private events).
        public int Ordinal, Round, Turn;
        public string Kind = "", Card = "";
        public string Unit = "", Value = "", SecondaryValue = "";
        public int? OtherSeat, Amount, Amount2, Amount3;
        public int? Seat;
        public Hex? From, To;
        public List<Hex> Path = new List<Hex>();
    }
    public sealed class ObservedEffect
    {
        public string Kind = "", Card = "", SourceUnit = "", ProtectedUnit = "";
        public int Controller, StartRound, StartTurn, EndRound, EndTurn;
        public int CreatedRound, CreatedTurn, Order, BaseRadius;
        public int? ExemptSeat;
        public bool PersistsThroughDefeat;
        public string Duration = "", AreaKind = "";
        public List<Hex> Area = new List<Hex>();
    }
    public sealed class ObservedResponse { public string Source = "", Unit = ""; public bool Optional; }
    public sealed class ObservedSequence
    {
        public int Round, Turn;
        public List<ObservedActionCard> Cards = new List<ObservedActionCard>();
    }
    public sealed class ObservedActionCard
    {
        // IDs only link nodes inside this public action sequence. They are never learned categories.
        public int Key, Order, Seat;
        public int? Parent;
        public string Card = "", Role = "";
        public int Initiative;
        public bool Started, Resolved, Focused;
    }
    public sealed class ObservedOpening
    {
        public string Purpose = "", Status = "", FirstTeam = "", Result = "", DraftTeam = "";
        public bool DraftComplete, OpeningComplete;
    }
    public sealed class ObservedUpgrade
    {
        public int Round, HeroLevel, CardLevel, Amount;
        public string Previous = "", Selected = "", Rejected = "", Bonus = "";
    }
    public sealed class CandidateFacts
    {
        public List<Hex> Path = new List<Hex>();
        public ObservedDefense? Defense;
        public ObservedUpgrade? Upgrade;
        public bool? Place;
        public int? RemainingDistance, TransferAmount;
        public string PreviewKind = "";
        public bool? PreviewOptional;
        public List<string> PreviewTargets = new List<string>();
        public List<Hex> PreviewCells = new List<Hex>();
    }
    public sealed class ObservedDefense
    {
        public bool Primary, Blocked, IgnoresMinions;
        public int Base, Bonus, Final, AttackCompared;
    }
    public sealed class Candidate
    {
        public const int Format = 2;
        public string Id = "", Kind = "", Value = "", Mode = "";
        public int TargetSeat = -1;
        public Hex Destination;
        public bool HasDestination, SuccessfulDefense, ImmediateSkip;
        public CandidateFacts Facts = new CandidateFacts();
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
