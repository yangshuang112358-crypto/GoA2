#nullable enable
using System;

namespace Goa2.Domain
{
    [Serializable]
    public sealed class BeforeActionFrame
    {
        public string Id = "", SourceCardId = "", ProgramId = "", TargetUnitId = "", Stage = "target";
        public int ControllerSeat, ProgramVersion;
        public CommandKind ResumeKind;
        public string ResumeValue = "";
        public Hex ResumeDestination;
        public MoveMode ResumeMoveMode;
        public Phase ParentPhase;
        public int? ParentActiveSeat;
        public PendingChoice? ParentPending;
        public CardExecution? ParentExecution;
    }
}
