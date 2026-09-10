#nullable enable
using System.Linq;
using Goa2.Domain;
using UnityEngine.UIElements;

namespace Goa2.Presentation
{
    public sealed partial class GameScreen
    {
        private bool debugTeleport;
        private string debugUnitId = "";
        private int debugGold = 10;
        private string debugCardId = "";
        private string debugEquipmentId = "";
        private int debugCrystal = 7;
        private void RenderDebugPanel(VisualElement parent, GameView view)
        {
            parent.Add(Text("手工测试", "panel-title"));
            if (view.Phase == Phase.Finished) { RenderVictory(parent, view); return; }
            if (!view.Sandbox) { parent.Add(Text("这是普通确认对局。创建新的测试对局后可使用调试工具。", "body")); return; }
            parent.Add(Text(PlayerName(seat), "section-title"));
            var quick = new Toggle("四人选完立即揭示") { value = view.QuickSelection };
            quick.AddToClassList("debug-toggle");
            quick.RegisterValueChangedCallback(e => Submit(CommandKind.SetQuickSelection, e.newValue ? "on" : "off")); parent.Add(quick);
            var prepare = Button("自动选英雄与出生", () => Submit(CommandKind.DebugPrepare), "primary-button");
            prepare.SetEnabled(view.Phase == Phase.HeroSelection || view.Phase == Phase.Deployment); parent.Add(prepare);
            var row = Box("debug-button-row"); parent.Add(row);
            var first = Button("四人快速选牌", () => Submit(CommandKind.DebugSelectAll), "choice-button"); first.SetEnabled(view.Phase == Phase.Planning); row.Add(first);
            var random = Button("随机选牌", () => Submit(CommandKind.DebugSelectAll, "random"), "choice-button"); random.SetEnabled(view.Phase == Phase.Planning); row.Add(random);
            parent.Add(Text("金币", "section-title"));
            var coins = Box("debug-button-row"); parent.Add(coins);
            foreach (int amount in new[] { 1, 5, 10, 50 })
            {
                int change = amount; coins.Add(Button("+" + amount, () => Submit(CommandKind.DebugGold, change.ToString(), seat), "compact-button"));
            }
            var gold = new IntegerField("数值") { value = debugGold, name = "debug-gold-delta" }; gold.AddToClassList("debug-input");
            gold.RegisterValueChangedCallback(e => debugGold = e.newValue); parent.Add(gold);
            var goldControls = Box("debug-button-row"); parent.Add(goldControls);
            goldControls.Add(Button("应用金币变化", () => Submit(CommandKind.DebugGold, debugGold.ToString(), seat), "choice-button"));
            goldControls.Add(Button("设为指定金币", () => Submit(CommandKind.DebugSetGold, debugGold.ToString(), seat), "choice-button"));
            parent.Add(Text("调试传送", "section-title"));
            if (view.Units.Count > 0)
            {
                var units = view.Units.ToList();
                if (units.All(u => u.Id != debugUnitId)) debugUnitId = units.FirstOrDefault(u => u.Seat == seat)?.Id ?? units[0].Id;
                var names = units.Select(u => u.Seat.HasValue ? PlayerName(u.Seat.Value) : (u.Team == Team.Blue ? "蓝" : "红") + (u.Kind == "heavy" ? "重型" : u.Kind == "ranged" ? "远程" : "近战") + " · " + u.Position).ToList();
                var picker = new DropdownField("单位", names, units.FindIndex(u => u.Id == debugUnitId)); picker.AddToClassList("debug-input");
                picker.RegisterValueChangedCallback(_ => { debugUnitId = units[picker.index].Id; chosenCell = null; Render(); }); parent.Add(picker);
                parent.Add(Button(debugTeleport ? "结束传送选点" : "在地图选择落点", () => { ClearPending(); debugTeleport = !debugTeleport; Render(); }, "choice-button"));
                if (debugTeleport && chosenCell.HasValue) Confirm(parent, "确认调试传送 " + chosenCell.Value, () => Submit(CommandKind.DebugTeleport, debugUnitId, destination: chosenCell!.Value));
                var selectedUnit = units.First(u => u.Id == debugUnitId);
                if (selectedUnit.Kind != "hero")
                {
                    parent.Add(Text(view.RemovableMinions.Contains(selectedUnit.Id) ? "此小兵可被正常移除。" : "重型受保护；下列调试操作会绕过保护。", "tiny"));
                    var removal = Box("debug-button-row"); parent.Add(removal);
                    var remove = Button("移除小兵 · 无金币", () => Submit(CommandKind.DebugRemoveMinion, debugUnitId), "choice-button", "debug-remove-minion");
                    remove.SetEnabled(view.Phase != Phase.EffectChoice && view.Phase != Phase.Deployment); removal.Add(remove);
                    var defeat = Button("击败小兵 · 计金币", () => Submit(CommandKind.DebugDefeatMinion, debugUnitId, seat), "choice-button", "debug-defeat-minion");
                    defeat.SetEnabled(view.Phase != Phase.EffectChoice && view.Phase != Phase.Deployment && selectedUnit.Team != view.Players[seat].Team); removal.Add(defeat);
                }
                else
                {
                    var defeatHero = Button("击败该英雄 · 计奖励", () => Submit(CommandKind.DebugDefeatHero, debugUnitId, seat), "choice-button", "debug-defeat-hero");
                    defeatHero.SetEnabled(view.Phase != Phase.EffectChoice && view.Phase != Phase.Deployment && selectedUnit.Team != view.Players[seat].Team); parent.Add(defeatHero);
                }
            }
            parent.Add(Text("战线与水晶", "section-title"));
            foreach (var heavy in view.Units.Where(u => u.Kind == "heavy"))
            {
                string id = heavy.Id;
                var push = Button("移除" + (heavy.Team == Team.Blue ? "蓝" : "红") + "重型并推进", () => Submit(CommandKind.DebugRemoveMinion, id), "choice-button", heavy.Team == Team.Blue ? "debug-remove-blue-heavy" : "debug-remove-red-heavy");
                push.SetEnabled(view.Phase != Phase.EffectChoice && view.Phase != Phase.Deployment); parent.Add(push);
            }
            var crystal = new IntegerField("水晶生命") { value = debugCrystal, name = "debug-crystal" }; crystal.AddToClassList("debug-input");
            crystal.RegisterValueChangedCallback(e => debugCrystal = e.newValue); parent.Add(crystal);
            var applyCrystal = Button("设置本队水晶", () => Submit(CommandKind.DebugSetCrystal, debugCrystal.ToString(), seat), "choice-button", "debug-set-crystal");
            applyCrystal.SetEnabled(view.Phase != Phase.HeroSelection && view.Phase != Phase.Deployment); parent.Add(applyCrystal);
            parent.Add(Text("水晶为0立即结束对局；推进会清除旧兵并处理新兵出生。", "tiny"));
            parent.Add(Text("手牌 / 弃牌", "section-title"));
            var cards = view.OwnCards.Where(c => c.Zone == CardZone.InHand || c.Zone == CardZone.Selected || c.Zone == CardZone.Discarded).ToList();
            if (cards.Count > 0)
            {
                if (cards.All(c => c.CardId != debugCardId)) debugCardId = cards[0].CardId;
                var labels = cards.Select(c => ColorName(catalog.Card(c.CardId).Color) + " · " + catalog.Card(c.CardId).Name + (c.Zone == CardZone.Discarded ? "（弃）" : "")).ToList();
                var picker = new DropdownField("卡牌", labels, cards.FindIndex(c => c.CardId == debugCardId)); picker.AddToClassList("debug-input");
                picker.RegisterValueChangedCallback(_ => { debugCardId = cards[picker.index].CardId; Render(); }); parent.Add(picker);
                var controls = Box("debug-button-row"); parent.Add(controls);
                var selected = cards.First(c => c.CardId == debugCardId);
                var discard = Button("弃置所选牌", () => Submit(CommandKind.DebugDiscard, debugCardId, seat), "choice-button"); discard.SetEnabled(view.Phase != Phase.EffectChoice && selected.Zone != CardZone.Discarded); controls.Add(discard);
                var recover = Button("取回所选弃牌", () => Submit(CommandKind.DebugRecover, debugCardId, seat), "choice-button"); recover.SetEnabled(view.Phase != Phase.EffectChoice && selected.Zone == CardZone.Discarded); controls.Add(recover);
            }
            var equipment = catalog.Cards.Where(c => c.HeroId == view.Players[seat].HeroId && c.Color != "purple").ToList();
            if (equipment.Count > 0)
            {
                parent.Add(Text("装配测试牌", "section-title"));
                if (equipment.All(c => c.Id != debugEquipmentId)) debugEquipmentId = equipment[0].Id;
                var picker = new DropdownField(equipment.Select(c => ColorName(c.Color) + " · " + c.Name).ToList(), equipment.FindIndex(c => c.Id == debugEquipmentId)); picker.AddToClassList("debug-input");
                picker.RegisterValueChangedCallback(_ => debugEquipmentId = equipment[picker.index].Id); parent.Add(picker);
                var equip = Button("替换当前同色卡", () => Submit(CommandKind.DebugEquipCard, debugEquipmentId, seat), "choice-button");
                equip.SetEnabled(view.Phase == Phase.Planning && !view.Players[seat].Confirmed); parent.Add(equip);
                parent.Add(Text("仅测试装配；不表示牌效已实现。", "tiny"));
            }
            parent.Add(Text("决策币", "section-title"));
            var coinRow = Box("debug-button-row"); parent.Add(coinRow);
            coinRow.Add(Button("设为蓝队", () => Submit(CommandKind.DebugSetCoin, "blue"), "choice-button"));
            coinRow.Add(Button("设为红队", () => Submit(CommandKind.DebugSetCoin, "red"), "choice-button"));
            parent.Add(Text("流程快进", "section-title"));
            var confirm = Button("确认所有已选牌", () => Submit(CommandKind.DebugConfirmAll), "choice-button");
            confirm.SetEnabled(view.Phase == Phase.Planning); parent.Add(confirm);
            var skip = Button("跳过当前行动", () => Submit(CommandKind.DebugAdvance, "action"), "choice-button");
            skip.SetEnabled(view.Phase == Phase.Action || view.Phase == Phase.InitiativeChoice); parent.Add(skip);
            bool canAdvance = view.Phase == Phase.Planning || view.Phase == Phase.Action || view.Phase == Phase.InitiativeChoice;
            var turn = Button("跳过本回合", () => Submit(CommandKind.DebugAdvance, "turn"), "choice-button"); turn.SetEnabled(canAdvance); parent.Add(turn);
            var round = Button("快速到轮末", () => Submit(CommandKind.DebugAdvance, "round"), "choice-button"); round.SetEnabled(canAdvance); parent.Add(round);
            parent.Add(Text("快进会依次选牌并放弃行动，保留出牌记录；轮末结算仍需单独处理。", "tiny"));
        }
    }
}
