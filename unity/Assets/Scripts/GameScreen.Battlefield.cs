#nullable enable
using Goa2.Domain;
using System.Linq;
using UnityEngine.UIElements;

namespace Goa2.Presentation
{
    public sealed partial class GameScreen
    {
        private string spawnUnitId = "";
        private void PrepareSpawnSelection(GameView view)
        {
            if (!view.SpawnChoices.ContainsKey(spawnUnitId)) spawnUnitId = view.SpawnChoices.Keys.FirstOrDefault() ?? "";
        }
        private static string MinionName(UnitState unit) => (unit.Team == Team.Blue ? "蓝方" : "红方") +
            (unit.Kind == "heavy" ? "重型" : unit.Kind == "ranged" ? "远程" : "近战") + "小兵";
        private void RenderBattlefieldChoice(VisualElement parent, GameView view)
        {
            var choice = view.Pending;
            if (choice == null) { parent.Add(Text("等待结算。", "body")); return; }
            if (choice.Kind == "spawn_order_unresolved")
            {
                parent.Add(Text("此旧版存档保留了此前暂停的出生冲突。新版对局已采用队长和决策币顺序。", "body"));
                parent.Add(Text("可保存此局面；测试中也可传送占位英雄后继续。", "tiny")); return;
            }
            if (choice.Kind != "minion_spawn") { parent.Add(Text("等待" + PlayerName(choice.ChooserSeat) + "完成选择。", "body")); return; }
            PrepareSpawnSelection(view);
            var selected = view.PendingSpawns.FirstOrDefault(s => s.Id == spawnUnitId) ?? view.PendingSpawn;
            string unit = selected == null ? "小兵" : MinionName(selected);
            parent.Add(Text(unit + "的出生点被占据。", "section-title"));
            parent.Add(Text(PlayerName(choice.ChooserSeat) + "选择相邻空格。", "body"));
            if (choice.ChooserSeat != seat) return;
            if (view.SpawnChoices.Count > 1)
            {
                parent.Add(Text("先选择本队哪名小兵出生。", "body"));
                foreach (var pendingUnit in view.PendingSpawns.Where(s => view.SpawnChoices.ContainsKey(s.Id)))
                {
                    string id = pendingUnit.Id;
                    var option = Button(MinionName(pendingUnit) + " · " + pendingUnit.Position, () => { spawnUnitId = id; chosenCell = null; Render(); }, "choice-button");
                    if (id == spawnUnitId) option.AddToClassList("chosen"); parent.Add(option);
                }
            }
            if (!view.SpawnChoices.TryGetValue(spawnUnitId, out var cells) || cells.Count == 0)
                parent.Add(Text("目前没有合法空格，已保留待出生任务。满占位规则待裁定；测试中可以传送占位单位。", "body"));
            else
            {
                if (chosenCell.HasValue) Confirm(parent, "确认小兵出生于 " + chosenCell.Value, () => Submit(CommandKind.ChooseMinionSpawn, spawnUnitId, destination: chosenCell!.Value));
                else parent.Add(Text("点击地图高亮格后确认。", "muted"));
            }
        }
        private void RenderVictory(VisualElement parent, GameView view)
        {
            parent.Add(Text((view.Winner == Team.Blue ? "蓝队" : "红队") + "获胜", "phase-title"));
            parent.Add(Text(view.VictoryReason == "crystal" ? "对方水晶生命归零。" : view.VictoryReason == "fountain" ? "战线已推进至敌方泉水。" : "累计推进次数达到胜利目标。", "body"));
            parent.Add(Text("蓝队推进 " + view.BlueMarks + " · 红队推进 " + view.RedMarks, "body"));
            parent.Add(Button("保存对局结果", Save, "primary-button"));
            var next = Button("开始新对局", () => { newMatchPending = true; Render(); }, "choice-button"); next.SetEnabled(!ScenarioRunning); parent.Add(next);
        }
    }
}
