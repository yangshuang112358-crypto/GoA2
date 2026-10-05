using System;
using System.Linq;
using Goa2.Domain;
namespace Goa2.Presentation.UI3D
{
    public static class UpgradeWheelLayout
    {
        // Each group: left route, current card, right route. Purple is a single claim.
        public static float Angle(int groups,int group,int branch)
        {
            if(groups<1 || groups>3 || group<0 || group>=groups || branch<0 || branch>2)throw new ArgumentOutOfRangeException();
            return -90+group*360f/groups+(branch-1)*120f/groups;
        }
        public static int Remaining(GameView view,int seat)
        {
            var option=view.UpgradeOptions.FirstOrDefault();
            var player=view.Players.FirstOrDefault(p=>p.Seat==seat);
            // Core creates contiguous pending levels when paying at round end.
            return option==null || player==null ? 0 : Math.Max(0,player.Level-option.HeroLevel+1);
        }
    }
}
