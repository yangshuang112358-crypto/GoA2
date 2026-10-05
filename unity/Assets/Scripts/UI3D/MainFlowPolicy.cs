#nullable enable
using System;
using System.Linq;
using Goa2.Domain;
namespace Goa2.Presentation.UI3D
{
    // Public summary is seat independent. Personal instruction uses only the received view.
    public static class MainFlowPolicy
    {
        public static string Step(string kind) => kind switch {
            "initiative" => "选择同先攻出牌顺序", "defense" => "选择防御牌或不防御",
            "forced_discard" => "选择要弃置的牌", "optional_discard" => "选择弃牌或跳过",
            "minion_protection" => "选择是否弃牌保护小兵", "recover_discard" => "选择要取回的牌或跳过",
            "discard_attack" => "选择弃牌堆中的攻击牌", "card_swap" => "选择换牌或跳过",
            "attack_target" => "选择攻击目标", "effect_target" => "选择技能目标",
            "effect_minion" => "选择要移除的小兵", "effect_move" => "选择移动落点",
            "placement" => "选择放置位置", "primary_option" => "选择卡牌效果分支",
            "gold_transfer" => "选择经验来源和数量", "hero_respawn" => "选择复活位置",
            "round_minion_removal" => "选择小兵战斗移除对象", "action_minion_removal" => "选择小兵战斗移除对象",
            "minion_spawn" => "安排小兵出生", "minion_return" => "安排小兵回归",
            "spawn_order_unresolved" => "选择出生顺序", _ => "完成当前选择"
        };
        public static string Summary(GameView view, Func<int,string> hero)
        {
            if(view.Winner.HasValue || view.Phase==Phase.Finished)return "对局已结束";
            if(view.Pending!=null)return hero(view.Pending.ChooserSeat)+" · "+Step(view.Pending.Kind);
            if(view.RoundEndStage=="upgrades")return "全体玩家 · 英雄升级";
            if(view.Phase==Phase.Action && view.ActiveSeat.HasValue)return hero(view.ActiveSeat.Value)+" · 执行卡牌行动";
            return view.Phase switch {Phase.HeroSelection=>"全体玩家 · 选择英雄",Phase.Deployment=>"双方队长 · 安排出生",Phase.Planning=>"全体玩家 · 暗选卡牌",Phase.RoundEnd=>"轮末结算",_=>"正在处理当前行动"};
        }
        public static bool NeedsInput(GameView view,int seat)
        {
            if(view.Winner.HasValue || view.Phase==Phase.Finished)return false;
            if(view.Pending!=null)return view.Pending.ChooserSeat==seat;
            if(view.CanResolveRoundEnd)return true;
            if(view.RoundEndStage=="upgrades")return view.UpgradeOptions.Count>0;
            if(view.Phase==Phase.Action)return view.ActiveSeat==seat;
            if(view.Phase==Phase.HeroSelection)return view.Players.FirstOrDefault(p=>p.Seat==seat)?.HeroId==null;
            if(view.Phase==Phase.Deployment)return view.Deployments.Count>0;
            if(view.Phase==Phase.Planning)return view.Players.Any(p=>p.Seat==seat && !p.Confirmed) && (!view.QuickSelection || !view.OwnCards.Any(c=>c.Zone==CardZone.Selected));
            return false;
        }
        public static string Instruction(GameView view,int seat)
        {
            if(!NeedsInput(view,seat))return view.Phase==Phase.Finished ? "可自由查看战场与记录" : "等待其他玩家操作，可自由观看";
            if(view.Pending!=null)return Step(view.Pending.Kind)+(view.CanDeclineRetaliationDiscard ? "，或选择被击败" : "");
            if(view.CanResolveRoundEnd)return "开始轮末结算";
            if(view.UpgradeOptions.Count>0)return "选择升级技能";
            return view.Phase switch {Phase.HeroSelection=>"选择英雄",Phase.Deployment=>"安排队员出生位置",Phase.Planning=>view.OwnCards.Any(c=>c.Zone==CardZone.Selected)?"决定本回合出牌，也可换选":"在自己的技能环选择本回合卡牌",Phase.Action=>"选择主要行动、移动或放弃此牌",_=>"完成当前选择"};
        }
    }
}
