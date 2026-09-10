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
            if (!view.UpgradeOptions.Any(o => o.CardId == upgradeCardId && o.Color == upgradeColor)) upgradeCardId = "";
        }
        private void BuildUpgradeCards(VisualElement parent, GameView view)
        {
            var row = Box("hand-row"); parent.Add(row);
            var options = view.UpgradeOptions.Where(o => o.Color == upgradeColor).ToList();
            foreach (var option in options)
            {
                string id = option.CardId; var card = catalog.Card(id);
                var tile = Button("", () => { upgradeCardId = id; showDebug = false; Render(); }, "hand-card", "upgrade-card-" + id);
                tile.AddToClassList("color-" + card.Color);
                if (option == options.Last()) tile.AddToClassList("last-card");
                if (upgradeCardId == id) tile.AddToClassList("chosen");
                SeatLabel(tile, card.Name + " · " + option.CardLevel + "级", "card-name", 6, 36);
                SeatLabel(tile, card.PrimaryCategory + " " + (card.Exclamation ? "!" : card.PrimaryValue.ToString()) + SubtypeText(card), "body", 43, 27);
                SeatLabel(tile, "先 " + card.Initiative + " · 移 " + Number(card.SecondaryMovement) + " / 防 " + Number(card.SecondaryDefense), "tiny", 77, 25);
                SeatLabel(tile, "选此牌：永久" + option.Bonus + " +1", "card-zone", 107, 25);
                SeatLabel(tile, "来自未选的“" + catalog.Card(option.RejectedCardId).Name + "”", "tiny", 132, 32);
                row.Add(tile);
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
            parent.Add(Text("第 " + option.HeroLevel + " 级升级：替换“" + catalog.Card(option.PreviousCardId).Name + "”。从下方两张候选中选一张。", "body"));
            var chosen = view.UpgradeOptions.SingleOrDefault(o => o.CardId == upgradeCardId);
            if (chosen == null) return;
            RenderCardDetail(parent, catalog.Card(chosen.CardId));
            parent.Add(Text("获得永久" + chosen.Bonus + " +1，来自未选候选“" + catalog.Card(chosen.RejectedCardId).Name + "”。", "body"));
            Confirm(parent, "确认升级为 " + catalog.Card(chosen.CardId).Name, () => Submit(CommandKind.ChooseUpgrade, chosen.CardId));
        }
        private bool RenderRoundMinionChoice(VisualElement parent, GameView view)
        {
            if (view.Pending?.Kind != "round_minion_removal") return false;
            parent.Add(Text("轮末小兵战斗", "section-title"));
            parent.Add(Text(PlayerName(view.Pending.ChooserSeat) + "还须移除 " + view.RemainingMinionRemovals + " 名本队小兵。先处理非重型；轮末移除没有金币。", "body"));
            if (view.Pending.ChooserSeat != seat) return true;
            var target = chosenCell.HasValue ? view.Units.SingleOrDefault(u => u.Position == chosenCell.Value && view.RoundMinionRemovals.Contains(u.Id)) : null;
            if (target == null) parent.Add(Text("点击地图高亮的小兵后确认。", "body"));
            else Confirm(parent, "确认移除 " + MinionName(target), () => Submit(CommandKind.ChooseRoundMinionRemoval, target.Id));
            return true;
        }
        private void RenderPermanentStats(VisualElement parent, GameView view)
        {
            if (view.OwnUpgradeHistory.Count > 0)
            {
                var box = Box("event-box"); parent.Add(box);
                box.Add(Text("永久加成", "section-title"));
                box.Add(Text(string.Join(" · ", view.OwnUpgradeHistory.GroupBy(h => h.Bonus).Select(g => g.Key + " +" + g.Sum(h => h.Amount))), "body"));
                box.Add(Button(showBonusSources ? "收起加成来源" : "查看加成来源", () => { showBonusSources = !showBonusSources; Render(); }, "quiet-button", "bonus-sources"));
                if (showBonusSources) foreach (var record in view.OwnUpgradeHistory)
                    box.Add(Text("Lv." + record.HeroLevel + " · " + catalog.Card(record.RejectedCardId).Name + " → " + record.Bonus + " +" + record.Amount, "tiny"));
            }
            string? purple = view.Players[seat].PurpleCardId;
            if (purple != null)
            {
                parent.Add(Text("紫卡 · 已获得", "section-title"));
                RenderCardDetail(parent, catalog.Card(purple));
                parent.Add(Text("持续文字待实装；紫卡不进入暗选手牌。", "tiny"));
            }
        }
    }
}
