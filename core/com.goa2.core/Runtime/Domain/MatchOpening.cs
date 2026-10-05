#nullable enable
using System;
using System.Collections.Generic;
namespace Goa2.Domain
{
    [Serializable]
    public sealed class MatchOpening
    {
        public int HostSeat;
        public int TossNumber=1;
        public string Purpose="draft",Status="throwing";
        public Team? FirstTeam;
        public Team? Result;
        public bool DraftComplete,OpeningComplete;
        public List<int> RerollVotes=new List<int>();
        public string FinalPose="";
        public string TossId=>Purpose+":"+TossNumber;
    }
}
