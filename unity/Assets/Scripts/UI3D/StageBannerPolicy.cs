using System;
using Goa2.Domain;
namespace Goa2.Presentation.UI3D
{
    public static class StageBannerPolicy
    {
        public const float Duration = 5f;
        public static string Detail(GameView view)
        {
            string kind=view.Pending?.Kind ?? "";
            if(view.Winner.HasValue || view.Phase==Phase.Finished)return "本局已结束，可查看战场与对局记录。";
            if(view.RoundEndStage=="upgrades")return "选择技能升级方向，获得对应永久被动。";
            if(kind=="defense")return "被攻击的英雄选择防御方式。";
            if(kind=="hero_respawn")return "为被击败的英雄选择合法出生点。";
            if(kind.Contains("discard"))return "当前响应英雄按效果要求处理手牌。";
            if(kind=="minion_spawn" || kind=="minion_return")return "按当前规则为小兵安排回归位置。";
            if(kind=="round_minion_removal" || kind=="action_minion_removal")return "比较双方兵力，处理小兵移除与战线推进。";
            if(kind=="initiative")return "由当前有权选择的队长安排同先攻英雄顺序。";
            return view.Phase switch {
                Phase.HeroSelection=>"每位玩家选择本局使用的英雄。",
                Phase.Deployment=>"双方队长选择出生点。",
                Phase.Planning=>"每位玩家秘密选择本回合使用的卡牌。",
                Phase.RoundEnd=>"结算本轮效果、金币升级与下一轮准备。",
                _=>"当前英雄选择并执行卡牌行动。"
            };
        }
        public static (string key,string title) Describe(GameView view,Func<int,string> hero)
        {
            if(view.Winner.HasValue || view.Phase==Phase.Finished)return ("finished","对局结束");
            string prefix=view.Round+":"+view.Turn+":";
            if(view.RoundEndStage=="upgrades")return (prefix+"upgrades","升级阶段");
            string kind=view.Pending?.Kind ?? "";
            if(kind=="defense")return (prefix+"defense:"+view.Pending!.Id,"战斗阶段\n"+hero(view.Pending.ChooserSeat)+"防御");
            if(kind=="hero_respawn")return (prefix+"respawn:"+view.Pending!.Id,"复活阶段");
            if(kind.Contains("discard"))return (prefix+"discard:"+view.Pending!.Id,"战斗阶段\n"+hero(view.Pending.ChooserSeat)+"弃牌");
            if(kind=="round_minion_removal" || kind=="action_minion_removal" || kind=="minion_spawn" || kind=="minion_return")return (prefix+"minions:"+kind,"小兵战斗阶段");
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
