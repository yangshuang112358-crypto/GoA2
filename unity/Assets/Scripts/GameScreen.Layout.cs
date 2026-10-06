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
            UpdateStageBanner();
            UpdateInitiativePresentation();
            UpdateOpening();
            UpdateCombatPresentation();
            UpdatePresentationFocus();
            UpdateTutorial();
            if(tutorialMenu || tutorialHelp)return;
            if(artSamplesOpen){if(Input.GetKeyDown(KeyCode.Escape)){artSamplesOpen=false;Render();}return;}
            if(Input.GetKeyDown(KeyCode.Escape)) { if(skillPopup!=null){skillPopup.RemoveFromHierarchy();skillPopup=null;}else if(heroPopup!=null)HideHeroHover();else if(keywordGlossaryOpen) CloseKeywordGlossary();else if(rightExpanded && !galleryOpen && !historyOpen && !newMatchPending && !debugPresetsOpen) {rightExpanded=false;showHotkeys=false;Render();}else if(wheelSeat.HasValue)ToggleHeroWheel(wheelSeat.Value);else if(browsingWheels.Count>0)CloseHeroWheel();else HideCardPreview();return; }
            if(keywordGlossaryOpen) return;
            if(Input.GetKeyDown(KeyCode.F1) && HasGameView && !startupFailed && !newMatchPending && !debugPresetsOpen && !IsEditingText()) { OpenKeywordGlossary(previewCard);return; }
            if (!HasGameView || startupFailed || galleryOpen || publicCardsOpen || historyOpen || newMatchPending || debugPresetsOpen || IsEditingText()) return;
            if(Input.GetKeyDown(KeyCode.Space)) {ReturnMainFlow();return;}
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
            if(NetworkMode || TutorialActive){ToggleHeroWheel(next);return;}
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
            if(view.UpgradeOptions.Count==0 && (!TutorialActive || tutorial!.Line.Id=="initiative"))BuildRevealedStrip(root,view);
            // Upgrade candidates are anchored to the hero by BuildUpgradeWheel.
            BuildScenarioBar(root);
            BuildRightPanel(root,view);
        }
        private void BuildSettingsCommands(VisualElement parent,GameView view)
        {
            var controls=Box("settings-commands");controls.name="settings-commands";parent.Add(controls);
            if(!NetworkMode && !TutorialActive)controls.Add(Button(view.QuickSelection ? "测试 · 选完揭示" : "手动确认",()=>{showDebug=true;showHotkeys=false;Render();},"quiet-button"));
            controls.Add(Button("操作与规则帮助",()=>{tutorialHelp=true;rightExpanded=false;Render();},"quiet-button","tutorial-help-open"));
            if(!TutorialActive)controls.Add(Button((tutorialProgress?.Hints??true)?"首次对局提示：开":"首次对局提示：关",ToggleTutorialHints,"quiet-button","tutorial-hints-toggle"));
            if(!NetworkMode && !ScenarioRunning)controls.Add(Button(TutorialActive?"教程目录":"新手教程 · 独立练习",OpenTutorialMenu,"quiet-button","tutorial-open"));
            controls.Add(Button("图鉴 108",()=>{galleryOpen=true;galleryHero=catalog.Heroes[0].Id;Render();},"quiet-button"));
            controls.Add(Button("术语",()=>OpenKeywordGlossary(),"quiet-button","keyword-open"));
            controls.Add(Button("出牌记录",()=>{publicCardsOpen=true;Render();},"quiet-button"));
            if(NetworkMode)RenderNetworkControls(controls);
            else if(TutorialActive){controls.Add(Button("退出教程，返回对局",ExitTutorial,"quiet-button","tutorial-exit-settings"));}
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
            var targets = DecisionFlow ? LegalCells(view) : new System.Collections.Generic.List<Hex>();
            board = new BattlefieldSurface(catalog, view, targets, chosenCell, cell =>
            {
                if(InitiativePresenting)return;
                if (!targets.Contains(cell)) { notice = "此格不可用于当前操作。"; return; }
                Sound("target");chosenCell = cell; worldOptionsOpen=false;wheelSeat=null; notice = "已选地图格 " + cell + "，确认后应用。"; Render();
            }, cell => cellInfo.text = BoardHint(view,RegionName(cell.Region) + " · " + cell.Position + (targets.Contains(cell.Position) ? " · 可选" : "")), viewport, SelectedEffectArea(view), board3DViewport, seat);
            board.HeroHover=ShowHeroHover;
            board.HeroClick=ToggleHeroWheel;
            board.EmptyClick=CloseHeroWheel;
            board.ManualPan=BrowseCamera;
            board.ViewportChanged = RequestCapture;
            field.Add(board);
            cellInfo = Text(BoardHint(view,targets.Count == 0 ? "滚轮缩放 · 中/右键拖动 · Home全图" : targets.Count + " 个合法目标 · 点击后确认"), "tiny");
            // Cell details remain available to QA and operations; no permanent footer.
        }
        private void BuildRightPanel(VisualElement parent, GameView view)
        {
            var panel = Box("settings-drawer");panel.name="operation-panel";parent.Add(panel);
            panel.style.display=rightExpanded ? DisplayStyle.Flex : DisplayStyle.None;
            var heading = Box("panel-heading"); panel.Add(heading);
            heading.Add(Button("行动", () => { showDebug = false; showHotkeys=false; debugTeleport = false; ClearPending(); Render(); }, !showDebug && !showHotkeys ? "active-tab" : "tab-button"));
            if(!NetworkMode && !TutorialActive) heading.Add(Button("调试", () => { showDebug = true; showHotkeys=false; Render(); }, showDebug && !showHotkeys ? "active-tab" : "tab-button"));
            heading.Add(Button("热键",()=>{showHotkeys=true;Render();},showHotkeys ? "active-tab" : "tab-button","hotkeys-tab"));
            heading.Add(Button("×", () => { rightExpanded = false; showHotkeys=false; Render(); }, "edge-button", "toggle-right"));
            var scroll = new ScrollView { name = "goa-scroll-right-" + (showDebug ? "debug" : "action") }; scroll.AddToClassList("sidebar"); panel.Add(scroll);
            BuildSettingsCommands(scroll,view);
            var decisions=Box("decision-content");decisions.name="decision-content";scroll.Add(decisions);
            if(showHotkeys)RenderHotkeys(decisions);else if(showDebug)RenderDebugPanel(decisions,view);else RenderSidebar(decisions,view);
        }
    }
}
