#nullable enable
using System;

namespace Goa2.Domain
{
    [Serializable]
    public sealed class PoisonMarker
    {
        public int TargetSeat, SourceSeat, AppliedRound;
        public string SourceCardId = "";
        public bool IncludesDefense;
    }
    public enum EffectDuration { ThisTurn, NextTurn, ThisRound, ThisAndNextTurn }
    public enum EffectKind { MovementBoundary, SkillSuppression, NonAdjacentRangedImmunity, FriendlyBasicMinionsRanged, FriendlyAttackMinionsRanged, FriendlyAttackMinionsDual, FriendlyNearMinionDefense, FriendlyDisplacementProtection, ImmunityAndUnitTraversal, OtherEnemyActionImmunity, FriendlyMeleeDefeatPrevention, FriendlyNonHeavyDefeatPrevention, FriendlyMinionDefeatPrevention, AttackFromDiscard, AttackActionImmunity, OtherEnemyAttackImmunity, EnemyMeleeFriendlyForOwnDefense, EnemyLightMinionsFriendlyForOwnDefense, EnemyAllMinionsFriendlyForOwnDefense, EnemyActionMovementLimitOne, EnemyMovementGoldOrRedOnly, EnemyMovementGoldOnly, PetrifyNearestEnemyHeroes, PetrifyAllEnemyHeroes }
    public enum EffectAreaKind { SkillRange, Adjacent, None }
    [Serializable]
    public sealed class EffectWindow
    {
        public int StartRound, StartTurn, EndRound, EndTurn;
    }
    [Serializable]
    public sealed class ActiveEffect
    {
        public string Id = "", SourceCardId = "", SourceUnitId = "";
        public string ProtectedUnitId = "";
        public int? SourcePrivateTo;
        public int? ExemptControllerSeat;
        public int ControllerSeat, CreatedRound, CreatedTurn, CreationOrder;
        public int ProgramVersion = 1;
        public int BaseRadius;
        public bool PersistsThroughDefeat;
        public EffectKind Kind;
        public EffectDuration Duration;
        public EffectAreaKind AreaKind;
        public EffectWindow Window = new EffectWindow();
    }
}
