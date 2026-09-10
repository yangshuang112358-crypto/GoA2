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
        private string notice = "";
        private string? galleryHero;
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
                int loadIndex = Array.IndexOf(arguments, "-goaLoad");
                if (loadIndex >= 0 && loadIndex + 1 < arguments.Length)
                {
                    session = LocalGameFactory.Restore(catalog, File.ReadAllText(arguments[loadIndex + 1]));
                    notice = "已恢复指定存档。"; Render();
                }
                else NewMatch();
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                root.Add(new Label("无法加载项目内容。请先运行 tools/prepare_unity.py，再重新启动。\n" + error.Message));
            }
        }
        private void NewMatch()
        {
            session = LocalGameFactory.Create(catalog, Guid.NewGuid().ToString("N"), new[] { "玩家 1", "玩家 2", "玩家 3", "玩家 4" }, UnityEngine.Random.Range(0, int.MaxValue), true);
            seat = 0; newMatchPending = false; notice = "测试对局已建立。可手工选英雄，或打开调试工具自动准备。";
            ClearPending(); Render();
        }
        private void ClearPending()
        {
            chosenHero = null; chosenCell = null; moveMode = null; initiativeSeat = null; passPending = false; deploymentSeat = -1;
        }
        private void Submit(CommandKind kind, string value = "", int target = -1, Hex destination = default, MoveMode mode = MoveMode.Secondary)
        {
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
        private static string PhaseName(Phase phase)
        {
            switch (phase)
            {
                case Phase.HeroSelection: return "选择英雄";
                case Phase.Deployment: return "安排出生";
                case Phase.Planning: return "暗选卡牌";
                case Phase.InitiativeChoice: return "先攻决策";
                case Phase.Action: return "执行行动";
                default: return "到达轮末";
            }
        }
        private void Render()
        {
            root.Query<ScrollView>().ForEach(scroll => { if (scroll.name.StartsWith("goa-scroll-")) scrollPositions[scroll.name] = scroll.scrollOffset; });
            root.Clear();
            renderedView = session.View(seat);
            BuildLayout(renderedView);
            if (galleryOpen) RenderGallery();
            if (publicCardsOpen) RenderPublicCards(renderedView);
            if (newMatchPending) RenderNewMatchDialog();
            root.Query<ScrollView>().ForEach(scroll =>
            {
                if (scrollPositions.TryGetValue(scroll.name, out var offset)) scroll.schedule.Execute(() => scroll.scrollOffset = offset);
                scroll.verticalScroller.valueChanged += _ => RequestCapture();
            });
            RequestCapture();
        }
        private void RequestCapture()
        {
            if (screenshotPath == null) return;
            captureJob?.Pause();
            captureJob = root.schedule.Execute(() => StartCoroutine(CaptureFrame(++screenshotRevision))).StartingIn(100);
        }
        private static void SeatLabel(VisualElement parent, string caption, string css, float top, float height)
        {
            var label = Text(caption, css);
            label.style.position = Position.Absolute;
            label.style.left = 10; label.style.right = 5; label.style.top = top; label.style.height = height;
            label.style.marginTop = 0; label.style.marginBottom = 0; label.style.paddingTop = 0; label.style.paddingBottom = 0;
            parent.Add(label);
        }
        private IEnumerator CaptureFrame(int revision)
        {
            yield return null;
            yield return new WaitForEndOfFrame();
            if (revision != screenshotRevision || screenshotPath == null) yield break;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(screenshotPath))!);
            var layout = new QaLayout { Width = Screen.width, Height = Screen.height, Seat = seat, Revision = renderedView.Revision,
                Zoom = viewport.Zoom, Focus = viewport.Focus, Phase = renderedView.Phase.ToString(), Round = renderedView.Round, Turn = renderedView.Turn,
                LeftExpanded = leftExpanded, RightExpanded = rightExpanded, TopExpanded = topExpanded, BottomExpanded = bottomExpanded,
                SelectedCell = chosenCell.HasValue ? chosenCell.Value.ToString() : "", BoardBounds = board?.worldBound ?? default,
                ActiveSeat = renderedView.ActiveSeat ?? -1, RevealedHeading = root.Q<Label>("revealed-heading")?.text ?? "",
                FilledPlayDots = root.Query<VisualElement>(className: "filled-dot").ToList().Count,
                DiscardDotCount = root.Query<VisualElement>(className: "discard-dot").ToList().Count };
            root.Query<Button>().ForEach(button => layout.Buttons.Add(new QaButton { Name = button.name, Text = button.text, Bounds = button.worldBound, Enabled = button.enabledInHierarchy, Visible = VisibleCenter(button) }));
            root.Query<IntegerField>().ForEach(field => layout.Fields.Add(new QaField { Name = field.name, Bounds = field.worldBound, Value = field.value }));
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
                if (ancestor is ScrollView scroll && !scroll.contentViewport.worldBound.Contains(center)) return false;
            return center.x >= 0 && center.y >= 0 && center.x < Screen.width && center.y < Screen.height;
        }
        [Serializable] private sealed class QaLayout
        {
            public int Width, Height, Seat, Round, Turn, ActiveSeat, FilledPlayDots, DiscardDotCount; public long Revision; public float Zoom; public Vector2 Focus; public Rect BoardBounds;
            public string Phase = "", SelectedCell = "", RevealedHeading = "";
            public bool LeftExpanded, RightExpanded, TopExpanded, BottomExpanded;
            public List<QaButton> Buttons = new List<QaButton>(); public List<QaCell> Cells = new List<QaCell>();
            public List<QaField> Fields = new List<QaField>();
        }
        [Serializable] private sealed class QaButton { public string Name = "", Text = ""; public Rect Bounds; public bool Enabled, Visible; }
        [Serializable] private sealed class QaCell { public int X, Y; public Vector2 Center; public bool Legal; }
        [Serializable] private sealed class QaField { public string Name = ""; public Rect Bounds; public int Value; }
        private List<Hex> LegalCells(GameView view)
        {
            if (debugTeleport && view.DebugTeleports.TryGetValue(debugUnitId, out var teleportTargets)) return teleportTargets;
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
            sidebar.Add(Text(PhaseName(view.Phase), "panel-title"));
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
                    if (view.Players[seat].Confirmed) sidebar.Add(Text("你已确认。等待其他有手牌的玩家确认后，自动翻牌。", "body"));
                    else
                    {
                        sidebar.Add(Text(view.QuickSelection ? "从下方选一张牌；四人选完立即揭示，之前可改选。" : "从下方手牌选一张。确认前可点击其他手牌改选。", "body"));
                        var selected = view.OwnCards.FirstOrDefault(c => c.Zone == CardZone.Selected);
                        if (selected != null)
                        {
                            RenderCardDetail(sidebar, catalog.Card(selected.CardId));
                            if (!view.QuickSelection) sidebar.Add(Button("确认出牌", () => Submit(CommandKind.ConfirmCard), "primary-button"));
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
                        var normal = Button("次要移动" + (moveMode == MoveMode.Secondary ? "  ✓" : ""), () => { debugTeleport = false; moveMode = MoveMode.Secondary; chosenCell = null; passPending = false; Render(); }, "choice-button");
                        normal.SetEnabled(view.SecondaryMoves.Count > 0); sidebar.Add(normal);
                        var fast = Button("快速移动" + (moveMode == MoveMode.Fast ? "  ✓" : ""), () => { debugTeleport = false; moveMode = MoveMode.Fast; chosenCell = null; passPending = false; Render(); }, "choice-button");
                        fast.SetEnabled(view.FastMoves.Count > 0); sidebar.Add(fast);
                        if (chosenCell.HasValue && moveMode.HasValue)
                            Confirm(sidebar, "确认移动至 " + chosenCell.Value, () => Submit(CommandKind.Move, destination: chosenCell!.Value, mode: moveMode!.Value));
                        else if (passPending) Confirm(sidebar, "确认放弃此牌行动", () => Submit(CommandKind.Pass));
                        else sidebar.Add(Button("放弃此牌行动", () => { passPending = true; moveMode = null; chosenCell = null; Render(); }, "quiet-button"));
                        sidebar.Add(Text("主要行动效果待实施；可执行已开放的基础行动。", "tiny"));
                    }
                    break;
                case Phase.RoundEnd:
                    sidebar.Add(Text("已完成本轮四个回合。", "section-title"));
                    sidebar.Add(Text("轮末回收、兵线结算与升级将在后续切片接入。当前状态可保存，尚不能开始下一轮。", "body"));
                    sidebar.Add(Button("保存本轮进度", Save, "primary-button"));
                    break;
            }
            RenderRecentEvents(sidebar, view);
        }
        private void Confirm(VisualElement parent, string caption, Action action)
        {
            var box = Box("confirm-box"); parent.Add(box);
            box.Add(Button(caption, action, "primary-button"));
            box.Add(Button("取消", () => { ClearPending(); Render(); }, "quiet-button"));
        }
        private void RenderCardDetail(VisualElement parent, CardDefinition card)
        {
            var box = Box("card-detail");
            box.Add(Text(card.Name + "    先攻 " + card.Initiative, "section-title"));
            box.Add(Text(card.PrimaryCategory + " " + (card.Exclamation ? "!" : card.PrimaryValue.ToString()) + SubtypeText(card), "muted"));
            var scroll = new ScrollView(); scroll.AddToClassList("detail-text");
            scroll.Add(Text(card.Text, "card-rules")); box.Add(scroll);
            box.Add(Text("移 " + Number(card.SecondaryMovement) + "    防 " + Number(card.SecondaryDefense), "tiny"));
            parent.Add(box);
        }
        private void RenderRecentEvents(VisualElement parent, GameView view)
        {
            var box = Box("event-box"); box.Add(Text("最近记录", "eyebrow"));
            foreach (var entry in view.Events.TakeLast(4))
                box.Add(Text(EventText(entry), "tiny"));
            parent.Add(box);
        }
        private string EventText(GameEvent entry)
        {
            string actor = entry.Seat.HasValue ? "席位 " + (entry.Seat.Value + 1) : "";
            switch (entry.Kind)
            {
                case "HeroChosen": return actor + "选择 " + HeroName(entry.Detail);
                case "HeroDeployed": return actor + "已出生";
                case "CardSelected": return actor + "更新了暗选";
                case "SelectionConfirmed": return actor + "已确认";
                case "CardRevealed": return actor + "揭示 " + catalog.Card(entry.CardId!).Name;
                case "UnitMoved": return actor + "移动至 " + entry.To;
                case "ActionStarted": return actor + "开始行动";
                case "CardResolved": return actor + "的牌已结算";
                case "PlanningStarted": return "进入暗选 " + entry.Detail;
                case "TurnEnded": return "回合结束 " + entry.Detail;
                case "DecisionCoinFlipped": return "跨队同先攻，决策币已翻面";
                case "InitiativeChoiceRequired": return actor + "需要选择先行动者";
                case "RoundEndReached": return "到达轮末";
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
            header.Add(Button("返回战场", () => { galleryOpen = false; Render(); }, "primary-button")); overlay.Add(header);
            var tabs = Box("gallery-tabs"); overlay.Add(tabs);
            foreach (var hero in catalog.Heroes)
            {
                string id = hero.Id;
                var tab = Button(HeroName(id) + " · 18", () => { galleryHero = id; Render(); }, "quiet-button");
                if (galleryHero == id) tab.AddToClassList("chosen"); tabs.Add(tab);
            }
            overlay.Add(Text("以下为正式牌面数据；全部主要效果仍待逐卡实施与测试。", "gallery-note"));
            var scroll = new ScrollView(); scroll.AddToClassList("gallery-scroll"); overlay.Add(scroll);
            scroll.contentContainer.AddToClassList("gallery-grid");
            foreach (var card in catalog.Cards.Where(c => c.HeroId == galleryHero))
            {
                var tile = Box("gallery-card"); tile.AddToClassList("color-" + card.Color);
                tile.Add(Text(card.Name, "card-name"));
                tile.Add(Text((card.Level.HasValue ? "卡牌等级 " + card.Level : "基础牌") + "    先攻 " + card.Initiative, "muted"));
                tile.Add(Text(card.PrimaryCategory + " " + (card.Exclamation ? "!" : card.PrimaryValue.ToString()) + SubtypeText(card), "body"));
                tile.Add(Text(card.Text, "card-rules"));
                tile.Add(Text("次要移动 " + Number(card.SecondaryMovement) + "    次要防御 " + Number(card.SecondaryDefense), "tiny"));
                tile.Add(Text("底部被动 " + (card.Passive ?? "无"), "tiny"));
                tile.Add(Text("牌文效果 · 待实施", "status-badge")); scroll.Add(tile);
            }
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
                tile.Add(Text(card.Text, "card-rules"));
                tile.Add(Text("次要移动 " + Number(card.SecondaryMovement) + " · 次要防御 " + Number(card.SecondaryDefense), "tiny"));
                tile.Add(Text("底部被动 " + (card.Passive ?? "无"), "tiny")); scroll.Add(tile);
            }
            if (view.Players.All(player => player.Plays.Count == 0)) scroll.Add(Text("本对局尚未揭示卡牌。", "body"));
        }
        private void RenderNewMatchDialog()
        {
            var overlay = Box("dialog-overlay"); root.Add(overlay);
            var dialog = Box("dialog"); overlay.Add(dialog);
            dialog.Add(Text("开始新对局", "panel-title"));
            dialog.Add(Text("将先保存当前对局，再建立新的四人热座。", "body"));
            dialog.Add(Button("保存并开始", () => { if (SaveCurrent()) NewMatch(); }, "primary-button"));
            dialog.Add(Button("继续当前对局", () => { newMatchPending = false; Render(); }, "quiet-button"));
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
