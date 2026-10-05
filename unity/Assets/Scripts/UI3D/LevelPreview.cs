using System;
namespace Goa2.Presentation.UI3D
{
    // Presentation forecast only: never spends gold or changes the authoritative level.
    public static class LevelPreview
    {
        public static int Target(int level,int gold)
        {
            int result=Math.Max(1,Math.Min(8,level)),remaining=Math.Max(0,gold);
            while(result<8 && remaining>=result){remaining-=result;result++;}
            return result;
        }
        public sealed class Motion
        {
            public int Count;
            public float Changed=-100;
            private bool initialized;
            public void Observe(int count,float now)
            {
                if(initialized && Count!=count)Changed=now;
                Count=count;initialized=true;
            }
        }
    }
}
