#nullable enable
using System.Collections.Generic;

namespace Goa2.Domain
{
    public sealed class PlayerView
    {
        public int Seat;
        public Team Team;
        public string Name = "";
        public string? HeroId;
        public int Level;
        public int Gold;
        public bool Confirmed;
        public bool AwaitingRespawn;
        public string? PurpleCardId;
        public int HandCount;
        public Dictionary<string, int> PermanentBonuses = new Dictionary<string, int>();
        public List<CardInstance> Revealed = new List<CardInstance>();
        public List<PublicPlay> Plays = new List<PublicPlay>();
        public List<string> DiscardColors = new List<string>();
    }
    public sealed class PublicPlay
    {
        public int Round;
        public int Turn;
        public string CardId = "";
        public string Color = "";
    }
    public sealed class MoveOption
    {
        public Hex Destination;
        public List<Hex> Path = new List<Hex>();
    }
    public sealed class GameView
    {
        public string MatchId = "";
        public long Revision;
        public bool Sandbox;
        public bool QuickSelection;
        public Phase Phase;
        public int Round;
        public int Turn;
        public Team DecisionCoin;
        public int? ActiveSeat;
        public int BlueCaptain;
        public int RedCaptain;
        public int BlueCrystal;
        public int RedCrystal;
        public string CombatRegion = "";
        public int BlueMarks;
        public int RedMarks;
        public int VictoryMarksRequired;
        public Team? Winner;
        public string VictoryReason = "";
        public List<string> RemovableMinions = new List<string>();
        public UnitState? PendingSpawn;
        public List<UnitState> PendingSpawns = new List<UnitState>();
        public Dictionary<string, List<Hex>> SpawnChoices = new Dictionary<string, List<Hex>>();
        public int EngineVersion;
        public bool CanUpgradeEngine;
        public bool CanBeginPrimary;
        public bool PrimarySupported;
        public string PrimaryRestriction = "";
        public List<ActiveEffect> Effects = new List<ActiveEffect>();
        public Dictionary<string, List<Hex>> EffectAreas = new Dictionary<string, List<Hex>>();
        public List<string> SupportedPrimaryCards = new List<string>();
        public List<string> SupportedDefenseCards = new List<string>();
        public List<string> AttackTargets = new List<string>();
        public List<DefenseOption> DefenseOptions = new List<DefenseOption>();
        public List<string> ForcedDiscardCards = new List<string>();
        public List<string> UnimplementedDefenseCards = new List<string>();
        public AttackBreakdown? Attack;
        public List<Hex> RespawnCells = new List<Hex>();
        public bool CanResolveRoundEnd;
        public string RoundEndStage = "";
        public int RemainingMinionRemovals;
        public List<string> RoundMinionRemovals = new List<string>();
        public List<int> UpgradingSeats = new List<int>();
        public List<UpgradeOption> UpgradeOptions = new List<UpgradeOption>();
        public List<UpgradeRecord> OwnUpgradeHistory = new List<UpgradeRecord>();
        public List<PlayerView> Players = new List<PlayerView>();
        public List<UnitState> Units = new List<UnitState>();
        public PendingChoice? Pending;
        public List<CardInstance> OwnCards = new List<CardInstance>();
        public List<string> AvailableHeroes = new List<string>();
        public Dictionary<int, List<Hex>> Deployments = new Dictionary<int, List<Hex>>();
        public List<MoveOption> SecondaryMoves = new List<MoveOption>();
        public List<MoveOption> FastMoves = new List<MoveOption>();
        public Dictionary<string, List<Hex>> DebugTeleports = new Dictionary<string, List<Hex>>();
        public bool CanPass;
        public List<GameEvent> Events = new List<GameEvent>();
    }
    public sealed class CommandResult
    {
        public bool Accepted;
        public bool Duplicate;
        public string Code = "";
        public string Message = "";
        public GameView View = new GameView();
    }
}
