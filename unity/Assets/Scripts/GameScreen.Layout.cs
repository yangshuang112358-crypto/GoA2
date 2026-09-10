#nullable enable
using System;
using System.Linq;
using Goa2.Domain;
using UnityEngine;
using UnityEngine.UIElements;

namespace Goa2.Presentation
{
    public sealed partial class GameScreen
    {
        private bool leftExpanded = true, rightExpanded = true, topExpanded = true, bottomExpanded = true;
        private bool showDebug;
        private readonly BoardViewport viewport = new BoardViewport();
        private HexBoard? board;
        private void Update()
        {
            if (session == null || galleryOpen || publicCardsOpen || newMatchPending || IsEditingText()) return;
            if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1)) SwitchSeat(0);
            if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2)) SwitchSeat(1);
            if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3)) SwitchSeat(2);
            if (Input.GetKeyDown(KeyCode.Alpha4) || Input.GetKeyDown(KeyCode.Keypad4)) SwitchSeat(3);
            if (Input.GetKeyDown(KeyCode.Home)) board?.ResetView();
        }
        private bool IsEditingText()
        {
            var focused = root.panel?.focusController?.focusedElement as VisualElement;
            for (var element = focused; element != null; element = element.parent)
                if (element is TextField || element is IntegerField || element is FloatField) return true;
            return false;
        }
        private void SwitchSeat(int next)
        {
            seat = next; ClearPending();
            debugUnitId = renderedView.Units.FirstOrDefault(u => u.Seat == seat)?.Id ?? "";
            notice = "正在操控" + PlayerName(seat) + "。"; Render();
        }
        private static Color CardColor(string color)
        {
            string hex = color switch { "gold" => "#e4c17d", "silver" => "#c5d1d8", "red" => "#df8a96", "green" => "#72c9a5", "blue" => "#75b8ef", _ => "#b5a0df" };
            ColorUtility.TryParseHtmlString(hex, out var value); return value;
        }
        private void BuildLayout(GameView view)
        {
            var shell = Box("shell"); root.Add(shell);
            var header = Box("header"); shell.Add(header);
            var brand = Box("brand"); brand.Add(Text("GOA II", "brand-title")); brand.Add(Text("GOA2V1 · 开发版本", "eyebrow")); header.Add(brand);
            var phase = Box("phase-banner");
            phase.Add(Text("第 " + view.Round + " 轮 · 回合 " + view.Turn + "/4", "muted"));
            phase.Add(Text(PhaseName(view.Phase), "phase-title")); header.Add(phase);
            var controls = Box("header-controls"); header.Add(controls);
            controls.Add(Button(view.Sandbox ? (view.QuickSelection ? "测试 · 选完揭示" : "测试 · 手动确认") : "正式确认", () => { rightExpanded = true; showDebug = true; Render(); }, "mode-button"));
            controls.Add(Button("图鉴 108", () => { galleryOpen = true; galleryHero = catalog.Heroes[0].Id; Render(); }, "quiet-button"));
            controls.Add(Button("保存", Save, "quiet-button")); controls.Add(Button("读取", Load, "quiet-button"));
            controls.Add(Button("新对局", () => { newMatchPending = true; Render(); }, "quiet-button"));
            var workspace = Box("workspace"); shell.Add(workspace);
            BuildRoster(workspace, view);
            var center = Box("center-column"); workspace.Add(center);
            BuildRevealedStrip(center, view);
            BuildBoard(center, view);
            BuildHand(center, view);
            BuildRightPanel(workspace, view);
            var footer = Box("footer"); footer.Add(Text(notice, "tiny"));
            footer.Add(Text("1—4 切换角色 · 滚轮缩放 · 中/右键拖动", "tiny")); shell.Add(footer);
        }
        private void BuildRoster(VisualElement parent, GameView view)
        {
            var panel = Box(leftExpanded ? "left-panel" : "collapsed-side"); parent.Add(panel);
            if (!leftExpanded)
            {
                panel.Add(Button("▶", () => { leftExpanded = true; Render(); }, "edge-button", "toggle-left"));
                for (int i = 0; i < 4; i++) { int next = i; panel.Add(Button((i + 1).ToString(), () => SwitchSeat(next), "edge-button")); }
                return;
            }
            var heading = Box("panel-heading"); heading.Add(Text("角色 · 1 2 3 4", "section-title"));
            heading.Add(Button("◀", () => { leftExpanded = false; Render(); }, "edge-button", "toggle-left")); panel.Add(heading);
            var scroll = new ScrollView { name = "goa-scroll-roster" }; scroll.AddToClassList("roster-scroll"); panel.Add(scroll);
            foreach (var player in view.Players)
            {
                int target = player.Seat;
                var card = Button("", () => SwitchSeat(target), "seat-card");
                card.name = "seat-" + (target + 1);
                card.style.height = player.DiscardColors.Count > 0 ? 174 : 136;
                card.style.minHeight = player.DiscardColors.Count > 0 ? 174 : 136;
                card.AddToClassList(player.Team == Team.Blue ? "blue-seat" : "red-seat");
                if (seat == target) card.AddToClassList("selected-seat");
                string captain = target == view.BlueCaptain || target == view.RedCaptain ? " · 队长" : "";
                SeatLabel(card, (player.Team == Team.Blue ? "蓝队" : "红队") + " / " + (target + 1) + captain, "eyebrow", 5, 22);
                SeatLabel(card, HeroName(player.HeroId) + (view.ActiveSeat == target ? " · 行动" : ""), "seat-name", 29, 30);
                SeatLabel(card, "Lv." + player.Level + "   " + player.Gold + " 金   手牌 " + player.HandCount, "tiny", 63, 24);
                var rounds = Box("round-dots"); card.Add(rounds);
                for (int turn = 1; turn <= 4; turn++)
                {
                    int cycle = turn;
                    var play = player.Plays.LastOrDefault(p => p.Round == view.Round && p.Turn == cycle);
                    var dot = Box("round-dot");
                    dot.tooltip = play == null ? "第 " + turn + " 回合 · 尚未出牌" : "第 " + turn + " 回合 · " + catalog.Card(play.CardId).Name;
                    if (play != null) { dot.style.backgroundColor = CardColor(play.Color); dot.AddToClassList("filled-dot"); }
                    rounds.Add(dot);
                }
                if (player.DiscardColors.Count > 0)
                {
                    var discards = Box("discard-dots"); card.Add(discards); discards.Add(Text("弃", "tiny"));
                    foreach (string color in player.DiscardColors)
                    {
                        var dot = Box("discard-dot"); dot.style.backgroundColor = CardColor(color); dot.tooltip = "弃牌 · " + ColorName(color); discards.Add(dot);
                    }
                }
                scroll.Add(card);
            }
            var team = Box("team-info");
            team.Add(Text("水晶   蓝 " + view.BlueCrystal + " : " + view.RedCrystal + " 红", "section-title"));
            team.Add(Text("决策币 · " + (view.DecisionCoin == Team.Blue ? "蓝队" : "红队"), "body"));
            team.Add(Text("战区 · " + RegionName(view.CombatRegion), "body")); scroll.Add(team);
        }
        private static string ColorName(string color) => color switch { "gold" => "金", "silver" => "银", "red" => "红", "green" => "绿", "blue" => "蓝", _ => "紫" };
        private void BuildRevealedStrip(VisualElement parent, GameView view)
        {
            var panel = Box(topExpanded ? "revealed-panel" : "collapsed-row"); parent.Add(panel);
            var heading = Box("panel-heading"); panel.Add(heading);
            var latest = view.Players.SelectMany(p => p.Plays).OrderByDescending(p => p.Round).ThenByDescending(p => p.Turn).FirstOrDefault();
            var title = Text("已揭示牌" + (latest == null ? "" : " · " + latest.Round + "轮" + latest.Turn + "回合"), "section-title");
            title.name = "revealed-heading"; heading.Add(title);
            if (topExpanded) heading.Add(Button("全部记录", () => { publicCardsOpen = true; Render(); }, "compact-button"));
            heading.Add(Button(topExpanded ? "▲" : "▼ 展开出牌区", () => { topExpanded = !topExpanded; Render(); }, "edge-button", "toggle-top"));
            if (!topExpanded) return;
            var row = Box("revealed-row"); panel.Add(row);
            foreach (var player in view.Players)
            {
                var play = latest == null ? null : player.Plays.LastOrDefault(p => p.Round == latest.Round && p.Turn == latest.Turn);
                var tile = Box("revealed-card"); row.Add(tile);
                tile.Add(Text((player.Seat + 1) + " · " + HeroName(player.HeroId), "eyebrow"));
                if (play == null) { tile.Add(Text(latest == null ? "尚未揭示" : "本回合未出牌", "muted")); continue; }
                var card = catalog.Card(play.CardId); tile.style.borderTopColor = CardColor(card.Color);
                if (view.ActiveSeat == player.Seat) tile.AddToClassList("active-public-card");
                tile.Add(Text(card.Name + "  " + card.Initiative, "public-card-name"));
                tile.Add(Text(card.PrimaryCategory + " " + (card.Exclamation ? "!" : card.PrimaryValue.ToString()) + SubtypeText(card), "tiny"));
                var text = new ScrollView { name = "goa-scroll-public-" + player.Seat + "-" + play.Round + "-" + play.Turn };
                text.AddToClassList("public-card-text"); text.Add(Text(card.Text, "card-rules")); tile.Add(text);
                tile.Add(Text("移 " + Number(card.SecondaryMovement) + " · 防 " + Number(card.SecondaryDefense), "tiny"));
            }
        }
        private void BuildBoard(VisualElement parent, GameView view)
        {
            var field = Box("field"); parent.Add(field);
            var heading = Box("field-header"); field.Add(heading);
            heading.Add(Text("亚特兰蒂斯 · 254格", "section-title"));
            var tools = Box("map-controls"); heading.Add(tools);
            tools.Add(Button("−", () => board?.ZoomAtCenter(.8f), "compact-button"));
            tools.Add(Button("全图", () => board?.ResetView(), "compact-button"));
            tools.Add(Button("＋", () => board?.ZoomAtCenter(1.25f), "compact-button"));
            var targets = LegalCells(view);
            board = new HexBoard(catalog, view, targets, chosenCell, cell =>
            {
                if (!targets.Contains(cell)) { notice = "此格不可用于当前操作。"; return; }
                chosenCell = cell; notice = "已选地图格 " + cell + "，确认后应用。"; Render();
            }, cell => cellInfo.text = RegionName(cell.Region) + " · " + cell.Position + (targets.Contains(cell.Position) ? " · 可选" : ""), viewport);
            board.ViewportChanged = RequestCapture;
            field.Add(board);
            cellInfo = Text(targets.Count == 0 ? "滚轮缩放 · 中/右键拖动 · Home全图" : targets.Count + " 个合法目标 · 点击后确认", "tiny");
            cellInfo.AddToClassList("board-footer"); field.Add(cellInfo);
        }
        private void BuildHand(VisualElement parent, GameView view)
        {
            var panel = Box(bottomExpanded ? "hand" : "collapsed-row"); parent.Add(panel);
            var heading = Box("panel-heading"); panel.Add(heading);
            heading.Add(Text("手牌 · " + PlayerName(seat), "section-title"));
            heading.Add(Button(bottomExpanded ? "▼" : "▲ 展开手牌", () => { bottomExpanded = !bottomExpanded; Render(); }, "edge-button", "toggle-bottom"));
            if (!bottomExpanded) return;
            if (view.OwnCards.Count == 0) { panel.Add(Text("选择英雄后获得五张起始牌", "empty-hand")); return; }
            var row = Box("hand-row"); panel.Add(row);
            foreach (var instance in view.OwnCards)
            {
                var card = catalog.Card(instance.CardId);
                var tile = Button("", () =>
                {
                    if (view.Phase == Phase.Planning && !view.Players[seat].Confirmed && (instance.Zone == CardZone.InHand || instance.Zone == CardZone.Selected)) Submit(CommandKind.SelectCard, card.Id);
                    else { galleryHero = card.HeroId; galleryOpen = true; Render(); }
                }, "hand-card");
                tile.name = "hand-" + card.Color;
                tile.AddToClassList("color-" + card.Color);
                if (instance == view.OwnCards.Last()) tile.AddToClassList("last-card");
                if (instance.Zone == CardZone.Selected) tile.AddToClassList("chosen");
                if (instance.Zone == CardZone.PlayedResolved || instance.Zone == CardZone.Discarded) tile.AddToClassList("spent");
                SeatLabel(tile, card.Name, "card-name", 6, 45);
                SeatLabel(tile, card.PrimaryCategory + " " + (card.Exclamation ? "!" : card.PrimaryValue.ToString()), "body", 53, 25);
                SeatLabel(tile, "先 " + card.Initiative + " · 移 " + Number(card.SecondaryMovement) + " / 防 " + Number(card.SecondaryDefense), "tiny", 82, 43);
                string status = instance.Zone == CardZone.Selected ? (view.QuickSelection ? "已选 · 等待其他人" : view.Players[seat].Confirmed ? "已确认" : "已选 · 待确认") : ZoneName(instance);
                SeatLabel(tile, status, "card-zone", 130, 29); row.Add(tile);
            }
        }
        private void BuildRightPanel(VisualElement parent, GameView view)
        {
            var panel = Box(rightExpanded ? "right-panel" : "collapsed-side"); parent.Add(panel);
            if (!rightExpanded) { panel.Add(Button("◀", () => { rightExpanded = true; Render(); }, "edge-button", "toggle-right")); return; }
            var heading = Box("panel-heading"); panel.Add(heading);
            heading.Add(Button("行动", () => { showDebug = false; debugTeleport = false; ClearPending(); Render(); }, showDebug ? "tab-button" : "active-tab"));
            heading.Add(Button("调试", () => { showDebug = true; Render(); }, showDebug ? "active-tab" : "tab-button"));
            heading.Add(Button("▶", () => { rightExpanded = false; Render(); }, "edge-button", "toggle-right"));
            var scroll = new ScrollView { name = "goa-scroll-right-" + (showDebug ? "debug" : "action") }; scroll.AddToClassList("sidebar"); panel.Add(scroll);
            if (showDebug) RenderDebugPanel(scroll, view); else RenderSidebar(scroll, view);
        }
    }
}
