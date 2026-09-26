#nullable enable
using System;
using System.Collections.Generic;

namespace Goa2.Domain
{
    [Serializable]
    public sealed class UpgradeRecord
    {
        public int Round;
        public int HeroLevel;
        public int CardLevel;
        public string Color = "";
        public string PreviousCardId = "";
        public string SelectedCardId = "";
        public string RejectedCardId = "";
        public string Bonus = "";
        public int Amount = 1;
    }
    [Serializable]
    public sealed class PlayerUpgradeProgress
    {
        public int Seat;
        public int StartingLevel;
        public List<int> PendingLevels = new List<int>();
    }
    [Serializable]
    public sealed class BattleHeroContribution
    {
        public string UnitId = "";
        public string SourceCardId = "";
        public int ControllerSeat;
        public int Count;
        public string ProgramId = "";
        public int ProgramVersion;
    }
    [Serializable]
    public sealed class RoundEndProgress
    {
        public int Round;
        public string Stage = "minion_battle";
        public int BlueMinions;
        public int RedMinions;
        public Team? LosingTeam;
        public int RemainingRemovals;
        public List<PlayerUpgradeProgress> Upgrades = new List<PlayerUpgradeProgress>();
        public List<BattleHeroContribution>? HeroContributions;
    }
    public sealed class UpgradeOption
    {
        public string CardId = "";
        public string PreviousCardId = "";
        public string RejectedCardId = "";
        public string Bonus = "";
        public string Color = "";
        public int CardLevel;
        public int HeroLevel;
    }
}
