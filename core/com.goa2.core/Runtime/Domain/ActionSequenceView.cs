#nullable enable
using System.Collections.Generic;

namespace Goa2.Domain
{
    // Read-only public presentation facts. Never used to execute or order commands.
    public sealed class ActionSequenceView
    {
        public string Id = "", FocusId = "";
        public int Round, Turn;
        public List<ActionCardView> Cards = new List<ActionCardView>();
    }
    public sealed class ActionCardView
    {
        public string Id = "", ParentId = "", CardId = "", Role = "main";
        public int Seat, Initiative;
        public bool Started, Resolved;
        public long Sequence;
        public List<string> Results = new List<string>();
        public bool IsMain => Role == "main";
    }
}
