#nullable enable
using System;

namespace Goa2.Domain
{
    [Serializable]
    public sealed class DiscardReaction
    {
        public string Id = "", CauseActionId = "", SourceCardId = "", EffectId = "", DiscardedCardId = "", Reason = "";
        public int ControllerSeat;
    }

    [Serializable]
    public sealed class DiscardReactionFrame
    {
        public DiscardReaction Reaction = new DiscardReaction();
        public CardExecution? ParentExecution;
        public PendingChoice? ParentPending;
        public Phase ParentPhase;
        public int? ParentActiveSeat;
    }
}
