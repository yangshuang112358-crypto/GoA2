#nullable enable
using System;
using System.Linq;
using Goa2.Domain;
using Goa2.Presentation.UI3D;
using UnityEngine;
using UnityEngine.UIElements;

namespace Goa2.Presentation
{
    public sealed partial class GameScreen
    {
        private bool leftExpanded = true, rightExpanded = false, topExpanded = Environment.GetCommandLineArgs().Contains("-goa2d"), bottomExpanded = true;
        private bool showDebug;
        private readonly BoardViewport viewport = new BoardViewport();
        private BattlefieldSurface? board;
        private readonly Board3DViewport board3DViewport = new Board3DViewport();
        private void Update()
        {
            RefreshFollowControls();
            UpdatePresentationFocus();
            if(Input.GetKeyDown(KeyCode.Escape)) { if(keywordGlossaryOpen) CloseKeywordGlossary();else if(rightExpanded && !galleryOpen && !historyOpen && !newMatchPending && !debugPresetsOpen) {rightExpanded=false;showHotkeys=false;Render();}else HideCardPreview();return; }
            if(keywordGlossaryOpen) return;
            if(Input.GetKeyDown(KeyCode.F1) && session!=null && !startupFailed && !newMatchPending && !debugPresetsOpen && !IsEditingText()) { OpenKeywordGlossary(previewCard);return; }
            if (session == null || startupFailed || galleryOpen || publicCardsOpen || historyOpen || newMatchPending || debugPresetsOpen || IsEditingText()) return;
            if(Input.GetKeyDown(KeyCode.Space)) {SetCameraFollow(!cameraFollow);return;}
            if(Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) {ConfirmCurrent();return;}
            if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1)) SwitchSeat(0);
            if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2)) SwitchSeat(1);
            if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3)) SwitchSeat(2);
            if (Input.GetKeyDown(KeyCode.Alpha4) || Input.GetKeyDown(KeyCode.Keypad4)) SwitchSeat(3);
            if (Input.GetKeyDown(KeyCode.Home)) {SetCameraFollow(false);board?.ResetView();}
            if (Input.GetKeyDown(KeyCode.Q)) board?.Rotate(-1);
            if (Input.GetKeyDown(KeyCode.E)) board?.Rotate(1);
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
            var roster=root.Q<ScrollView>("goa-scroll-roster");
            var selected=roster?.Q<Button>("seat-"+(next+1));
            if(roster==null || selected==null) return;
            EventCallback<GeometryChangedEvent>? reveal=null;
            reveal=_ =>
            {
                if(selected.worldBound.height<=0 || roster.contentViewport.worldBound.height<=0) return;
                selected.UnregisterCallback(reveal);
                // Wait for this rebuilt card's layout, then reveal it once. Ordinary
                // state refreshes keep the user's scroll position and map viewport.
                roster.ScrollTo(selected);
            };
            selected.RegisterCallback(reveal);
        }
        private static Color CardColor(string color)
        {
            string hex = color switch { "gold" => "#e4c17d", "silver" => "#c5d1d8", "red" => "#df8a96", "green" => "#72c9a5", "blue" => "#75b8ef", _ => "#b5a0df" };
            ColorUtility.TryParseHtmlString(hex, out var value); return value;
        }
        private void BuildLayout(GameView view)
        {
            var screenScroll=new ScrollView(ScrollViewMode.VerticalAndHorizontal) {name="goa-scroll-screen"};screenScroll.style.flexGrow=1;root.Add(screenScroll);
            var shell = Box("shell"); shell.style.width=Mathf.Max(1280,Screen.width-18);shell.style.minWidth=Mathf.Max(1280,Screen.width-18);
            shell.style.height=Mathf.Max(1000,Screen.height-18);shell.style.minHeight=Mathf.Max(1000,Screen.height-18);shell.style.flexShrink=0;screenScroll.Add(shell);
            var header = Box("header"); header.name="match-header"; shell.Add(header);
            var brand = Box("brand"); brand.Add(Text("GOA II", "brand-title")); brand.Add(Text("GOA2V1", "eyebrow")); header.Add(brand);
            var phase = Box("phase-banner");
            phase.Add(Text("第 " + view.Round + " 轮 · 回合 " + view.Turn + "/4", "muted"));
            var stageTitle=Text(PhaseName(view), "phase-title"); stageTitle.name="match-stage"; phase.Add(stageTitle); header.Add(phase);
            var controls = Box("header-controls"); header.Add(controls);
            controls.Add(Button(view.Sandbox ? (view.QuickSelection ? "测试 · 选完揭示" : "测试 · 手动确认") : "正式确认", () => { rightExpanded = true; showDebug = true; showHotkeys=false; Render(); }, "mode-button"));
            controls.Add(Button("图鉴 108", () => { galleryOpen = true; galleryHero = catalog.Heroes[0].Id; Render(); }, "quiet-button"));
            controls.Add(Button("术语",()=>OpenKeywordGlossary(),"quiet-button","keyword-open"));
            controls.Add(Button("保存", Save, "quiet-button"));
            var load = Button("读取", Load, "quiet-button"); load.SetEnabled(!ScenarioRunning); controls.Add(load);
            var newGame = Button("新对局", () => { newMatchPending = true; Render(); }, "quiet-button"); newGame.SetEnabled(!ScenarioRunning); controls.Add(newGame);
            BuildScenarioBar(shell);
            var workspace = Box("workspace"); shell.Add(workspace);
            // Hero roster now lives on the battlefield.
            var center = Box("center-column"); center.name="battlefield-workspace"; workspace.Add(center);
            if(view.UpgradeOptions.Count==0) BuildRevealedStrip(center, view);
            BuildBoard(center, view);
            BuildHand(center, view);
            BuildRightPanel(root, view);
            var footer = Box("footer"); footer.name="status-bar";footer.Add(Text(notice, "tiny"));
            footer.Add(Text("1—4 切换角色 · Enter确认 · 空格跟随", "tiny")); shell.Add(footer);
        }
        private static string ColorName(string color) => color switch { "gold" => "金", "silver" => "银", "red" => "红", "green" => "绿", "blue" => "蓝", _ => "紫" };
        private void BuildRevealedStrip(VisualElement parent, GameView view)
        {
            var panel = Box(topExpanded ? "revealed-panel" : "collapsed-row");panel.name="revealed-zone"; parent.Add(panel);
            var heading = Box("panel-heading"); panel.Add(heading);
            var latest = view.Players.SelectMany(p => p.Plays).OrderByDescending(p => p.Round).ThenByDescending(p => p.Turn).FirstOrDefault();
            if(latest==null && topExpanded) panel.AddToClassList("empty-revealed-panel");
            var title = Text("已揭示牌" + (latest == null ? "" : " · " + latest.Round + "轮" + latest.Turn + "回合"), "section-title");
            title.name = "revealed-heading"; heading.Add(title);
            if (topExpanded) heading.Add(Button("全部记录", () => { publicCardsOpen = true; Render(); }, "compact-button"));
            heading.Add(Button(topExpanded ? "▲" : "▼ 展开出牌区", () => { topExpanded = !topExpanded; Render(); }, "edge-button", "toggle-top"));
            if (!topExpanded) return;
            var row = new ScrollView(ScrollViewMode.Horizontal) {name="goa-scroll-revealed",verticalScrollerVisibility=ScrollerVisibility.Hidden};row.AddToClassList("revealed-row");row.contentContainer.style.flexDirection=FlexDirection.Row;panel.Add(row);
            row.horizontalScroller.style.height=18;row.horizontalScroller.style.minHeight=18;
            foreach (var player in view.Players.OrderByDescending(p=> {var play=latest==null ? null : p.Plays.LastOrDefault(x=>x.Round==latest.Round && x.Turn==latest.Turn);return play==null ? int.MinValue : catalog.Card(play.CardId).Initiative+Bonus(p,"先攻");}))
            {
                var play = latest == null ? null : player.Plays.LastOrDefault(p => p.Round == latest.Round && p.Turn == latest.Turn);
                var tile = Box("revealed-card"); row.Add(tile);
                tile.name="revealed-seat-"+(player.Seat+1);
                if (play == null) { tile.Add(Text((player.Seat+1)+" · "+HeroName(player.HeroId)+" · "+(latest==null ? "尚未揭示" : "未出牌"),"muted")); continue; }
                var card = catalog.Card(play.CardId); tile.style.borderTopColor = CardColor(card.Color);
                var colorStrip=Box("card-color-stripe");colorStrip.name="revealed-color-"+(player.Seat+1);colorStrip.style.backgroundColor=CardColor(card.Color);tile.Add(colorStrip);
                if (view.ActiveSeat == player.Seat) tile.AddToClassList("active-public-card");
                tile.Add(Text((player.Seat+1)+" · "+HeroName(player.HeroId)+" · "+card.Name, "public-card-name"));
                CardRulesPreview(tile,card,"revealed-rules-"+player.Seat);
                CompactCardNumbers(tile,card,player,true,"revealed-"+player.Seat,true);
                var teamStrip=Box("team-stripe");teamStrip.name="revealed-team-"+(player.Seat+1);teamStrip.style.backgroundColor=player.Team==Team.Blue ? new Color(.15f,.45f,.95f) : new Color(.9f,.2f,.25f);tile.Add(teamStrip);
                AttachCardReading(tile,card,player);
            }
        }
        private void BuildBoard(VisualElement parent, GameView view)
        {
            var field = Box("field");field.name="battlefield-map"; parent.Add(field);
            var heading = Box("field-header"); field.Add(heading);
            heading.Add(Text("亚特兰蒂斯 · "+RegionName(view.CombatRegion), "section-title"));
            var tools = Box("map-controls"); heading.Add(tools);
            tools.Add(Button(board3DViewport.Enabled ? "2.5D / 切2D" : "2D / 切2.5D", () => { board3DViewport.Enabled = !board3DViewport.Enabled; Render(); }, "compact-button", "toggle-3d"));
            for(int i=0;i<4;i++){int target=i;var seatButton=Button((i+1).ToString(),()=>SwitchSeat(target),"compact-button","seat-"+(i+1));seatButton.tooltip=PlayerName(i);tools.Add(seatButton);}
            var ownUnit=view.Units.SingleOrDefault(u => u.Seat==seat);
            var focus=Button("定位角色",() => { if (ownUnit!=null) {SetCameraFollow(false);board?.FocusAt(ownUnit.Position);} },"compact-button","focus-hero");
            focus.SetEnabled(ownUnit!=null); tools.Add(focus);
            tools.Add(Button("−", () => board?.ZoomAtCenter(.8f), "compact-button"));
            tools.Add(Button("全图", () => {SetCameraFollow(false);board?.ResetView();}, "compact-button"));
            tools.Add(Button("＋", () => board?.ZoomAtCenter(1.25f), "compact-button"));
            var targets = LegalCells(view);
            board = new BattlefieldSurface(catalog, view, targets, chosenCell, cell =>
            {
                if (!targets.Contains(cell)) { notice = "此格不可用于当前操作。"; return; }
                chosenCell = cell; notice = "已选地图格 " + cell + "，确认后应用。"; Render();
            }, cell => cellInfo.text = BoardHint(view,RegionName(cell.Region) + " · " + cell.Position + (targets.Contains(cell.Position) ? " · 可选" : "")), viewport, SelectedEffectArea(view), board3DViewport, seat);
            board.HeroHover=ShowHeroHover;
            board.ManualPan=()=>SetCameraFollow(false);
            board.ViewportChanged = RequestCapture;
            field.Add(board);
            cellInfo = Text(BoardHint(view,targets.Count == 0 ? "滚轮缩放 · 中/右键拖动 · Home全图" : targets.Count + " 个合法目标 · 点击后确认"), "tiny");
            cellInfo.AddToClassList("board-footer"); field.Add(cellInfo);
        }
        private void BuildHand(VisualElement parent, GameView view)
        {
            PrepareUpgradeSelection(view);
            var panel = Box(bottomExpanded ? "hand" : "collapsed-row");panel.name=view.UpgradeOptions.Count>0 ? "upgrade-zone" : "hand-zone"; parent.Add(panel);
            if(view.UpgradeOptions.Count>0) panel.AddToClassList("upgrade-panel");
            var heading = Box("panel-heading"); panel.Add(heading);
            heading.Add(Text((view.UpgradeOptions.Count > 0 ? "升级候选 · " : "手牌 · ") + PlayerName(seat), "section-title"));
            heading.Add(Button(bottomExpanded ? "▼" : "▲ 展开手牌", () => { bottomExpanded = !bottomExpanded; Render(); }, "edge-button", "toggle-bottom"));
            if (!bottomExpanded) return;
            if (view.UpgradeOptions.Count > 0) { BuildUpgradeCards(panel, view); return; }
            if (view.OwnCards.Count == 0) { panel.Add(Text("选择英雄后获得五张起始牌", "empty-hand")); return; }
            var row = new ScrollView(ScrollViewMode.Horizontal) {name="goa-scroll-hand",verticalScrollerVisibility=ScrollerVisibility.Hidden};row.AddToClassList("hand-row");row.contentContainer.style.flexDirection=FlexDirection.Row;panel.Add(row);
            row.horizontalScroller.style.height=18;row.horizontalScroller.style.minHeight=18;
            foreach (var instance in view.OwnCards)
            {
                var card = catalog.Card(instance.CardId);
                var tile = Button("", () =>
                {
                    if (view.Pending?.Kind == "defense" && view.Pending.ChooserSeat == seat && view.DefenseOptions.Any(o => o.CardId == card.Id))
                    { defenseCardId = card.Id; declineDefensePending = false; showDebug = false; showHotkeys=false; Render(); }
                    else if (view.ForcedDiscardCards.Contains(card.Id) || view.OptionalDiscardCards.Contains(card.Id) || view.MinionProtectionCards.Contains(card.Id) || view.CardSwapOptions.Contains(card.Id)) { discardCardId=card.Id;declineRetaliationPending=false;showDebug=false;showHotkeys=false;Render(); }
                    else if (view.Phase == Phase.Planning && !view.Players[seat].Confirmed && (instance.Zone == CardZone.InHand || instance.Zone == CardZone.Selected)) Submit(CommandKind.SelectCard, card.Id);
                    else { galleryHero = card.HeroId; galleryOpen = true; Render(); }
                }, "hand-card");
                tile.name = "hand-" + card.Color;
                tile.AddToClassList("color-" + card.Color);
                if (instance == view.OwnCards.Last()) tile.AddToClassList("last-card");
                if (instance.Zone == CardZone.Selected || defenseCardId == card.Id || discardCardId == card.Id) tile.AddToClassList("chosen");
                if (instance.Zone == CardZone.PlayedResolved || instance.Zone == CardZone.Discarded) tile.AddToClassList("spent");
                tile.Add(Text(card.Name,"card-name"));
                CardRulesPreview(tile,card,"hand-rules-"+card.Color);
                CompactCardNumbers(tile,card,view.Players[seat],instance.Zone==CardZone.Selected || defenseCardId==card.Id,"hand-"+card.Color);
                string status = instance.Zone == CardZone.Selected ? (view.QuickSelection ? "已选 · 等待其他人" : view.Players[seat].Confirmed ? "已确认" : "已选 · 待确认") : ZoneName(instance);
                if(view.DefenseRestrictions.ContainsKey(card.Id)) status="本次不能防御";
                tile.Add(Text(status,"card-zone")); row.Add(tile);
                AttachCardReading(tile,card,view.Players[seat]);
            }
        }
        private void BuildRightPanel(VisualElement parent, GameView view)
        {
            var panel = Box("settings-drawer");panel.name="operation-panel";parent.Add(panel);
            panel.style.display=rightExpanded ? DisplayStyle.Flex : DisplayStyle.None;
            var heading = Box("panel-heading"); panel.Add(heading);
            heading.Add(Button("行动", () => { showDebug = false; showHotkeys=false; debugTeleport = false; ClearPending(); Render(); }, !showDebug && !showHotkeys ? "active-tab" : "tab-button"));
            heading.Add(Button("调试", () => { showDebug = true; showHotkeys=false; Render(); }, showDebug && !showHotkeys ? "active-tab" : "tab-button"));
            heading.Add(Button("热键",()=>{showHotkeys=true;Render();},showHotkeys ? "active-tab" : "tab-button","hotkeys-tab"));
            heading.Add(Button("×", () => { rightExpanded = false; showHotkeys=false; Render(); }, "edge-button", "toggle-right"));
            var scroll = new ScrollView { name = "goa-scroll-right-" + (showDebug ? "debug" : "action") }; scroll.AddToClassList("sidebar"); panel.Add(scroll);
            if(showHotkeys) RenderHotkeys(scroll);else if (showDebug) RenderDebugPanel(scroll, view); else RenderSidebar(scroll, view);
        }
    }
}
