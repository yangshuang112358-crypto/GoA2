#nullable enable
using System.Linq;
using Goa2.Domain;
using UnityEngine.UIElements;

namespace Goa2.Presentation
{
    public sealed partial class GameScreen
    {
        private string upgradeColor = "", upgradeCardId = "";
        private bool showBonusSources;
        private void PrepareUpgradeSelection(GameView view)
        {
            if (!view.UpgradeOptions.Any(o => o.Color == upgradeColor)) upgradeColor = view.UpgradeOptions.FirstOrDefault()?.Color ?? "";
            if (!view.UpgradeOptions.Any(o => o.CardId == upgradeCardId)) upgradeCardId = "";
        }
        private void BuildUpgradeCards(VisualElement parent, GameView view)
        {
            var scroll=new ScrollView {name="goa-scroll-upgrades"};scroll.style.flexGrow=1;parent.Add(scroll);
            bool purple=view.UpgradeOptions[0].Color=="purple";
            foreach(string color in purple ? new[]{"purple"} : new[]{"red","green","blue"})
            {
                var row=Box("upgrade-row");row.name="upgrade-row-"+color;scroll.Add(row);
                int level=view.UpgradeOptions[0].CardLevel;
                foreach(var card in catalog.Cards.Where(c=>c.HeroId==view.Players[seat].HeroId && c.Color==color && c.Level==level).OrderBy(c=>c.Id,System.StringComparer.Ordinal))
                {
                    string id=card.Id;var option=view.UpgradeOptions.SingleOrDefault(o=>o.CardId==id);
                    var tile=Button("",()=>{upgradeCardId=id;upgradeColor=card.Color;showDebug=false;Render();},"upgrade-card","upgrade-card-"+id);
                    tile.AddToClassList("color-"+color);tile.SetEnabled(option!=null);
                    tile.style.borderTopColor=CardColor(color);
                    if(upgradeCardId==id) tile.AddToClassList("chosen");
                    tile.Add(Text(card.Name+" · "+level+"级","card-name"));
                    CardRulesPreview(tile,card,"upgrade-rules-"+id);
                    CompactCardNumbers(tile,card,view.Players[seat],false,"upgrade-"+id);
                    tile.Add(Text(option==null ? "此色本阶段已升级" : purple ? "8级唯一紫卡 · 仍须确认" : "永久"+option.Bonus+" +1（未选卡）","card-zone"));
                    AttachCardReading(tile,card,view.Players[seat]);
                    row.Add(tile);
                }
            }
        }
        private void RenderRoundEnd(VisualElement parent, GameView view)
        {
            if (view.CanResolveRoundEnd)
            {
                parent.Add(Text("本轮四回合已结束。", "section-title"));
                parent.Add(Text("回收牌 → 小兵战斗 → 升级 → 下一轮。少兵方和升级者按提示完成各自选择。", "body"));
                parent.Add(Button("结算轮末", () => Submit(CommandKind.ResolveRoundEnd), "primary-button", "resolve-round-end"));
                return;
            }
            if (view.RoundEndStage != "upgrades") { parent.Add(Text("正在处理轮末结算。", "body")); return; }
            parent.Add(Text("英雄升级", "section-title"));
            parent.Add(Text("金币已按当前等级连续扣除。所有升级者选完后进入下一轮。", "body"));
            var seats = Box("debug-button-row"); parent.Add(seats);
            foreach (int target in view.UpgradingSeats)
            {
                int next = target;
                seats.Add(Button("席位 " + (next + 1) + " 待选", () => SwitchSeat(next), "compact-button", "upgrade-seat-" + (next + 1)));
            }
            if (view.UpgradeOptions.Count == 0) { parent.Add(Text("你已完成本轮升级，等待其他角色。", "body")); return; }
            PrepareUpgradeSelection(view);
            var colors = Box("debug-button-row"); parent.Add(colors);
            foreach (string color in view.UpgradeOptions.Select(o => o.Color).Distinct())
            {
                string selected = color;
                var button = Button(ColorName(color) + "色", () => { upgradeColor = selected; upgradeCardId = ""; Render(); }, "compact-button", "upgrade-color-" + color);
                if (color == upgradeColor) button.AddToClassList("chosen"); colors.Add(button);
            }
            var option = view.UpgradeOptions.First(o => o.Color == upgradeColor);
            parent.Add(Text(option.Color=="purple" ? "英雄8级：选择紫卡后确认领取。" : "第 "+option.HeroLevel+" 级：从三色候选中选择一张可用卡。灰色候选不可重复升级。", "body"));
            var chosen = view.UpgradeOptions.SingleOrDefault(o => o.CardId == upgradeCardId);
            if (chosen == null) return;
            RenderCardDetail(parent, catalog.Card(chosen.CardId));
            if(chosen.Color!="purple") parent.Add(Text("获得永久" + chosen.Bonus + " +1，来自未选候选“" + catalog.Card(chosen.RejectedCardId).Name + "”。", "body"));
            Confirm(parent, "确认升级为 " + catalog.Card(chosen.CardId).Name, () => Submit(CommandKind.ChooseUpgrade, chosen.CardId));
        }
        private bool RenderRoundMinionChoice(VisualElement parent, GameView view)
        {
            if (view.Pending?.Kind != "round_minion_removal") return false;
            parent.Add(Text("轮末小兵战斗", "section-title"));
            parent.Add(Text(PlayerName(view.Pending.ChooserSeat) + "还须移除 " + view.RemainingMinionRemovals + " 名本队小兵。先处理非重型；轮末移除没有金币。", "body"));
            if(view.Units.Any(u => u.Kind == "hero" && view.RoundMinionRemovals.Contains(u.Id)))
                parent.Add(Text("可选择高亮英雄承担移除：保留英雄，立即停止本次剩余移除和推进；此前移除的小兵不恢复。", "body"));
            if (view.Pending.ChooserSeat != seat) return true;
            var target = chosenCell.HasValue ? view.Units.SingleOrDefault(u => u.Position == chosenCell.Value && view.RoundMinionRemovals.Contains(u.Id)) : null;
            if (target == null) parent.Add(Text("点击地图高亮的参战单位后确认。", "body"));
            else Confirm(parent, target.Kind == "hero" ? "选择 " + PlayerName(target.Seat!.Value) + " · 保留并结束兵战" : "确认移除 " + MinionName(target), () => Submit(CommandKind.ChooseRoundMinionRemoval, target.Id));
            return true;
        }
        private void RenderPermanentStats(VisualElement parent, GameView view)
        {
            if (view.Players[seat].PermanentBonuses.Count > 0)
            {
                var box = Box("event-box"); parent.Add(box);
                box.Add(Text("永久加成", "section-title"));
                box.Add(Text(string.Join(" · ", view.Players[seat].PermanentBonuses.Select(b => b.Key + " +" + b.Value)), "body"));
                box.Add(Button(showBonusSources ? "收起加成来源" : "查看加成来源", () => { showBonusSources = !showBonusSources; Render(); }, "quiet-button", "bonus-sources"));
                if (showBonusSources) foreach (var record in view.OwnUpgradeHistory)
                    box.Add(Text("Lv." + record.HeroLevel + " · " + catalog.Card(record.RejectedCardId).Name + " → " + record.Bonus + " +" + record.Amount, "tiny"));
            }
            string? purple = view.Players[seat].PurpleCardId;
            if (purple != null)
            {
                parent.Add(Text("紫卡 · 已获得", "section-title"));
                RenderCardDetail(parent, catalog.Card(purple));
                parent.Add(Text((view.SupportedUltimateCards.Contains(purple) ? "持有能力已接入；" : "持续文字待实装；") + "紫卡不进入暗选手牌。", "tiny"));
            }
        }
    }
}
