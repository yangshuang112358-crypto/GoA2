#nullable enable
using Goa2.Domain;
using UnityEngine.UIElements;

namespace Goa2.Presentation
{
    public sealed partial class GameScreen
    {
        private void RenderBattlefieldChoice(VisualElement parent, GameView view)
        {
            var choice = view.Pending;
            if (choice == null) { parent.Add(Text("等待结算。", "body")); return; }
            if (choice.Kind == "spawn_order_unresolved")
            {
                parent.Add(Text("多个出生点争用相邻空格。出生先后规则待确认，当前进度已保留。", "body"));
                parent.Add(Text("可保存此局面；测试中也可传送占位英雄后继续。", "tiny")); return;
            }
            if (choice.Kind != "minion_spawn") { parent.Add(Text("等待" + PlayerName(choice.ChooserSeat) + "完成选择。", "body")); return; }
            string unit = view.PendingSpawn == null ? "小兵" : (view.PendingSpawn.Team == Team.Blue ? "蓝方" : "红方") +
                (view.PendingSpawn.Kind == "heavy" ? "重型" : view.PendingSpawn.Kind == "ranged" ? "远程" : "近战") + "小兵";
            parent.Add(Text(unit + "的出生点被占据。", "section-title"));
            parent.Add(Text(PlayerName(choice.ChooserSeat) + "选择相邻空格。", "body"));
            if (choice.CandidateCells.Count == 0)
                parent.Add(Text("目前没有合法空格，已保留待出生任务。满占位规则待裁定；测试中可以传送占位单位。", "body"));
            else if (choice.ChooserSeat == seat)
            {
                if (chosenCell.HasValue) Confirm(parent, "确认小兵出生于 " + chosenCell.Value, () => Submit(CommandKind.ChooseMinionSpawn, destination: chosenCell!.Value));
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
