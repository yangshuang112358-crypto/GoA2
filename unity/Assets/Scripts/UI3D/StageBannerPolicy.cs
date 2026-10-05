using System;
using Goa2.Domain;
namespace Goa2.Presentation.UI3D
{
    public static class StageBannerPolicy
    {
        public static (string key,string title) Describe(GameView view,Func<int,string> hero)
        {
            if(view.Winner.HasValue || view.Phase==Phase.Finished)return ("finished","对局结束");
            string prefix=view.Round+":"+view.Turn+":";
            if(view.RoundEndStage=="upgrades")return (prefix+"upgrades","升级阶段");
            string kind=view.Pending?.Kind ?? "";
            if(kind=="defense")return (prefix+"defense:"+view.Pending!.Id,"战斗阶段\n"+hero(view.Pending.ChooserSeat)+"防御");
            if(kind=="round_minion_removal" || kind=="action_minion_removal" || kind=="minion_spawn" || kind=="minion_return")return (prefix+"minions","小兵战斗阶段");
            if(view.Phase==Phase.Planning)return (prefix+"planning","暗选阶段");
            if(view.Phase==Phase.HeroSelection)return ("heroes","选择英雄阶段");
            if(view.Phase==Phase.Deployment)return ("deployment","部署阶段");
            if(view.Phase==Phase.RoundEnd)return (prefix+"round-end","轮末结算阶段");
            if(view.ActiveSeat.HasValue)return (prefix+"action:"+view.ActiveSeat,"战斗阶段\n"+hero(view.ActiveSeat.Value)+"行动");
            if(kind=="initiative")return (prefix+"initiative","战斗阶段\n决定同先攻顺序");
            return (prefix+"choice","战斗阶段");
        }
    }
}
