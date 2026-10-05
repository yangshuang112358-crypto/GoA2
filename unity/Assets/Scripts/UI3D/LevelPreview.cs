using System;
namespace Goa2.Presentation.UI3D
{
    // Presentation forecast only: never spends gold or changes the authoritative level.
    public static class LevelPreview
    {
        public static (int Level,int Gold,int Cost) Progress(int level,int gold)
        {
            int result=Math.Max(1,Math.Min(8,level)),remaining=Math.Max(0,gold);
            while(result<8 && remaining>=result){remaining-=result;result++;}
            return (result,result==8?0:remaining,result==8?0:result);
        }
        public static int Target(int level,int gold)=>Progress(level,gold).Level;
        public static float ArrowY(float age)
        {
            // Rise fully out, re-enter at the baseline, then decelerate to rest.
            if(age<.92f)return 101-78*Smooth(age/.92f);
            if(age<1.10f)return 23;
            return 101-32*Smooth((age-1.10f)/.90f);
        }
        public static float ArrowOpacity(float age)=>age<.92f?1-Smooth((age-.65f)/.27f):age<1.10f?0:Smooth((age-1.10f)/.25f);
        private static float Smooth(float t){t=Math.Max(0,Math.Min(1,t));return t*t*t*(t*(t*6-15)+10);}
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
