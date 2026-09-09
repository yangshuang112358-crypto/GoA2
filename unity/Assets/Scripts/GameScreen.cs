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
    public sealed class GameScreen : MonoBehaviour
    {
        private ContentCatalog catalog = null!;
        private GameSession session = null!;
        private VisualElement root = null!;
        private Font font = null!;
        private int seat;
        private bool curtain;
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
        private int screenshotRevision;
        private Label cellInfo = null!;
        private static bool created;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlayMode() { created = false; }
        private string SavePath => Path.Combine(UnityApplication.persistentDataPath, "saves", "hotseat-v1.json");

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
                catalog = ContentLoader.LoadDirectory(Path.Combine(UnityApplication.streamingAssetsPath, "Goa2"));
                NewMatch();
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                root.Add(new Label("无法加载项目内容。请先运行 tools/prepare_unity.py，再重新启动。\n" + error.Message));
            }
        }
        private void NewMatch()
        {
            session = LocalGameFactory.Create(catalog, Guid.NewGuid().ToString("N"), new[] { "玩家 1", "玩家 2", "玩家 3", "玩家 4" }, UnityEngine.Random.Range(0, int.MaxValue));
            seat = 0; curtain = false; newMatchPending = false; notice = "选择英雄，开始四人热座基础流程。";
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
        private static Button Button(string text, Action action, string css = "button")
        {
            var button = new Button(action) { text = text }; button.AddToClassList(css); return button;
        }
        private string HeroName(string? id) => catalog.Heroes.FirstOrDefault(h => h.Id == id)?.Name.Split('·').Last() ?? "未选英雄";
        private string PlayerName(int number) => "席位 " + (number + 1) + " · " + HeroName(session.View(null).Players[number].HeroId);
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
            root.Clear();
            var shell = Box("shell"); root.Add(shell);
            var view = session.View(curtain ? (int?)null : seat);
            var header = Box("header"); shell.Add(header);
            var brand = Box("brand");
            brand.Add(Text("GOA II", "brand-title")); brand.Add(Text("GOA2V1  /  基础流程", "eyebrow")); header.Add(brand);
            var phase = Box("phase-banner");
            phase.Add(Text("第 " + view.Round + " 轮  ·  回合 " + view.Turn + " / 4", "muted"));
            phase.Add(Text(PhaseName(view.Phase), "phase-title")); header.Add(phase);
            var controls = Box("header-controls"); header.Add(controls);
            controls.Add(Button("已揭示牌", () => { publicCardsOpen = true; Render(); }, "quiet-button"));
            controls.Add(Button("卡牌图鉴 · 108", () => { galleryOpen = true; galleryHero = catalog.Heroes[0].Id; Render(); }, "quiet-button"));
            controls.Add(Button("保存", Save, "quiet-button"));
            controls.Add(Button("读取", Load, "quiet-button"));
            controls.Add(Button("新对局", () => { newMatchPending = true; Render(); }, "quiet-button"));
            var main = Box("main"); shell.Add(main);
            var roster = new ScrollView(ScrollViewMode.Vertical); roster.AddToClassList("roster"); main.Add(roster);
            roster.Add(Text("四人热座", "eyebrow"));
            roster.Add(Text("切换席位后，点击显示手牌。", "tiny"));
            foreach (var player in view.Players)
            {
                int targetSeat = player.Seat;
                var card = Button("", () => { seat = targetSeat; curtain = true; ClearPending(); Render(); }, "seat-card");
                if (seat == targetSeat) card.AddToClassList("selected-seat");
                card.AddToClassList(player.Team == Team.Blue ? "blue-seat" : "red-seat");
                string captain = targetSeat == view.BlueCaptain || targetSeat == view.RedCaptain ? " · 队长" : "";
                SeatLabel(card, (player.Team == Team.Blue ? "蓝队" : "红队") + "  /  " + (targetSeat + 1) + captain, "eyebrow", 8, 16);
                SeatLabel(card, HeroName(player.HeroId), "seat-name", 29, 24);
                SeatLabel(card, player.Name + "    Lv." + player.Level + "    " + player.Gold + " 金", "tiny", 58, 16);
                SeatLabel(card, view.ActiveSeat == targetSeat ? "正在行动" : player.Confirmed && view.Phase == Phase.Planning ? "已确认" : "手牌 " + player.HandCount, "seat-state", 79, 16);
                roster.Add(card);
            }
            var teamInfo = Box("team-info");
            teamInfo.Add(Text("水晶", "eyebrow"));
            teamInfo.Add(Text("蓝队 " + view.BlueCrystal + "     红队 " + view.RedCrystal, "body"));
            teamInfo.Add(Text("决策币  ·  " + (view.DecisionCoin == Team.Blue ? "蓝" : "红"), "muted"));
            teamInfo.Add(Text("当前战区  ·  " + RegionName(view.CombatRegion), "muted")); roster.Add(teamInfo);
            var field = Box("field"); main.Add(field);
            var fieldHeader = Box("field-header");
            fieldHeader.Add(Text("亚特兰蒂斯战场", "section-title"));
            fieldHeader.Add(Text("正式地图 · 254 格", "muted")); field.Add(fieldHeader);
            var targets = LegalCells(view);
            var board = new HexBoard(catalog, view, targets, chosenCell, cell =>
            {
                if (!targets.Contains(cell)) { notice = "此格不可用于当前操作。"; return; }
                chosenCell = cell; Render();
            }, cell =>
            {
                if (cellInfo != null) cellInfo.text = RegionName(cell.Region) + "  ·  (" + cell.Position + ")" + (cell.Obstacle ? "  障碍" : "") + (targets.Contains(cell.Position) ? "  可选目标" : "");
            });
            field.Add(board);
            var legend = Box("board-footer");
            cellInfo = Text(targets.Count > 0 ? targets.Count + " 个合法目标 · 点击地图后确认" : "将鼠标移到地图上查看格子", "tiny");
            legend.Add(cellInfo); legend.Add(Text("○ 出生点     1—4 英雄     兵 / 弓 / 重 小兵", "tiny")); field.Add(legend);
            var sidebar = new ScrollView(ScrollViewMode.Vertical); sidebar.AddToClassList("sidebar"); main.Add(sidebar);
            RenderSidebar(sidebar, view);
            RenderHand(shell, view);
            var footer = Box("footer");
            footer.Add(Text(notice, "tiny"));
            footer.Add(Text("主要牌文效果待实施 · 本切片止于轮末", "tiny")); shell.Add(footer);
            if (galleryOpen) RenderGallery();
            if (publicCardsOpen) RenderPublicCards(view);
            if (newMatchPending) RenderNewMatchDialog();
            if (screenshotPath != null) StartCoroutine(CaptureFrame(++screenshotRevision));
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
            var layout = new QaLayout();
            root.Query<Button>().ForEach(button => layout.Buttons.Add(new QaButton { Text = button.text, Bounds = button.worldBound, Enabled = button.enabledInHierarchy }));
            File.WriteAllText(Path.ChangeExtension(screenshotPath, ".ui.json"), JsonUtility.ToJson(layout));
            ScreenCapture.CaptureScreenshot(screenshotPath);
        }
        [Serializable] private sealed class QaLayout { public List<QaButton> Buttons = new List<QaButton>(); }
        [Serializable] private sealed class QaButton { public string Text = ""; public Rect Bounds; public bool Enabled; }
        private List<Hex> LegalCells(GameView view)
        {
            if (curtain) return new List<Hex>();
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
            sidebar.Add(Text(curtain ? "交接屏幕" : PhaseName(view.Phase), "panel-title"));
            if (curtain)
            {
                sidebar.Add(Text("请将屏幕交给" + PlayerName(seat) + "。", "body"));
                sidebar.Add(Button("显示我的手牌与操作", () => { curtain = false; Render(); }, "primary-button"));
                RenderRecentEvents(sidebar, view); return;
            }
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
                        sidebar.Add(Text("从下方手牌选一张。确认前可点击其他手牌改选。", "body"));
                        var selected = view.OwnCards.FirstOrDefault(c => c.Zone == CardZone.Selected);
                        if (selected != null)
                        {
                            RenderCardDetail(sidebar, catalog.Card(selected.CardId));
                            sidebar.Add(Button("确认出牌", () => Submit(CommandKind.ConfirmCard), "primary-button"));
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
                        var normal = Button("次要移动" + (moveMode == MoveMode.Secondary ? "  ✓" : ""), () => { moveMode = MoveMode.Secondary; chosenCell = null; passPending = false; Render(); }, "choice-button");
                        normal.SetEnabled(view.SecondaryMoves.Count > 0); sidebar.Add(normal);
                        var fast = Button("快速移动" + (moveMode == MoveMode.Fast ? "  ✓" : ""), () => { moveMode = MoveMode.Fast; chosenCell = null; passPending = false; Render(); }, "choice-button");
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
                default: return actor + (entry.Kind == "DeploymentStarted" ? "开始安排出生" : entry.Kind == "EmptyHandSkipped" ? "无手牌，自动跳过" : "先攻选择已确认");
            }
        }
        private void RenderHand(VisualElement shell, GameView view)
        {
            var hand = Box("hand"); shell.Add(hand);
            var title = Box("hand-title"); title.Add(Text("我的卡牌", "section-title"));
            title.Add(Text(curtain ? "内容已隐藏" : PlayerName(seat) + "  ·  已出牌保留真实回合记录", "tiny")); hand.Add(title);
            if (curtain) { hand.Add(Text("点击右侧“显示我的手牌与操作”后继续。", "curtain-text")); return; }
            if (view.OwnCards.Count == 0) { hand.Add(Text("选定英雄后，将获得金、银、红、绿、蓝五张起始牌。", "curtain-text")); return; }
            var row = Box("hand-row"); hand.Add(row);
            foreach (var instance in view.OwnCards)
            {
                var card = catalog.Card(instance.CardId);
                var tile = Button("", () =>
                {
                    if (view.Phase == Phase.Planning && !view.Players[seat].Confirmed && (instance.Zone == CardZone.InHand || instance.Zone == CardZone.Selected))
                        Submit(CommandKind.SelectCard, card.Id);
                    else { galleryHero = card.HeroId; galleryOpen = true; Render(); }
                }, "hand-card");
                tile.AddToClassList("color-" + card.Color);
                if (instance == view.OwnCards.Last()) tile.AddToClassList("last-card");
                if (instance.Zone == CardZone.Selected) tile.AddToClassList("chosen");
                if (instance.Zone == CardZone.PlayedResolved || instance.Zone == CardZone.Discarded) tile.AddToClassList("spent");
                SeatLabel(tile, card.Name, "card-name", 8, 25);
                SeatLabel(tile, card.PrimaryCategory + " " + (card.Exclamation ? "!" : card.PrimaryValue.ToString()), "body", 39, 23);
                SeatLabel(tile, "先攻 " + card.Initiative + "     移 " + Number(card.SecondaryMovement) + " / 防 " + Number(card.SecondaryDefense), "tiny", 68, 18);
                SeatLabel(tile, instance.Zone == CardZone.Selected && view.Players[seat].Confirmed ? "已确认 · 等待翻牌" : ZoneName(instance), "card-zone", 91, 16); row.Add(tile);
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
            foreach (var instance in player.Revealed)
            {
                var card = catalog.Card(instance.CardId);
                var tile = Box("gallery-card"); tile.AddToClassList("color-" + card.Color);
                tile.Add(Text("席位 " + (player.Seat + 1) + " · " + HeroName(player.HeroId), "eyebrow"));
                tile.Add(Text(card.Name + " · 先攻 " + card.Initiative, "card-name"));
                tile.Add(Text("第 " + instance.PlayedRound + " 轮 · " + ZoneName(instance), "muted"));
                tile.Add(Text(card.PrimaryCategory + " " + (card.Exclamation ? "!" : card.PrimaryValue.ToString()) + SubtypeText(card), "body"));
                tile.Add(Text(card.Text, "card-rules"));
                tile.Add(Text("次要移动 " + Number(card.SecondaryMovement) + " · 次要防御 " + Number(card.SecondaryDefense), "tiny"));
                tile.Add(Text("底部被动 " + (card.Passive ?? "无"), "tiny")); scroll.Add(tile);
            }
            if (view.Players.All(player => player.Revealed.Count == 0)) scroll.Add(Text("本对局尚未揭示卡牌。", "body"));
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
                session = restored; curtain = true; ClearPending(); notice = "已恢复对局。请确认当前操作者后显示手牌。";
            }
            catch (Exception error) { notice = "无法读取存档：" + error.Message; Debug.LogWarning(error.Message); }
            Render();
        }
    }
}
