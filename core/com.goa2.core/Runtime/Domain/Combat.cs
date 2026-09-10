#nullable enable
using System;
using System.Collections.Generic;

namespace Goa2.Domain
{
    [Serializable]
    public sealed class AttackBreakdown
    {
        public string SourceCardId = "", TargetUnitId = "";
        public int AttackerSeat, DefenderSeat;
        public bool Ranged, Unblockable;
        public int BaseAttack, AttackBonus, EnemySupport, FriendlyGuard, FinalAttack;
        public int CardTextBonus;
        public List<string> EnemySupportSources = new List<string>();
        public List<string> FriendlyGuardSources = new List<string>();
    }
    public sealed class DefenseAssessment
    {
        public int BaseDefense, DefenseBonus, FinalDefense, AttackCompared;
        public bool IgnoresMinions, Blocked, Successful;
    }
    [Serializable]
    public sealed class CardExecution
    {
        public string CardId = "", ProgramId = "", TargetUnitId = "";
        public int ControllerSeat, ProgramVersion, Cursor;
        public AttackBreakdown? Attack;
        public bool AwaitingAttackCompletion;
        public string AttackOutcome = "";
        public DefenseResponse? DefenseResponse;
    }
    [Serializable]
    public sealed class DefenseResponse
    {
        public string SourceCardId = "", SourceUnitId = "", AttackerUnitId = "", ProgramId = "";
        public int ControllerSeat, AttackerSeat, ProgramVersion, Cursor;
    }
    public sealed class DefenseOption
    {
        public string CardId = "";
        public bool Primary, Block, IgnoresMinions;
        public DefenseAssessment Assessment = new DefenseAssessment();
    }
}
