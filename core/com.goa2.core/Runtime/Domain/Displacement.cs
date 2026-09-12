#nullable enable
using System.Collections.Generic;

namespace Goa2.Domain
{
    // A transient rule result. The applied path and source are stored in GameEvent.
    public sealed class PushResult
    {
        public List<Hex> Path = new List<Hex>();
        public string StopReason = "";
    }
}
