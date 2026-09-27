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
        public int UltimateBonus;
        public string CardTextReason = "";
        public List<string> CardTextSourceUnits = new List<string>();
        // One entry per point of support: a dual-kind adjacent minion contributes its ID twice.
        public List<string> EnemySupportSources = new List<string>();
        public List<string> FriendlyGuardSources = new List<string>();
    }
    public sealed class DefenseAssessment
    {
        public int BaseDefense, DefenseBonus, FinalDefense, AttackCompared;
        public bool IgnoresMinions, Blocked, Successful;
    }
    public sealed class CardAttackModifier
    {
        public int Amount;
        public bool Unblockable;
        public string Reason = "";
        public List<string> UnitSources = new List<string>();
    }
    [Serializable]
    public sealed class ActionCompletionProgress
    {
        public string SourceCardId = "", ProgramId = "", TargetUnitId = "";
        public int ProgramVersion;
        public string Stage = "target";
        public ActionMinionBattleProgress? ActionBattle;
    }
    [Serializable]
    public sealed class ForcedPaymentProgress
    {
        public string SourceCardId = "", TargetUnitId = "";
        public int ControllerSeat;
    }
    [Serializable]
    public sealed class CardExecution
    {
        public string CardId = "", ProgramId = "", TargetUnitId = "";
        public string? ActionInstanceId;
        public bool FromDiscard;
        public bool RecoveredCard;
        public bool ApproachRepeated;
        public int ControllerSeat, ProgramVersion, Cursor;
        public AttackBreakdown? Attack;
        public bool AwaitingAttackCompletion;
        public string AttackOutcome = "";
        public DefenseResponse? DefenseResponse;
        public bool PreAttackDiscarded, AttackRangeLocked;
        public int AttackRangeBonus;
        public bool PreAttackMoved;
        public bool ReturnSourceAtEnd;
        public Hex? AttackTargetCell;
        public List<string>? DisplacedMinions;
        public List<string>? RemainingUnitTargets, AffectedHeroTargets;
        public string? ReturningMinionId;
        public bool ActionStopped;
        public ActionCompletionProgress? Completion;
        public ForcedPaymentProgress? ForcedPayment;
        public bool UltimateRepeatUsed;
        public string? UltimateRepeatExcludedTarget;
    }
    [Serializable]
    public sealed class PendingMinionDefeat
    {
        public string UnitId="", Source="", ProtectionCardId="";
        public int RewardSeat, ProtectorSeat;
        public bool BypassProtection, FinishActionOnResume, ResumeCardExecution, ResumeRoundEnd;
        public Phase ResumePhase;
        public int? ResumeActiveSeat;
        public PendingChoice? ResumePending;
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
