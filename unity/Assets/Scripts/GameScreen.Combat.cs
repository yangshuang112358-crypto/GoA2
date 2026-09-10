#nullable enable
using System.Linq;
using Goa2.Domain;
using UnityEngine.UIElements;

namespace Goa2.Presentation
{
    public sealed partial class GameScreen
    {
        private string defenseCardId = "";
        private bool declineDefensePending;
        private static string AttackFormula(AttackBreakdown attack) => "攻击 " + attack.BaseAttack + " + 加成 " + attack.AttackBonus + " + 敌兵 " + attack.EnemySupport + " − 友兵 " + attack.FriendlyGuard + " = " + attack.FinalAttack;
        private bool RenderCombatChoice(VisualElement parent, GameView view)
        {
            var choice = view.Pending;
            if (choice == null) return false;
            if (choice.Kind == "hero_respawn")
            {
                parent.Add(Text(PlayerName(choice.ChooserSeat) + "先复活，再执行已出牌。", "body"));
                if (choice.ChooserSeat != seat) return true;
                if (view.RespawnCells.Count == 0) parent.Add(Text("本队出生点全部被占，已保留复活任务。满占位规则待确认；测试时可传送占位者。", "body"));
                else if (chosenCell.HasValue) Confirm(parent, "确认复活于 " + chosenCell.Value, () => Submit(CommandKind.RespawnHero, destination: chosenCell!.Value));
                else parent.Add(Text("选择地图高亮出生点。复活不会再扣除水晶。", "body"));
                return true;
            }
            if (choice.Kind == "attack_target")
            {
                parent.Add(Text(PlayerName(choice.ChooserSeat) + "选择攻击目标。", "body"));
                RenderCardDetail(parent, catalog.Card(choice.Source));
                if (choice.ChooserSeat != seat) return true;
                var target = chosenCell.HasValue ? view.Units.SingleOrDefault(u => u.Position == chosenCell.Value && view.AttackTargets.Contains(u.Id)) : null;
                if (target != null) Confirm(parent, "确认攻击 " + (target.Seat.HasValue ? PlayerName(target.Seat.Value) : MinionName(target)), () => Submit(CommandKind.ChooseAttackTarget, target.Id));
                else parent.Add(Text("点击高亮敌方单位后确认。", "body"));
                return true;
            }
            if (choice.Kind != "defense") return false;
            parent.Add(Text(PlayerName(choice.ChooserSeat) + "响应攻击。", "section-title"));
            if (view.Attack != null)
            {
                parent.Add(Text(AttackFormula(view.Attack), "body"));
                parent.Add(Text(view.Attack.Ranged ? "本次是远程攻击" : "本次是非远程攻击", "muted"));
            }
            if (choice.ChooserSeat != seat) { parent.Add(Text("切换至对应角色选择防御。", "body")); return true; }
            parent.Add(Text("使用手牌防御；也可选择不防御并被击败。", "body"));
            foreach (var option in view.DefenseOptions)
            {
                string id = option.CardId;
                string result = option.Block ? "抵挡" : "防御 " + option.Assessment.FinalDefense + (option.Assessment.Successful ? " ≥ " : " < ") + option.Assessment.AttackCompared;
                if (option.IgnoresMinions) result += " · 忽略小兵修正";
                var button = Button(catalog.Card(id).Name + " · " + result, () => { defenseCardId = id; declineDefensePending = false; Render(); }, "choice-button");
                if (id == defenseCardId) button.AddToClassList("chosen"); parent.Add(button);
            }
            foreach (string id in view.UnimplementedDefenseCards) parent.Add(Text(catalog.Card(id).Name + "：响应文字待实装，已保留等待。", "tiny"));
            if (defenseCardId != "" && view.DefenseOptions.Any(o => o.CardId == defenseCardId))
                Confirm(parent, "确认使用 " + catalog.Card(defenseCardId).Name + " 防御", () => Submit(CommandKind.Defend, defenseCardId));
            else if (declineDefensePending) Confirm(parent, "确认不防御并被击败", () => Submit(CommandKind.DeclineDefense));
            else parent.Add(Button("不防御", () => { defenseCardId = ""; declineDefensePending = true; Render(); }, "quiet-button", "decline-defense"));
            return true;
        }
    }
}
