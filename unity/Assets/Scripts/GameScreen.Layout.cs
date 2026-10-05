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
            if(Input.GetKeyDown(KeyCode.Escape)) { if(skillPopup!=null){skillPopup.RemoveFromHierarchy();skillPopup=null;}else if(heroPopup!=null)HideHeroHover();else if(keywordGlossaryOpen) CloseKeywordGlossary();else if(rightExpanded && !galleryOpen && !historyOpen && !newMatchPending && !debugPresetsOpen) {rightExpanded=false;showHotkeys=false;Render();}else if(wheelSeat.HasValue)ToggleHeroWheel(wheelSeat.Value);else if(browsingWheels.Count>0)CloseHeroWheel();else HideCardPreview();return; }
            if(keywordGlossaryOpen) return;
            if(Input.GetKeyDown(KeyCode.F1) && HasGameView && !startupFailed && !newMatchPending && !debugPresetsOpen && !IsEditingText()) { OpenKeywordGlossary(previewCard);return; }
            if (!HasGameView || startupFailed || galleryOpen || publicCardsOpen || historyOpen || newMatchPending || debugPresetsOpen || IsEditingText()) return;
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
            if(NetworkMode){ToggleHeroWheel(next);return;}
            seat = next; browsingWheels.Clear(); mainFlow=true; cameraFollow=true; wheelContext=""; ClearPending();
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
            var shell=Box("battlefield-shell");shell.name="battlefield-workspace";shell.StretchToParentSize();root.Add(shell);
            BuildBoard(shell,view);
            if(view.UpgradeOptions.Count==0)BuildRevealedStrip(root,view);
            var handLayer=Box("hand-overlay");handLayer.name="hand-overlay";root.Add(handLayer);BuildHand(handLayer,view);
            if(handLayer.childCount==0)handLayer.style.display=DisplayStyle.None;
            BuildScenarioBar(root);
            BuildRightPanel(root,view);
        }
        private void BuildSettingsCommands(VisualElement parent,GameView view)
        {
            var controls=Box("settings-commands");controls.name="settings-commands";parent.Add(controls);
            if(!NetworkMode)controls.Add(Button(view.QuickSelection ? "测试 · 选完揭示" : "手动确认",()=>{showDebug=true;showHotkeys=false;Render();},"quiet-button"));
            controls.Add(Button("图鉴 108",()=>{galleryOpen=true;galleryHero=catalog.Heroes[0].Id;Render();},"quiet-button"));
            controls.Add(Button("术语",()=>OpenKeywordGlossary(),"quiet-button","keyword-open"));
            controls.Add(Button("出牌记录",()=>{publicCardsOpen=true;Render();},"quiet-button"));
            if(NetworkMode)RenderNetworkControls(controls);
            else {
                controls.Add(Button("保存",Save,"quiet-button"));
                var load=Button("读取",Load,"quiet-button");load.SetEnabled(!ScenarioRunning);controls.Add(load);
                var fresh=Button("新对局",()=>{newMatchPending=true;Render();},"quiet-button");fresh.SetEnabled(!ScenarioRunning);controls.Add(fresh);
            }
            BuildAudioSettings(parent);
            parent.Add(Text(notice,"settings-notice"));
        }
        private static string ColorName(string color) => color switch { "gold" => "金", "silver" => "银", "red" => "红", "green" => "绿", "blue" => "蓝", _ => "紫" };
        private void BuildRevealedStrip(VisualElement parent, GameView view) => BuildActionSequence(parent,view);
        private void BuildBoard(VisualElement parent, GameView view)
        {
            var field = Box("field");field.name="battlefield-map"; parent.Add(field);
            var targets = mainFlow ? LegalCells(view) : new System.Collections.Generic.List<Hex>();
            board = new BattlefieldSurface(catalog, view, targets, chosenCell, cell =>
            {
                if (!targets.Contains(cell)) { notice = "此格不可用于当前操作。"; return; }
                Sound("target");chosenCell = cell; worldOptionsOpen=false;wheelSeat=null; notice = "已选地图格 " + cell + "，确认后应用。"; Render();
            }, cell => cellInfo.text = BoardHint(view,RegionName(cell.Region) + " · " + cell.Position + (targets.Contains(cell.Position) ? " · 可选" : "")), viewport, SelectedEffectArea(view), board3DViewport, seat);
            board.HeroHover=ShowHeroHover;
            board.HeroClick=ToggleHeroWheel;
            board.EmptyClick=CloseHeroWheel;
            board.ManualPan=()=>{
                if(!mainFlow)return;LeaveMainFlow();
                root.Q("main-flow-status")?.RemoveFromHierarchy();BuildMainFlowStatus();
                if(root.Q("world-decisions")!=null)root.Q("world-decisions").style.display=DisplayStyle.None;
                if(decisionDock!=null)decisionDock.style.display=DisplayStyle.None;
            };
            board.RegisterCallback<PointerUpEvent>(e=>{if((e.button==1 || e.button==2) && !mainFlow)root.schedule.Execute(Render);});
            board.ViewportChanged = RequestCapture;
            field.Add(board);
            cellInfo = Text(BoardHint(view,targets.Count == 0 ? "滚轮缩放 · 中/右键拖动 · Home全图" : targets.Count + " 个合法目标 · 点击后确认"), "tiny");
            // Cell details remain available to QA and operations; no permanent footer.
        }
        private void BuildHand(VisualElement parent, GameView view)
        {
            if(!mainFlow || wheelState.Discards.Count>0 || view.UpgradeOptions.Count==0) return;
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
            if(!NetworkMode) heading.Add(Button("调试", () => { showDebug = true; showHotkeys=false; Render(); }, showDebug && !showHotkeys ? "active-tab" : "tab-button"));
            heading.Add(Button("热键",()=>{showHotkeys=true;Render();},showHotkeys ? "active-tab" : "tab-button","hotkeys-tab"));
            heading.Add(Button("×", () => { rightExpanded = false; showHotkeys=false; Render(); }, "edge-button", "toggle-right"));
            var scroll = new ScrollView { name = "goa-scroll-right-" + (showDebug ? "debug" : "action") }; scroll.AddToClassList("sidebar"); panel.Add(scroll);
            BuildSettingsCommands(scroll,view);
            var decisions=Box("decision-content");decisions.name="decision-content";scroll.Add(decisions);
            if(showHotkeys)RenderHotkeys(decisions);else if(showDebug)RenderDebugPanel(decisions,view);else RenderSidebar(decisions,view);
        }
    }
}
