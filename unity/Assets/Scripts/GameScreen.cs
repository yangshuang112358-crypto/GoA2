#nullable enable
using System;
using System.Collections.Generic;
using System.Collections;
using System.IO;
using System.Linq;
using Goa2.Application;
using Goa2.Domain;
using Goa2.Infrastructure;
using UnityEngine;
using UnityEngine.UIElements;
using UnityApplication = UnityEngine.Application;

namespace Goa2.Presentation
{
    public sealed partial class GameScreen : MonoBehaviour
    {
        private ContentCatalog catalog = null!;
        private GameSession session = null!;
        private VisualElement root = null!;
        private Font font = null!;
        private int seat;
        private string? chosenHero;
        private int deploymentSeat = -1;
        private Hex? chosenCell;
        private MoveMode? moveMode;
        private int? initiativeSeat;
        private bool passPending;
        private bool newMatchPending;
        private string newMatchError = "";
        private bool startupFailed;
        private string notice = "";
        private string? galleryHero;
        private string galleryQuery="";
        private bool galleryOnlySupported;
        private bool galleryOpen;
        private bool publicCardsOpen;
        private string? screenshotPath;
        private string? customSavePath;
        private int screenshotRevision;
        private IVisualElementScheduledItem? captureJob;
        private Label cellInfo = null!;
        private static bool created;
        private GameView renderedView = new GameView();
        private readonly Dictionary<string, Vector2> scrollPositions = new Dictionary<string, Vector2>();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlayMode() { created = false; }
        private string SavePath => customSavePath ?? Path.Combine(UnityApplication.persistentDataPath, "saves", "hotseat-v1.json");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Boot()
        {
            if (created) return;
            created = true;
            var args = Environment.GetCommandLineArgs();
            if (UnityApplication.isBatchMode && args.Contains("-goaScenario")) { ScenarioPlayer.RunHeadless(args); return; }
            var host = new GameObject("Goa2 Application");
            DontDestroyOnLoad(host);
            host.AddComponent<GameScreen>();
        }
        private void Awake()
        {
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            settings.sortingOrder = 100;
            settings.themeStyleSheet = Resources.Load<ThemeStyleSheet>("Goa2Theme");
            var document = gameObject.AddComponent<UIDocument>();
            document.panelSettings = settings;
            root = document.rootVisualElement;
            font = Resources.Load<Font>("Fonts/NotoSansCJKsc-Regular");
            if (font == null) font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "Arial" }, 16);
            root.style.unityFont = font;
            root.style.unityFontDefinition = FontDefinition.FromFont(font);
            root.style.flexGrow = 1;
            var stylesheet = Resources.Load<StyleSheet>("Goa2");
            if (stylesheet != null) root.styleSheets.Add(stylesheet);
            try
            {
                var arguments = Environment.GetCommandLineArgs();
                int captureIndex = Array.IndexOf(arguments, "-goaScreenshot");
                if (captureIndex >= 0 && captureIndex + 1 < arguments.Length) screenshotPath = arguments[captureIndex + 1];
                int saveIndex = Array.IndexOf(arguments, "-goaSavePath");
                if (saveIndex >= 0 && saveIndex + 1 < arguments.Length) customSavePath = Path.GetFullPath(arguments[saveIndex + 1]);
                catalog = ContentLoader.LoadDirectory(Path.Combine(UnityApplication.streamingAssetsPath, "Goa2"));
                if (arguments.Contains("-goaScenario")) { SetupScenario(arguments); return; }
                int loadIndex = Array.IndexOf(arguments, "-goaLoad");
                if (loadIndex >= 0 && loadIndex + 1 < arguments.Length)
                {
                    session = LocalGameFactory.Restore(catalog, File.ReadAllText(arguments[loadIndex + 1]));
                    notice = "已恢复指定存档。"; Render();
                }
                else NewMatch();
                StartCardReadingAudit(arguments);
                StartRevealedStripeAudit(arguments);
            }
            catch (Exception error)
            {
                Debug.LogWarning("启动读取失败："+error.Message);
                var arguments = Environment.GetCommandLineArgs();
                if (arguments.Contains("-goaScenario")) ScenarioPlayer.WriteFailure(arguments,error);
                int loadIndex=Array.IndexOf(arguments,"-goaLoad");
                string? failedSave=catalog!=null && loadIndex>=0 && loadIndex+1<arguments.Length ? arguments[loadIndex+1] : null;
                RenderStartupFailure(failedSave,error.Message);
                if(arguments.Contains("-goaScenarioQuit")) root.schedule.Execute(()=>UnityApplication.Quit(1)).StartingIn(1500);
            }
        }
        private void NewMatch()
        {
            startupFailed=false; newMatchError="";
            scenario = null;
            session = LocalGameFactory.Create(catalog, Guid.NewGuid().ToString("N"), new[] { "玩家 1", "玩家 2", "玩家 3", "玩家 4" }, UnityEngine.Random.Range(0, int.MaxValue), true);
            seat = 0; newMatchPending = false; notice = "测试对局已建立。可手工选英雄，或打开调试工具自动准备。";
            ClearPending(); Render();
        }
        private void ClearPending()
        {
            chosenHero = null; chosenCell = null; moveMode = null; initiativeSeat = null; passPending = false; deploymentSeat = -1;
            defenseCardId = ""; discardCardId = ""; declineDefensePending = false;declineRetaliationPending=false;
            goldTransferTarget=-1;goldTransferAmount=-1;
            upgradeCardId = ""; upgradeColor = "";
            debugAttack = false;
        }
        private void Submit(CommandKind kind, string value = "", int target = -1, Hex destination = default, MoveMode mode = MoveMode.Secondary)
        {
            if (ScenarioRunning) { notice = "场景执行期间可查看角色和地图；完成后可转为手工操作。"; Render(); return; }
            var view = session.View(seat);
            var result = session.Execute(seat, new Command
            {
                Id = Guid.NewGuid().ToString("N"), MatchId = view.MatchId, ExpectedRevision = view.Revision,
                ActorSeat = seat, Kind = kind, Value = value, TargetSeat = target, Destination = destination, MoveMode = mode
            });
            notice = result.Accepted ? "操作已确认。" : result.Message;
            ClearPending(); Render();
        }
        private static VisualElement Box(string css)
        {
            var box = new VisualElement(); box.AddToClassList(css); return box;
        }
        private static Label Text(string value, string css = "body")
        {
            var label = new Label(value); label.AddToClassList(css); return label;
        }
        private static Button Button(string text, Action action, string css = "button", string name = "")
        {
            var button = new Button(action) { text = text, name = name }; button.AddToClassList(css); return button;
        }
        private string HeroName(string? id) => catalog.Heroes.FirstOrDefault(h => h.Id == id)?.Name.Split('·').Last() ?? "未选英雄";
        private string PlayerName(int number) => "席位 " + (number + 1) + " · " + HeroName(renderedView.Players[number].HeroId);
        private static string PhaseName(GameView view)
        {
            if (view.RoundEndStage=="upgrades") return "英雄升级";
            if (view.Pending!=null)
            {
                switch(view.Pending.Kind)
                {
                    case "attack_target": return "选择攻击目标";
                    case "effect_target": return "选择牌文目标";
                    case "effect_minion": return "选择额外移除的小兵";
                    case "defense": return "选择防御";
                    case "forced_discard": return "反制选择";
                    case "optional_discard": return "攻击前弃牌";
                    case "effect_move": return "牌文移动";
                    case "placement": return "放置落点";
                    case "card_swap": return "防御后换牌";
                    case "recover_discard": return "取回卡牌";
                    case "gold_transfer": return "选择拿取金币";
                    case "hero_respawn": return "英雄复活";
                    case "round_minion_removal": return "轮末小兵战斗";
                    case "minion_spawn": return "安排小兵出生";
                }
            }
            switch (view.Phase)
            {
                case Phase.HeroSelection: return "选择英雄";
                case Phase.Deployment: return "安排出生";
                case Phase.Planning: return "暗选卡牌";
                case Phase.InitiativeChoice: return "先攻决策";
                case Phase.Action: return "执行行动";
                case Phase.EffectChoice: return "处理待选择";
                case Phase.Finished: return view.Winner.HasValue ? (view.Winner==Team.Blue ? "蓝队获胜" : "红队获胜") : "对局结束";
                default: return "到达轮末";
            }
        }
        private void Render()
        {
            if(!spaceGuardInstalled)
            {
                root.RegisterCallback<KeyDownEvent>(e=> {if(e.keyCode==KeyCode.Space && !IsEditingText()) { e.StopImmediatePropagation();e.PreventDefault(); }},TrickleDown.TrickleDown);
                spaceGuardInstalled=true;
            }
            root.Query<ScrollView>().ForEach(scroll => { if (scroll.name.StartsWith("goa-scroll-")) scrollPositions[scroll.name] = scroll.scrollOffset; });
            HideCardPreview();
            root.Clear();
            confirmAction=null; confirmButton=null;
            renderedView = session.View(seat);
            if (!renderedView.EffectAreas.ContainsKey(effectAreaId)) effectAreaId="";
            BuildLayout(renderedView);
            if (galleryOpen) RenderGallery();
            if (publicCardsOpen) RenderPublicCards(renderedView);
            if (historyOpen) RenderHistory(renderedView);
            if (newMatchPending) RenderNewMatchDialog();
            if (debugPresetsOpen) RenderDebugPositions();
            if (keywordGlossaryOpen) RenderKeywordGlossary();
            root.Query<ScrollView>().ForEach(scroll =>
            {
                if (scrollPositions.TryGetValue(scroll.name, out var offset)) scroll.schedule.Execute(() => scroll.scrollOffset = offset);
                scroll.verticalScroller.valueChanged += _ => RequestCapture();
                scroll.horizontalScroller.valueChanged += _ => RequestCapture();
            });
            RequestCapture();
        }
        private void RequestCapture()
        {
            if (screenshotPath == null) return;
            captureJob?.Pause();
            captureJob = root.schedule.Execute(() => StartCoroutine(CaptureFrame(++screenshotRevision))).StartingIn(100);
        }
        private static Label SeatLabel(VisualElement parent, string caption, string css, float top, float height)
        {
            var label = Text(caption, css);
            label.style.position = Position.Absolute;
            label.style.left = 10; label.style.right = 5; label.style.top = top; label.style.height = height;
            label.style.marginTop = 0; label.style.marginBottom = 0; label.style.paddingTop = 0; label.style.paddingBottom = 0;
            parent.Add(label);
            return label;
        }
        private IEnumerator CaptureFrame(int revision)
        {
            yield return null;
            yield return new WaitForEndOfFrame();
            if (revision != screenshotRevision || screenshotPath == null) yield break;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(screenshotPath))!);
            var layout = new QaLayout { Width = Screen.width, Height = Screen.height, Seat = seat, Revision = renderedView.Revision,
                Zoom = viewport.Zoom, HexRadius=board?.HexRadius ?? 0, Focus = viewport.Focus, Phase = startupFailed ? "StartupError" : renderedView.Phase.ToString(), Round = renderedView.Round, Turn = renderedView.Turn,
                LeftExpanded = leftExpanded, RightExpanded = rightExpanded, TopExpanded = topExpanded, BottomExpanded = bottomExpanded,
                SelectedCell = chosenCell.HasValue ? chosenCell.Value.ToString() : "", BoardBounds = board?.worldBound ?? default,
                ActiveSeat = renderedView.ActiveSeat ?? -1, RevealedHeading = root.Q<Label>("revealed-heading")?.text ?? "",
                FilledPlayDots = root.Query<VisualElement>(className: "filled-dot").ToList().Count,
                DiscardDotCount = root.Query<VisualElement>(className: "discard-dot").ToList().Count,
                ActiveEffectCount=renderedView.Effects.Count, EffectAreaId=effectAreaId, EffectAreaCells=SelectedEffectArea(renderedView).Count,
                PrimaryRestriction=renderedView.PrimaryRestriction,
                AttackSourceSummary=root.Q<Label>("attack-card-text-sources")?.text ?? "" };
            root.Query<Button>().ForEach(button => layout.Buttons.Add(new QaButton { Name = button.name, Text = button.text, Bounds = button.worldBound, Enabled = button.enabledInHierarchy, Visible = VisibleCenter(button), Viewports=ScrollViewports(button) }));
            root.Query<Label>().ForEach(label => { if(!string.IsNullOrEmpty(label.name)) layout.Labels.Add(new QaLabel {Name=label.name,Text=label.ClassListContains("keyword-rules") ? CardTextMarkup.PlainText(label.text) : label.text,RichText=label.text,Bounds=label.worldBound,Visible=VisibleCenter(label),FontSize=label.resolvedStyle.fontSize,Color=label.resolvedStyle.color}); });
            layout.MinimumFontSize=root.Query<TextElement>().ToList().Where(e=>!string.IsNullOrEmpty(e.text) && VisibleCenter(e)).Select(e=>e.resolvedStyle.fontSize).DefaultIfEmpty(26).Min();
            layout.CardPreviewScrollCount=cardPreview?.Query<ScrollView>().ToList().Count ?? 0;
            layout.NestedCardScrollCount=root.Query<ScrollView>().ToList().Count(e=>e.ClassListContains("detail-text") || e.ClassListContains("public-card-text"));
            root.Query<VisualElement>().ForEach(e=> {if(e.name.StartsWith("purple-") || e.name.StartsWith("revealed-") || e.name.StartsWith("gallery-row-") || e.name.StartsWith("upgrade-row-") || e.name.StartsWith("hand-") || e.name.StartsWith("card-preview") || e.name.StartsWith("inspect-")) layout.Elements.Add(new QaElement {Name=e.name,Bounds=e.worldBound,Visible=VisibleCenter(e),Background=e.resolvedStyle.backgroundColor,BorderTop=e.resolvedStyle.borderTopWidth});});
            root.Query<ScrollView>().ForEach(scroll=>
            {
                var slider=scroll.horizontalScroller.Q<Slider>();var thumb=slider?.Q(className:"unity-base-slider__dragger");
                if(slider!=null && thumb!=null && scroll.horizontalScroller.highValue>0)
                    layout.Scrolls.Add(new QaScroll {Name=scroll.name,Viewport=scroll.contentViewport.worldBound,Track=slider.worldBound,Thumb=thumb.worldBound,Value=scroll.horizontalScroller.value,Maximum=scroll.horizontalScroller.highValue});
            });
            root.Query<IntegerField>().ForEach(field => layout.Fields.Add(new QaField { Name = field.name, Bounds = field.worldBound, Value = field.value, Enabled=field.enabledInHierarchy,Visible=VisibleCenter(field),Viewports=ScrollViewports(field) }));
            root.Query<TextField>().ForEach(field => { if(!string.IsNullOrEmpty(field.name)) layout.TextFields.Add(new QaTextField {Name=field.name,Bounds=field.worldBound,Value=field.value,Enabled=field.enabledInHierarchy,Visible=VisibleCenter(field),Viewports=ScrollViewports(field)}); });
            if (board != null)
            {
                var legal = new HashSet<Hex>(LegalCells(renderedView));
                foreach (var cell in catalog.Cells) layout.Cells.Add(new QaCell { X = cell.Position.X, Y = cell.Position.Y, Center = board.PanelCenter(cell.Position), Legal = legal.Contains(cell.Position) });
            }
            string layoutPath = Path.ChangeExtension(screenshotPath, ".ui.json");
            File.WriteAllText(layoutPath + ".tmp", JsonUtility.ToJson(layout));
            if (File.Exists(layoutPath)) File.Replace(layoutPath + ".tmp", layoutPath, null);
            else File.Move(layoutPath + ".tmp", layoutPath);
            ScreenCapture.CaptureScreenshot(screenshotPath);
        }
        private static bool VisibleCenter(VisualElement element)
        {
            if (element.resolvedStyle.visibility != Visibility.Visible || element.resolvedStyle.display == DisplayStyle.None) return false;
            Vector2 center = element.worldBound.center;
            for (var ancestor = element.parent; ancestor != null; ancestor = ancestor.parent)
            {
                if(ancestor.resolvedStyle.display==DisplayStyle.None) return false;
                if (ancestor is ScrollView scroll && !scroll.contentViewport.worldBound.Contains(center)) return false;
            }
            if (center.x < 0 || center.y < 0 || center.x >= Screen.width || center.y >= Screen.height) return false;
            if (element is Button)
            {
                // A point inside the reported viewport can still be clipped or covered.
                // QA must use the same hit-test result as a real pointer click.
                for (var picked = element.panel?.Pick(center); picked != null; picked = picked.parent)
                    if (picked == element) return true;
                return false;
            }
            return true;
        }
        private static List<Rect> ScrollViewports(VisualElement element)
        {
            var result=new List<Rect>();
            for(var ancestor=element.parent;ancestor!=null;ancestor=ancestor.parent)
                if(ancestor is ScrollView scroll) result.Add(scroll.contentViewport.worldBound);
            result.Reverse();return result;
        }
        [Serializable] private sealed class QaLayout
        {
            public int Width, Height, Seat, Round, Turn, ActiveSeat, FilledPlayDots, DiscardDotCount; public long Revision; public float Zoom, HexRadius; public Vector2 Focus; public Rect BoardBounds;
            public string Phase = "", SelectedCell = "", RevealedHeading = "", EffectAreaId="", PrimaryRestriction="", AttackSourceSummary="";
            public int ActiveEffectCount, EffectAreaCells;
            public int CardPreviewScrollCount, NestedCardScrollCount;
            public float MinimumFontSize;
            public List<QaElement> Elements=new List<QaElement>();
            public List<QaScroll> Scrolls=new List<QaScroll>();
            public bool LeftExpanded, RightExpanded, TopExpanded, BottomExpanded;
            public List<QaButton> Buttons = new List<QaButton>(); public List<QaCell> Cells = new List<QaCell>();
            public List<QaField> Fields = new List<QaField>();
            public List<QaTextField> TextFields = new List<QaTextField>();
            public List<QaLabel> Labels = new List<QaLabel>();
        }
        [Serializable] private sealed class QaButton { public string Name = "", Text = ""; public Rect Bounds; public bool Enabled, Visible;public List<Rect> Viewports=new List<Rect>(); }
        [Serializable] private sealed class QaCell { public int X, Y; public Vector2 Center; public bool Legal; }
        [Serializable] private sealed class QaField { public string Name = ""; public Rect Bounds; public int Value; public bool Enabled,Visible;public List<Rect> Viewports=new List<Rect>(); }
        [Serializable] private sealed class QaTextField { public string Name="",Value=""; public Rect Bounds;public bool Enabled,Visible;public List<Rect> Viewports=new List<Rect>(); }
        [Serializable] private sealed class QaLabel { public string Name="", Text="",RichText=""; public Rect Bounds; public bool Visible; public float FontSize; public Color Color; }
        [Serializable] private sealed class QaElement {public string Name="";public Rect Bounds; public bool Visible; public Color Background;public float BorderTop;}
        [Serializable] private sealed class QaScroll {public string Name="";public Rect Viewport,Track,Thumb;public float Value,Maximum;}
        private List<Hex> LegalCells(GameView view)
        {
            if (debugAttack) return view.Units.Where(u=>view.DebugAttackTargets.Contains(u.Id)).Select(u=>u.Position).ToList();
            if (debugTeleport && view.DebugTeleports.TryGetValue(debugUnitId, out var teleportTargets)) return teleportTargets;
            if (view.Pending?.Kind == "attack_target" && view.Pending.ChooserSeat == seat)
                return view.Units.Where(u => view.AttackTargets.Contains(u.Id)).Select(u => u.Position).ToList();
            if ((view.Pending?.Kind == "effect_target" || view.Pending?.Kind=="effect_minion") && view.Pending.ChooserSeat == seat)
                return view.Units.Where(u => view.EffectTargets.Contains(u.Id)).Select(u => u.Position).ToList();
            if (view.Pending?.Kind == "hero_respawn") return view.RespawnCells;
            if (view.Pending?.Kind == "placement") return view.Placements;
            if (view.Pending?.Kind == "effect_move") return view.EffectMoves.Select(m=>m.Destination).ToList();
            if (view.Pending?.Kind == "round_minion_removal") return view.Units.Where(u => view.RoundMinionRemovals.Contains(u.Id)).Select(u => u.Position).ToList();
            if (view.Pending?.Kind == "minion_spawn" && view.Pending.ChooserSeat == seat)
            {
                PrepareSpawnSelection(view);
                return view.SpawnChoices.TryGetValue(spawnUnitId, out var cells) ? cells : new List<Hex>();
            }
            if (view.Phase == Phase.Deployment && view.Deployments.Count > 0)
            {
                if (!view.Deployments.ContainsKey(deploymentSeat)) deploymentSeat = view.Deployments.Keys.First();
                return view.Deployments[deploymentSeat];
            }
            if (moveMode == MoveMode.Secondary) return view.SecondaryMoves.Select(m => m.Destination).ToList();
            if (moveMode == MoveMode.Fast) return view.FastMoves.Select(m => m.Destination).ToList();
            return new List<Hex>();
        }
        private static string RegionName(string region)
        {
            switch (region)
            {
                case "redFountain": return "红方泉水"; case "blueFountain": return "蓝方泉水";
                case "redNear": return "红方近区"; case "blueNear": return "蓝方近区";
                case "topGrass": return "上方草地"; case "bottomGrass": return "下方草地";
                case "terrain": return "障碍地形"; default: return "中区";
            }
        }
        private void RenderSidebar(VisualElement sidebar, GameView view)
        {
            sidebar.Add(Text("当前席位 " + (seat + 1), "eyebrow"));
            sidebar.Add(Text(PhaseName(view), "panel-title"));
            RenderDebugGuideShortcut(sidebar,view);
            switch (view.Phase)
            {
                case Phase.HeroSelection:
                    sidebar.Add(Text("每人选择不同英雄。六名英雄均可加入任一队伍。", "body"));
                    foreach (var hero in catalog.Heroes)
                    {
                        string id = hero.Id;
                        var option = Button(hero.Name, () => { chosenHero = id; Render(); }, "choice-button");
                        option.SetEnabled(view.AvailableHeroes.Contains(id));
                        if (chosenHero == id || view.Players[seat].HeroId == id) option.AddToClassList("chosen");
                        sidebar.Add(option);
                    }
                    if (chosenHero != null)
                        Confirm(sidebar, "确认选择 " + HeroName(chosenHero), () => Submit(CommandKind.ChooseHero, chosenHero!));
                    else sidebar.Add(Text("点击英雄，再确认选择。", "muted"));
                    break;
                case Phase.Deployment:
                    if (view.Deployments.Count == 0) sidebar.Add(Text("等待蓝队长 1、红队长 2 安排各自队员出生。", "body"));
                    else
                    {
                        sidebar.Add(Text("为本队英雄选择地图上高亮的出生点。", "body"));
                        foreach (int target in view.Deployments.Keys)
                        {
                            int copy = target;
                            var option = Button(PlayerName(target), () => { deploymentSeat = copy; chosenCell = null; Render(); }, "choice-button");
                            if (deploymentSeat == target) option.AddToClassList("chosen");
                            sidebar.Add(option);
                        }
                        if (chosenCell.HasValue)
                            Confirm(sidebar, "确认出生于 " + chosenCell.Value, () => Submit(CommandKind.DeployHero, target: deploymentSeat, destination: chosenCell!.Value));
                        else sidebar.Add(Text("选择地图格后，在这里确认。", "muted"));
                    }
                    break;
                case Phase.Planning:
                    if (view.CanUpgradeEngine)
                    {
                        sidebar.Add(Text("此存档使用先前的规则能力。选牌前可采用当前版本，保留已有对局历史。", "body"));
                        sidebar.Add(Button("采用当前规则", () => Submit(CommandKind.UpgradeEngine, GameState.CurrentEngineVersion.ToString()), "quiet-button", "upgrade-engine"));
                    }
                    if (view.Players[seat].Confirmed) sidebar.Add(Text("你已确认。等待其他有手牌的玩家确认后，自动翻牌。", "body"));
                    else
                    {
                        sidebar.Add(Text(view.QuickSelection ? "从下方选一张牌；四人选完立即揭示，之前可改选。" : "从下方手牌选一张。确认前可点击其他手牌改选。", "body"));
                        var selected = view.OwnCards.FirstOrDefault(c => c.Zone == CardZone.Selected);
                        if (selected != null)
                        {
                            RenderCardDetail(sidebar, catalog.Card(selected.CardId));
                            if (!view.QuickSelection) Confirm(sidebar,"确认出牌", () => Submit(CommandKind.ConfirmCard));
                        }
                    }
                    break;
                case Phase.InitiativeChoice:
                    var pending = view.Pending!;
                    sidebar.Add(Text(PlayerName(pending.ChooserSeat) + "决定本队哪张并列牌先执行。", "body"));
                    if (pending.ChooserSeat == seat)
                    {
                        foreach (int target in pending.CandidateSeats)
                        {
                            int copy = target;
                            var option = Button(PlayerName(target), () => { initiativeSeat = copy; Render(); }, "choice-button");
                            if (initiativeSeat == target) option.AddToClassList("chosen");
                            sidebar.Add(option);
                        }
                        if (initiativeSeat.HasValue) Confirm(sidebar, "确认先行动者", () => Submit(CommandKind.ChooseInitiative, target: initiativeSeat!.Value));
                    }
                    break;
                case Phase.Action:
                    sidebar.Add(Text(PlayerName(view.ActiveSeat!.Value) + "正在行动。", "body"));
                    var played = view.Players[view.ActiveSeat.Value].Revealed.Single(c => c.Zone == CardZone.PlayedUnresolved);
                    RenderCardDetail(sidebar, catalog.Card(played.CardId));
                    if (view.ActiveSeat == seat)
                    {
                        var primary = Button("执行" + catalog.Card(played.CardId).PrimaryCategory, () => { debugTeleport = false; Submit(CommandKind.BeginPrimary); }, "primary-button", "begin-primary");
                        primary.SetEnabled(view.CanBeginPrimary); sidebar.Add(primary);
                        if (view.PrimaryRestriction!="") sidebar.Add(Text("受到“" + catalog.Card(view.PrimaryRestriction).Name + "”影响，当前不能执行技能。可选择其他合法行动或放弃。", "restriction-text"));
                        if (view.PrimarySupported) sidebar.Add(Text("开始后按牌文完成行动；不能再改选次要移动或放弃。", "tiny"));
                        var normal = Button("次要移动" + (moveMode == MoveMode.Secondary ? "  ✓" : ""), () => { debugTeleport = false; moveMode = MoveMode.Secondary; chosenCell = null; passPending = false; Render(); }, "choice-button");
                        normal.SetEnabled(view.SecondaryMoves.Count > 0); sidebar.Add(normal);
                        var fast = Button("快速移动" + (moveMode == MoveMode.Fast ? "  ✓" : ""), () => { debugTeleport = false; moveMode = MoveMode.Fast; chosenCell = null; passPending = false; Render(); }, "choice-button");
                        fast.SetEnabled(view.FastMoves.Count > 0); sidebar.Add(fast);
                        if (chosenCell.HasValue && moveMode.HasValue)
                            Confirm(sidebar, "确认移动至 " + chosenCell.Value, () => Submit(CommandKind.Move, destination: chosenCell!.Value, mode: moveMode!.Value));
                        else if (passPending) Confirm(sidebar, "确认放弃此牌行动", () => Submit(CommandKind.Pass));
                        else sidebar.Add(Button("放弃此牌行动", () => { passPending = true; moveMode = null; chosenCell = null; Render(); }, "quiet-button"));
                        if (!view.PrimarySupported) sidebar.Add(Text("此卡主要行动待实装；次要行动按显示选项使用。", "tiny"));
                    }
                    break;
                case Phase.RoundEnd:
                    RenderRoundEnd(sidebar, view);
                    break;
                case Phase.EffectChoice:
                    if (!RenderRoundMinionChoice(sidebar, view) && !RenderCombatChoice(sidebar, view)) RenderBattlefieldChoice(sidebar, view);
                    break;
                case Phase.Finished:
                    RenderVictory(sidebar, view);
                    break;
            }
            RenderActiveEffects(sidebar, view);
            RenderPermanentStats(sidebar, view);
            RenderRecentEvents(sidebar, view);
        }
        private void Confirm(VisualElement parent, string caption, Action action)
        {
            var box = Box("confirm-box"); parent.Add(box);
            confirmAction=action;
            confirmButton=Button(caption,action,"primary-button","confirm-current");
            box.Add(confirmButton);
            box.Add(Button("取消", () => { ClearPending(); Render(); }, "quiet-button"));
        }
        private void RenderCardDetail(VisualElement parent, CardDefinition card)
        {
            var box = Box("card-detail");
            box.Add(Text(card.Name + "    先攻 " + card.Initiative, "section-title"));
            box.Add(RulesText(card.Text, "card-rules"));
            box.Add(Button("本牌术语",()=>OpenKeywordGlossary(card),"quiet-button","card-keywords"));
            box.Add(Text(card.PrimaryCategory + " " + (card.Exclamation ? "!" : card.PrimaryValue.ToString()) + SubtypeText(card), "muted"));
            box.Add(Text("移 " + Number(card.SecondaryMovement) + "    防 " + Number(card.SecondaryDefense), "tiny"));
            parent.Add(box);
            AttachCardReading(box,card);
        }
        private void RenderRecentEvents(VisualElement parent, GameView view)
        {
            var box = Box("event-box");box.name="recent-event-log";
            var heading=Box("panel-heading");box.Add(heading);heading.Add(Text("最近记录","eyebrow"));
            heading.Add(Button("展开",()=>{historyOpen=true;historyRound=view.Round;Render();},"compact-button","history-open"));
            RenderDebugGuide(box,view);
            foreach (var entry in view.Events.TakeLast(12))
                box.Add(Text(EventText(entry), "tiny"));
            parent.Add(box);
        }
        private string EventText(GameEvent entry)
        {
            string actor = entry.Seat.HasValue ? "席位 " + (entry.Seat.Value + 1) : "";
            switch (entry.Kind)
            {
                case "DebugAttackStarted": return actor+"开始调试基础攻击 · "+entry.Detail;
                case "DebugAttackCompleted": return actor+"完成调试攻击";
                case "HeroChosen": return actor + "选择 " + HeroName(entry.Detail);
                case "HeroDeployed": return actor + "已出生";
                case "CardSelected": return actor + "更新了暗选";
                case "SelectionConfirmed": return actor + "已确认";
                case "CardRevealed": return actor + "揭示 " + catalog.Card(entry.CardId!).Name;
                case "UnitPlaced": return actor + "放置到 " + entry.To;
                case "PlacementChoiceRequired": return actor + "选择放置落点";
                case "UnitMoved": return actor + "移动至 " + entry.To;
                case "UnitPushed": return actor + "被推动 " + (entry.Path.Count-1) + " 格，位置 " + entry.To;
                case "PushStopped": return actor + "推动停止：" + (entry.Detail=="obstacle" ? "前方地形阻挡" : entry.Detail=="occupied" ? "前方有单位" : "已到地图边缘");
                case "EffectMoveChoiceRequired": return actor+"可按牌文移动"+entry.Detail+"格";
                case "DefenseMoveResolved": return actor+"已完成防御后的直线移动";
                case "CardSwapChoiceRequired": return actor+"可选择手牌交换本次防御牌";
                case "CardSwapSkipped": return actor+(entry.Detail=="declined"?"选择不交换卡牌":"没有可交换的手牌");
                case "CardsSwapped": return actor+"用 "+catalog.Card(entry.Detail).Name+" 换回 "+catalog.Card(entry.CardId!).Name;
                case "CardSwapColorsShown": return actor+"交换了手牌与防御牌的状态";
                case "RecoverDiscardRequired": return actor+"可按牌文取回一张卡牌";
                case "EffectTargetChoiceRequired": return actor+"选择牌文作用的英雄";
                case "EffectTargetChosen": return actor+"选定牌文目标 · "+entry.Detail;
                case "GoldTransferChoiceRequired": return actor+"选择是否拿取金币";
                case "GoldTransferred": return actor+"拿取金币";
                case "GoldTransferSkipped": return actor+"未拿取金币，继续后续效果";
                case "RecoverDiscardSkipped": return actor+(entry.Detail=="declined" ? "选择不取回卡牌" : "不满足取回条件或没有可取回的卡牌");
                case "RecoverDiscardCompleted": return actor+"完成牌文取回";
                case "EffectMoveSkipped": return actor+(entry.Detail=="declined" ? "选择不进行牌文移动" : entry.Detail=="pre_attack_move_used" ? "攻击前已移动，略过攻击后移动" : entry.Detail=="target_position_occupied" ? "无法进入攻击目标原格：该格仍有单位" : entry.Detail=="target_position_unreachable" ? "无法进入攻击目标原格：移动受阻" : "没有可用的牌文移动落点");
                case "ActionStarted": return actor + "开始行动";
                case "CardResolved": return actor + "的牌已结算";
                case "PlanningStarted": return "进入暗选 " + entry.Detail;
                case "TurnEnded": return "回合结束 " + entry.Detail;
                case "DecisionCoinFlipped": return "跨队同先攻，决策币已翻面";
                case "InitiativeChoiceRequired": return actor + "需要选择先行动者";
                case "RoundEndReached": return "到达轮末";
                case "MinionRemoved": return "小兵已移除 · " + entry.From;
                case "MinionDefeated": return actor + "击败小兵 · " + entry.From;
                case "GoldAwarded": return actor + "获得 " + entry.Detail + " 金";
                case "MinionsCleared": return "清除旧战区小兵 " + entry.Detail + " 名";
                case "FrontlineAdvanced": return "战线推进至" + RegionName(entry.Detail);
                case "FrontlineMarkGained": return (entry.Detail == "Blue" ? "蓝队" : "红队") + "累计推进 +1";
                case "MinionSpawned": return "小兵出生于 " + entry.To;
                case "MinionSpawnChoiceRequired": return actor + "需要选择小兵出生位置";
                case "SpawnOrderRulingRequired": return "出生位置发生冲突，等待顺序裁定";
                case "FrontlineCompleted": return "推进出生完成，继续原流程";
                case "DebugCrystalSet": return "调试水晶生命已更新";
                case "MatchWon": return (entry.Detail.StartsWith("Blue:") ? "蓝队" : "红队") + "获胜";
                case "PrimaryActionStarted": return actor + "开始主要行动";
                case "AttackTargetChoiceRequired": return actor + "选择攻击目标";
                case "EffectMinionChoiceRequired": return actor + "可额外移除小兵，不获得金币";
                case "EffectMinionRemovalSkipped": return actor + "选择不额外移除小兵";
                case "EffectMinionRemoved": return actor + "完成牌文移除，不获得金币";
                case "AttackRepeatChoiceRequired": return actor + "击败英雄，可选择再次攻击或停止";
                case "AttackRepeated": return actor + "继续本牌的下一次攻击";
                case "AttackRepeatSkipped": return actor + "选择不再重复攻击";
                case "AttackRepeatUnavailable": return actor + "没有可重复攻击的目标";
                case "OptionalDiscardRequired": return actor + "选择攻击前是否弃牌";
                case "OptionalDiscardSkipped": return actor + (entry.Detail=="empty_hand" ? "没有手牌，继续攻击" : "选择不弃牌，继续攻击");
                case "OptionalDiscardCompleted": return actor + "完成攻击前弃牌";
                case "AttackRangeDetermined": return actor + "本次攻击距离为 " + entry.Detail;
                case "AttackTargetChosen": return actor + "确认攻击目标";
                case "AttackDeclared": return actor + "发起攻击";
                case "AttackCalculated": return entry.AttackValues == null ? "计算攻击" : AttackFormula(entry.AttackValues);
                case "DefenseChoiceRequired": return actor + "需要选择防御";
                case "NoDefenseAvailable": return actor + "没有可用防御牌";
                case "DefenseDeclined": return actor + "选择不防御";
                case "DefenseCalculated": return actor + "防御计算 " + entry.Detail;
                case "DefenseResolved": return actor + (entry.Detail == "success" ? "防御成功" : "防御失败");
                case "ForcedDiscardRequired": return actor + "需要处理反制弃牌";
                case "RetaliationDiscardDeclined": return actor + "选择不弃牌并被击败";
                case "ForcedDiscardSkipped": return actor + "没有手牌，继续反制后续步骤";
                case "ForcedDiscardCompleted": return actor + "完成强制弃牌";
                case "DefenseResponseCompleted": return actor + "的防御后处理完成";
                case "ProtectionActivated": return actor + "本回合免疫非相邻英雄的远程攻击";
                case "ProtectionExpired": return actor + "的远程攻击免疫到期";
                case "AttackResolved": return "本次攻击处理完毕";
                case "CardEffectStopped": return "本牌剩余步骤无法执行，结束结算";
                case "HeroDefeated": return actor + "被击败，等待下一张牌前复活";
                case "HeroDefeatSource": return actor + "通过“" + catalog.Card(entry.CardId!).Name + "”击败原攻击者";
                case "AssistGoldAwarded": return actor + "获得助攻 " + entry.Detail + " 金";
                case "CrystalDamaged": return actor + "所在队伍水晶减少 " + entry.Detail;
                case "HeroRespawnChoiceRequired": return actor + "需要选择复活位置";
                case "HeroRespawned": return actor + "已复活，继续本张牌";
                case "RoundEndStarted": return "开始结算轮末";
                case "CardsRecalled": return actor + "回收 " + entry.Detail + " 张牌";
                case "MinionBattleCounted":
                    var minionCounts = entry.Detail.Split(':');
                    return "轮末蓝兵 " + minionCounts[0] + " · 红兵 " + minionCounts[1] + " · 需移除 " + minionCounts[2];
                case "RoundMinionChoiceRequired": return actor + "还须选择移除 " + entry.Detail + " 名小兵";
                case "MinionBattleCompleted": return "轮末小兵战斗完成";
                case "HeroLeveled":
                    var levelChange = entry.Detail.Split(':');
                    return actor + "升至 Lv." + levelChange[1] + "，支付 " + levelChange[2] + " 金";
                case "UpgradesStarted": return "已结算升级费用，等待各自选择";
                case "UpgradeChoiceRequired": return actor + "选择第 " + entry.Detail + " 级升级";
                case "CardUpgraded": return actor + "获得“" + catalog.Card(entry.CardId!).Name + "”";
                case "UpgradeBonusGranted": return actor + "获得永久加成 " + entry.Detail;
                case "PurpleCardGranted": return actor + "获得紫卡“" + catalog.Card(entry.CardId!).Name + "”";
                case "RoundCompensationGranted": return actor + "本轮未升级，获得补偿 1 金";
                case "RoundEnded": return "第 " + entry.Detail + " 轮结算完成";
                case "EngineUpgraded": return "已采用更新的规则能力，原对局记录已保留";
                case "EffectCreated": return actor + "建立“" + catalog.Card(entry.CardId!).Name + "”持续效果";
                case "EffectActivated": return "“" + catalog.Card(entry.CardId!).Name + "”生效";
                case "EffectScheduled": return "“" + catalog.Card(entry.CardId!).Name + "”等待下一回合生效";
                case "EffectExpired": return "“" + catalog.Card(entry.CardId!).Name + "”到期";
                case "EffectCancelled": return actor + "取消了“" + catalog.Card(entry.CardId!).Name + "”的持续效果";
                case "EffectNotScheduled": return "本轮没有下一回合，后续效果不生效";
                case "ActionPassed": return actor + "放弃此牌行动";
                case "DeploymentStarted": return "开始安排出生";
                case "EmptyHandSkipped": return actor + "无手牌，自动跳过";
                case "InitiativeChosen": return actor + "被选为先行动者";
                case "QuickSelectionChanged": return entry.Detail == "on" ? "开启选完即揭示" : "改为四人确认";
                case "DebugGoldChanged": return actor + "调试金币变化 " + entry.Detail;
                case "DebugGoldSet": return actor + "金币设为 " + entry.Detail;
                case "DebugTeleported": return actor + "调试传送至 " + entry.To;
                case "DebugPrepared": return "自动准备完成";
                case "DebugCardEquipped": return actor + "装配测试牌 " + catalog.Card(entry.CardId!).Name;
                case "DebugCoinChanged": return "决策币设为" + (entry.Detail == "blue" ? "蓝队" : "红队");
                case "CardDiscarded": return actor + "弃置 " + catalog.Card(entry.CardId!).Name;
                case "CardRecovered": return actor + "取回 " + catalog.Card(entry.CardId!).Name;
                case "DiscardColorShown": return actor + "弃牌区加入" + ColorName(entry.Detail) + "牌";
                case "RecoveredColorShown": return actor + "取回一张" + ColorName(entry.Detail) + "牌";
                case "DebugAdvanceStarted": return "开始流程快进";
                case "DebugAdvanceFinished": return "流程快进已停止";
                default: return "对局状态已更新";
            }
        }
        private static string Number(int? value) => value?.ToString() ?? "—";
        private static string SubtypeText(CardDefinition card) => card.Subtype == null ? "" : "  ·  " + card.Subtype + " " + card.SubtypeValue;
        private static string ZoneName(CardInstance card)
        {
            switch (card.Zone)
            {
                case CardZone.Selected: return "已暗选 · 待确认";
                case CardZone.PlayedUnresolved: return "第 " + card.PlayedTurn + " 回合 · 待执行";
                case CardZone.PlayedResolved: return "第 " + card.PlayedTurn + " 回合 · 已结算";
                case CardZone.Discarded: return "已弃置"; default: return "可用手牌";
            }
        }
        private void RenderGallery()
        {
            var overlay = Box("gallery-overlay"); root.Add(overlay);
            var header = Box("gallery-header");
            header.Add(Text("卡牌图鉴", "panel-title"));
            header.Add(Text("6 名英雄 / 108 张正式卡牌", "muted"));
            header.Add(Button("返回战场", () => { galleryOpen = false; Render(); }, "primary-button","gallery-return")); overlay.Add(header);
            var tabs = Box("gallery-tabs"); overlay.Add(tabs);
            var all=Button("全部 · 108",()=>{galleryHero="";Render();},"quiet-button","gallery-all");
            if(galleryHero=="") all.AddToClassList("chosen");tabs.Add(all);
            foreach (var hero in catalog.Heroes)
            {
                string id = hero.Id;
                var tab = Button(HeroName(id) + " · 18", () => { galleryHero = id; Render(); }, "quiet-button","gallery-hero-"+id);
                if (galleryHero == id) tab.AddToClassList("chosen"); tabs.Add(tab);
            }
            overlay.Add(Text("以下为正式牌面数据；每张牌标明当前可执行的主要行动或防御响应。", "gallery-note"));
            var filters=Box("gallery-filters");overlay.Add(filters);
            var query=new TextField("搜索") {name="gallery-query",value=galleryQuery,maxLength=128};query.AddToClassList("gallery-search");filters.Add(query);
            var supported=new HashSet<string>(renderedView.SupportedPrimaryCards.Concat(renderedView.SupportedDefenseCards));
            Button supportedButton=null!;
            var count=Text("","muted");count.name="gallery-count";overlay.Add(count);
            var scroll = new ScrollView(); scroll.AddToClassList("gallery-scroll"); overlay.Add(scroll);
            var colorRows=new Dictionary<string,ScrollView>();
            foreach(string color in new[]{"gold","silver","red","green","blue","purple"})
            {
                scroll.Add(Text(ColorName(color)+"色","section-title"));
                var colorRow=new ScrollView(ScrollViewMode.Horizontal) {name="gallery-row-"+color};
                colorRow.AddToClassList("gallery-color-row");colorRow.contentContainer.style.flexDirection=FlexDirection.Row;
                scroll.Add(colorRow);colorRows[color]=colorRow;
            }
            var tiles=new List<(CardDefinition card,VisualElement tile)>();
            var empty=Text("没有匹配卡牌。请修改关键词或筛选条件。","body");empty.name="gallery-empty";overlay.Add(empty);
            void FilterCards()
            {
                string term=galleryQuery.Trim();int visible=0;
                foreach(var entry in tiles)
                {
                    var c=entry.card;
                    bool matches=(!galleryOnlySupported || supported.Contains(c.Id)) &&
                        (term=="" || c.Id.IndexOf(term,StringComparison.OrdinalIgnoreCase)>=0 || c.Name.IndexOf(term,StringComparison.OrdinalIgnoreCase)>=0 || c.Text.IndexOf(term,StringComparison.OrdinalIgnoreCase)>=0 || HeroName(c.HeroId).IndexOf(term,StringComparison.OrdinalIgnoreCase)>=0);
                    entry.tile.style.display=matches ? DisplayStyle.Flex : DisplayStyle.None;if(matches) visible++;
                }
                count.text="显示 "+visible+" / "+tiles.Count+" 张 · "+(galleryHero=="" ? "全部英雄" : HeroName(galleryHero));
                empty.style.display=visible==0 ? DisplayStyle.Flex : DisplayStyle.None;
                supportedButton.text=galleryOnlySupported ? "本局可用 ✓" : "只看本局可用";
                scroll.scrollOffset=Vector2.zero;RequestCapture();
            }
            query.RegisterValueChangedCallback(e=>{galleryQuery=e.newValue;FilterCards();});
            filters.Add(Button("清空",()=>{query.value="";query.Focus();},"quiet-button","gallery-clear"));
            supportedButton=Button("",()=>{galleryOnlySupported=!galleryOnlySupported;FilterCards();},"quiet-button","gallery-supported");filters.Add(supportedButton);
            foreach (var card in catalog.Cards.Where(c => galleryHero=="" || c.HeroId == galleryHero))
            {
                var tile = Box("gallery-card");tile.name="inspect-gallery-"+card.Id; tile.AddToClassList("color-" + card.Color);
                tile.style.borderTopColor=CardColor(card.Color);
                var name=Text(card.Name,"card-name");name.name="gallery-card-name-"+card.Id;tile.Add(name);
                tile.Add(Text((card.Color=="purple" ? "紫卡 · 英雄8级获得" : card.Level.HasValue ? "卡牌等级 " + card.Level : "基础牌") + "    先攻 " + card.Initiative, "muted"));
                if(galleryHero=="") tile.Add(Text(HeroName(card.HeroId),"tiny"));
                tile.Add(Text(card.PrimaryCategory + " " + (card.Exclamation ? "!" : card.PrimaryValue.ToString()) + SubtypeText(card), "body"));
                tile.Add(RulesText(card.Text, "card-rules"));
                tile.Add(Text("次要移动 " + Number(card.SecondaryMovement) + "    次要防御 " + Number(card.SecondaryDefense), "tiny"));
                tile.Add(Text("底部被动 " + (card.Passive ?? "无"), "tiny"));
                tile.Add(Text(renderedView.SupportedPrimaryCards.Contains(card.Id) ? "主要行动 · 已开放" : renderedView.SupportedDefenseCards.Contains(card.Id) ? "防御响应 · 已开放" : "牌文效果 · 待实施", "status-badge")); colorRows[card.Color].Add(tile);
                tiles.Add((card,tile));
                AttachCardReading(tile,card);
            }
            FilterCards();
        }
        private void RenderPublicCards(GameView view)
        {
            var overlay = Box("gallery-overlay"); root.Add(overlay);
            var header = Box("gallery-header"); header.Add(Text("公开已出牌", "panel-title"));
            header.Add(Button("返回战场", () => { publicCardsOpen = false; Render(); }, "primary-button")); overlay.Add(header);
            overlay.Add(Text("保留实际出牌轮次与回合。未翻开的手牌不会出现在这里。", "gallery-note"));
            var scroll = new ScrollView(); scroll.AddToClassList("gallery-scroll"); overlay.Add(scroll);
            scroll.contentContainer.AddToClassList("gallery-grid");
            foreach (var player in view.Players)
            foreach (var play in player.Plays.OrderByDescending(p => p.Round).ThenByDescending(p => p.Turn))
            {
                var card = catalog.Card(play.CardId);
                var tile = Box("gallery-card"); tile.AddToClassList("color-" + card.Color);
                tile.Add(Text("席位 " + (player.Seat + 1) + " · " + HeroName(player.HeroId), "eyebrow"));
                tile.Add(Text(card.Name + " · 先攻 " + card.Initiative, "card-name"));
                tile.Add(Text("第 " + play.Round + " 轮 · 第 " + play.Turn + " 回合", "muted"));
                tile.Add(Text(card.PrimaryCategory + " " + (card.Exclamation ? "!" : card.PrimaryValue.ToString()) + SubtypeText(card), "body"));
                tile.Add(RulesText(card.Text, "card-rules"));
                tile.Add(Text("次要移动 " + Number(card.SecondaryMovement) + " · 次要防御 " + Number(card.SecondaryDefense), "tiny"));
                tile.Add(Text("底部被动 " + (card.Passive ?? "无"), "tiny")); scroll.Add(tile);
                AttachCardReading(tile,card,player);
            }
            if (view.Players.All(player => player.Plays.Count == 0)) scroll.Add(Text("本对局尚未揭示卡牌。", "body"));
        }
        private void RenderNewMatchDialog()
        {
            var overlay = Box("dialog-overlay"); root.Add(overlay);
            var dialog = Box("dialog"); overlay.Add(dialog);
            dialog.Add(Text("开始新对局", "panel-title"));
            dialog.Add(Text("将先保存当前对局，再建立新的四人热座。", "body"));
            if(newMatchError!="") { var message=Text(newMatchError,"restriction-text");message.name="new-match-save-error";dialog.Add(message); }
            dialog.Add(Button("保存并开始", () => { if (SaveCurrent()) NewMatch(); else { newMatchError="保存失败，当前对局仍保留。请检查保存目录是否可写后重试，或继续当前对局。"; Render(); } }, "primary-button"));
            dialog.Add(Button("继续当前对局", () => { newMatchPending = false;newMatchError=""; Render(); }, "quiet-button"));
        }
        private bool SaveCurrent()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SavePath)!);
                string temporary = SavePath + ".tmp";
                File.WriteAllText(temporary, session.ExportSave(), new System.Text.UTF8Encoding(false));
                if (File.Exists(SavePath)) File.Replace(temporary, SavePath, SavePath + ".bak");
                else File.Move(temporary, SavePath);
                notice = "对局已保存。"; return true;
            }
            catch (Exception error) { notice = "保存失败：" + error.Message; Debug.LogException(error); return false; }
        }
        private void Save() { SaveCurrent(); Render(); }
        private void Load()
        {
            try
            {
                var restored = LocalGameFactory.Restore(catalog, File.ReadAllText(SavePath, System.Text.Encoding.UTF8));
                session = restored; ClearPending(); notice = "已恢复对局。使用1/2/3/4切换角色。";
            }
            catch (Exception error) { notice = "无法读取存档：" + error.Message; Debug.LogWarning(error.Message); }
            Render();
        }
    }
}
