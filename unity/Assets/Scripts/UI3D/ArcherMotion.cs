#nullable enable
using UnityEngine;

namespace Goa2.Presentation.UI3D
{
    // Visual clock, retained with the unit across board rebuilds. It never submits actions.
    public sealed class ArcherMotion
    {
        public const float PrepareDuration=1.7f;
        public float Progress {get;private set;}
        public float Weight {get;private set;}
        public bool Released {get;private set;}
        public float ReleaseAge {get;private set;}=-1;
        public long ShotId {get;private set;}=-1;
        private float last=-1,releaseTime=-1;

        public void Advance(float now,bool requested,long shotId=-1,float releaseAt=float.PositiveInfinity)
        {
            float dt=last<0?0:Mathf.Max(0,now-last);last=now;
            if(shotId>=0 && shotId!=ShotId)
            {
                // The first shot can reuse a bow already drawn while waiting for defense.
                // A later shot must fetch a new arrow after the previous follow-through.
                if(Released){Progress=0;dt=0;}
                ShotId=shotId;Released=false;releaseTime=-1;
            }
            else if(shotId<0 && requested && Released && Weight<=.001f)
            {Progress=0;Released=false;releaseTime=-1;ShotId=-1;dt=0;}
            if(shotId>=0 && now>=releaseAt && !Released)
            {Released=true;releaseTime=releaseAt;Progress=1;}
            ReleaseAge=Released?Mathf.Max(0,now-releaseTime):-1;
            if(Released)
            {
                // Keep the bow extended briefly after release, then lower both arms together.
                Weight=1-Ease((ReleaseAge-.20f)/.82f);
                if(Weight<=.001f)Progress=0;
            }
            else
            {
                // Cancelling retraces the current fetch/nock/draw, rather than restarting at t=0.
                Progress=Mathf.MoveTowards(Progress,requested?1:0,dt/(requested?PrepareDuration:1.15f));
                Weight=Ease(Progress/.20f);
            }
        }

        public static float Ease(float value)
        {float t=Mathf.Clamp01(value);return t*t*t*(t*(t*6-15)+10);}
        public static float Segment(float progress,float start,float end)=>Ease((progress-start)/(end-start));
    }
}
