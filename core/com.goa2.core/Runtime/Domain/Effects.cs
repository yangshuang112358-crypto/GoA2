#nullable enable
using System;

namespace Goa2.Domain
{
    public enum EffectDuration { ThisTurn, NextTurn, ThisRound }
    public enum EffectKind { MovementBoundary, SkillSuppression, NonAdjacentRangedImmunity, FriendlyBasicMinionsRanged, FriendlyAttackMinionsRanged, FriendlyAttackMinionsDual, FriendlyNearMinionDefense, FriendlyDisplacementProtection, ImmunityAndUnitTraversal, OtherEnemyActionImmunity, FriendlyMeleeDefeatPrevention, FriendlyNonHeavyDefeatPrevention, FriendlyMinionDefeatPrevention }
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
        public EffectKind Kind;
        public EffectDuration Duration;
        public EffectAreaKind AreaKind;
        public EffectWindow Window = new EffectWindow();
    }
}
