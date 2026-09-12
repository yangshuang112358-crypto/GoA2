#nullable enable
using System.Linq;
using Goa2.Domain;
using UnityEngine.UIElements;

namespace Goa2.Presentation
{
    public sealed partial class GameScreen
    {
        private string defenseCardId = "";
        private string discardCardId = "";
        private bool declineDefensePending;
        private bool declineRetaliationPending;
        private static string DefenseRestrictionText(string reason) => reason switch
        {
            "unblockable" => "此攻击不可抵挡",
            "requires_ranged" => "只可抵挡远程攻击",
            "requires_non_ranged" => "只可抵挡非远程攻击",
            "requires_minimum_distance" => "攻击者距离太近",
            "requires_adjacent_friendly_minion" => "没有相邻友方小兵",
            _ => "未满足牌面防御条件"
        };
        private static string AttackFormula(AttackBreakdown attack) => "攻击 " + attack.BaseAttack + " + 加成 " + (attack.AttackBonus-attack.CardTextBonus) +
            (attack.CardTextBonus==0 ? "" : " + 牌文 " + attack.CardTextBonus) + " + 敌兵 " + attack.EnemySupport + " − 友兵 " + attack.FriendlyGuard + " = " + attack.FinalAttack;
        private void RenderAttackSources(VisualElement parent,GameView view,AttackBreakdown attack)
        {
            string summary="";
            if (attack.CardTextReason=="source_adjacent_enemies")
                summary="攻击者相邻敌方："+attack.CardTextSourceUnits.Count+"个"+
                    (attack.CardTextSourceUnits.Count==0 ? "，牌文 +0" : " × "+attack.CardTextBonus/attack.CardTextSourceUnits.Count+" = +"+attack.CardTextBonus);
            else if (attack.CardTextReason=="target_adjacent_other_allies")
                summary="目标相邻的其他友方："+attack.CardTextSourceUnits.Count+"个，牌文 +"+attack.CardTextBonus;
            if (summary=="") return;
            var label=Text(summary,"body"); label.name="attack-card-text-sources"; parent.Add(label);
            foreach(string id in attack.CardTextSourceUnits)
            {
                var unit=view.Units.SingleOrDefault(u => u.Id==id);
                if(unit!=null) parent.Add(Text("· "+(unit.Seat.HasValue ? PlayerName(unit.Seat.Value) : MinionName(unit))+"（"+unit.Position+"）","muted"));
            }
        }
        private bool RenderCombatChoice(VisualElement parent, GameView view)
        {
            var choice = view.Pending;
            if (choice == null) return false;
            if(choice.Kind=="effect_minion")
            {
                var heading=Text(PlayerName(choice.ChooserSeat)+"可额外移除一个小兵；这次移除不获得金币。","body");heading.name="effect-minion-choice";parent.Add(heading);
                if(choice.ChooserSeat==seat)
                {
                    var target=chosenCell.HasValue ? view.Units.SingleOrDefault(u=>u.Position==chosenCell.Value && view.EffectTargets.Contains(u.Id)) : null;
                    if(target!=null) Confirm(parent,"确认移除 "+MinionName(target),()=>Submit(CommandKind.ChooseEffectTarget,target.Id));
                    else parent.Add(Text("点击高亮的敌方小兵后确认。","body"));
                    parent.Add(Button("不移除，结束本牌",()=>Submit(CommandKind.ChooseEffectTarget,"skip"),"quiet-button","effect-minion-skip"));
                }
                else parent.Add(Text("等待行动英雄选择小兵或跳过。","body"));
                RenderCardDetail(parent,catalog.Card(choice.Source));return true;
            }
            if(choice.Kind=="effect_target")
            {
                parent.Add(Text(PlayerName(choice.ChooserSeat)+"选择牌文作用的英雄。","section-title"));
                if(choice.ChooserSeat!=seat) {parent.Add(Text("等待来源英雄选择目标。","body"));RenderCardDetail(parent,catalog.Card(choice.Source));return true;}
                var target=chosenCell.HasValue ? view.Units.SingleOrDefault(u=>u.Position==chosenCell.Value && view.EffectTargets.Contains(u.Id)) : null;
                if(target!=null) Confirm(parent,"确认选择 "+PlayerName(target.Seat!.Value),()=>Submit(CommandKind.ChooseEffectTarget,target.Id));
                else parent.Add(Text("点击高亮英雄后确认；后续取回由受益英雄本人选择。","body"));
                RenderCardDetail(parent,catalog.Card(choice.Source));
                return true;
            }
            if (choice.Kind == "recover_discard")
            {
                parent.Add(Text(PlayerName(choice.ChooserSeat)+"选择取回一张卡牌。","section-title"));
                if(choice.ChooserSeat!=seat) { parent.Add(Text("等待对应角色选择取回或跳过。","body"));RenderCardDetail(parent,catalog.Card(choice.Source));return true; }
                foreach(string id in view.RecoverableCards)
                {
                    string selected=id;
                    string zone=view.OwnCards.Single(c=>c.CardId==id).Zone==CardZone.PlayedResolved ? "已结算" : "已丢弃";
                    var button=Button(catalog.Card(id).Name+" · "+zone,()=> { discardCardId=selected;Render(); },"choice-button","recover-card-"+catalog.Card(id).Color);
                    if(discardCardId==id) button.AddToClassList("chosen");parent.Add(button);
                }
                if(view.RecoverableCards.Contains(discardCardId))
                {
                    Confirm(parent,"确认取回 "+catalog.Card(discardCardId).Name,()=>Submit(CommandKind.ChooseRecoveredCard,discardCardId));
                    if(view.EngineVersion>=16 && view.Effects.Any(e=>e.ControllerSeat==seat && e.SourceCardId==discardCardId))
                        parent.Add(Text("取回此牌会取消它当前或待生效的持续效果。","body"));
                }
                parent.Add(Button("不取回，继续结算",()=>Submit(CommandKind.ChooseRecoveredCard,"skip"),"quiet-button","recover-card-skip"));
                RenderCardDetail(parent,catalog.Card(view.RecoverableCards.Contains(discardCardId)?discardCardId:choice.Source));
                return true;
            }
            if(choice.Kind=="card_swap")
            {
                var instruction=Text(PlayerName(choice.ChooserSeat)+"可以选择一张手牌交换；换回本次防御牌，所选手牌进入弃牌区。","body");instruction.name="card-swap-choice";parent.Add(instruction);
                if(choice.ChooserSeat!=seat){parent.Add(Text("等待防御者选择交换或跳过。","body"));return true;}
                foreach(string id in view.CardSwapOptions)
                {
                    string selected=id;var button=Button(catalog.Card(id).Name,()=>{discardCardId=selected;Render();},"choice-button","card-swap-"+catalog.Card(id).Color);
                    if(discardCardId==id)button.AddToClassList("chosen");parent.Add(button);
                }
                if(view.CardSwapOptions.Contains(discardCardId))Confirm(parent,"确认交换 "+catalog.Card(discardCardId).Name,()=>Submit(CommandKind.ChooseCardSwap,discardCardId));
                parent.Add(Button("不交换，继续结算",()=>Submit(CommandKind.ChooseCardSwap,"skip"),"quiet-button","card-swap-skip"));
                if(choice.Source!="")RenderCardDetail(parent,catalog.Card(choice.Source));return true;
            }
            if (choice.Kind == "effect_move")
            {
                parent.Add(Text(PlayerName(choice.ChooserSeat)+"选择牌文移动。","section-title"));
                if(choice.ChooserSeat!=seat) { parent.Add(Text("等待对应角色选择牌文移动。","body"));return true; }
                bool beforeAttack=choice.ResumeAt=="before_attack";
                bool defenseMove=choice.ResumeAt=="defense_response_move";
                bool charge=choice.ResumeAt=="charge_before_attack";
                var instruction=Text((charge ? "必须沿直线移动2格，终点须邻接合法敌方目标。完成移动后选择攻击目标。" : defenseMove ? "攻击及后续已结算。从当前位置沿直线移动2格，或不移动。" : beforeAttack ? "攻击前移动。移动或跳过后再选择攻击目标。" : "攻击已结算。")+"点击高亮格后确认。","body");instruction.name="effect-move-choice";parent.Add(instruction);
                if(chosenCell.HasValue && view.EffectMoves.Any(m=>m.Destination==chosenCell.Value))
                    Confirm(parent,"确认牌文移动至 "+chosenCell.Value,()=>Submit(CommandKind.ChooseEffectMove,destination:chosenCell!.Value));
                if(choice.Optional)parent.Add(Button(beforeAttack ? "不移动，继续攻击" : "不移动，继续结算",()=>Submit(CommandKind.ChooseEffectMove,"skip"),"quiet-button","effect-move-skip"));
                if(choice.Source!="")RenderCardDetail(parent,catalog.Card(choice.Source));
                return true;
            }
            if (choice.Kind == "optional_discard")
            {
                parent.Add(Text(PlayerName(choice.ChooserSeat) + "选择是否在攻击前弃置一张手牌。", "section-title"));
                RenderCardDetail(parent,catalog.Card(choice.Source));
                if (choice.ChooserSeat != seat) { parent.Add(Text("切换至对应角色选择。", "body")); return true; }
                parent.Add(Text("弃牌后再选择攻击目标。也可直接继续攻击。", "body"));
                foreach (string id in view.OptionalDiscardCards)
                {
                    string selected=id;
                    var button=Button(catalog.Card(id).Name,() => { discardCardId=selected; Render(); },"choice-button","optional-discard-"+catalog.Card(id).Color);
                    if (discardCardId==id) button.AddToClassList("chosen"); parent.Add(button);
                }
                if (view.OptionalDiscardCards.Contains(discardCardId))
                {
                    RenderCardDetail(parent,catalog.Card(discardCardId));
                    Confirm(parent,"确认弃置 "+catalog.Card(discardCardId).Name,() => Submit(CommandKind.ChooseOptionalDiscard,discardCardId));
                }
                parent.Add(Button("不弃牌，继续攻击",() => Submit(CommandKind.ChooseOptionalDiscard,"skip"),"quiet-button","optional-discard-skip"));
                return true;
            }
            if (choice.Kind == "forced_discard")
            {
                parent.Add(Text(PlayerName(choice.ChooserSeat) + "处理反制选择。", "section-title"));
                parent.Add(Text("本次攻击已结算，完成反制后继续下一次行动。", "body"));
                if (choice.Source!="") RenderCardDetail(parent,catalog.Card(choice.Source));
                if (choice.ChooserSeat != seat) { parent.Add(Text("切换至对应角色选择弃牌。", "body")); return true; }
                parent.Add(Text(view.CanDeclineRetaliationDiscard ? "选择一张手牌弃置，或不弃牌、直接被击败。" : "点击下方手牌或以下选项，再确认弃置。此选择不能跳过。", "body"));
                foreach (string id in view.ForcedDiscardCards)
                {
                    string selected=id;
                    var button=Button(catalog.Card(id).Name,() => { discardCardId=selected;declineRetaliationPending=false;Render(); },"choice-button","forced-discard-"+catalog.Card(id).Color);
                    if (discardCardId==id) button.AddToClassList("chosen"); parent.Add(button);
                }
                if(view.CanDeclineRetaliationDiscard)
                {
                    var defeat=Button("不弃牌，直接被击败",()=> { discardCardId="";declineRetaliationPending=true;Render(); },"choice-button","retaliation-decline");
                    if(declineRetaliationPending) defeat.AddToClassList("chosen");parent.Add(defeat);
                }
                if(declineRetaliationPending && view.CanDeclineRetaliationDiscard)
                    Confirm(parent,"确认不弃牌并被击败",()=>Submit(CommandKind.DeclineRetaliationDiscard));
                else if (view.ForcedDiscardCards.Contains(discardCardId))
                {
                    RenderCardDetail(parent,catalog.Card(discardCardId));
                    Confirm(parent,"确认弃置 "+catalog.Card(discardCardId).Name,() => Submit(CommandKind.ForcedDiscard,discardCardId));
                }
                return true;
            }
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
                var heading=Text(PlayerName(choice.ChooserSeat)+(choice.Optional ? "已击败英雄，可再次攻击或停止。" : "选择攻击目标。"),"body");
                if(choice.Optional) heading.name="attack-repeat-choice";parent.Add(heading);
                if (view.AttackRange.HasValue)
                {
                    var range=Text("本次攻击距离 " + view.AttackRange.Value,"body"); range.name="attack-range"; parent.Add(range);
                }
                if (choice.ChooserSeat != seat) {RenderCardDetail(parent,catalog.Card(choice.Source));return true;}
                var target = chosenCell.HasValue ? view.Units.SingleOrDefault(u => u.Position == chosenCell.Value && view.AttackTargets.Contains(u.Id)) : null;
                if (target != null) Confirm(parent, "确认攻击 " + (target.Seat.HasValue ? PlayerName(target.Seat.Value) : MinionName(target)), () => Submit(CommandKind.ChooseAttackTarget, target.Id));
                else parent.Add(Text("点击高亮敌方单位后确认。", "body"));
                if(choice.Optional) parent.Add(Button("不再重复，结束本牌",()=>Submit(CommandKind.ChooseAttackTarget,"skip"),"quiet-button","attack-repeat-skip"));
                RenderCardDetail(parent,catalog.Card(choice.Source));
                return true;
            }
            if (choice.Kind != "defense") return false;
            parent.Add(Text(PlayerName(choice.ChooserSeat) + "响应攻击。", "section-title"));
            if (view.Attack != null)
            {
                parent.Add(Text(AttackFormula(view.Attack), "body"));
                RenderAttackSources(parent,view,view.Attack);
                parent.Add(Text(view.Attack.Ranged ? "本次是远程攻击" : "本次是非远程攻击", "muted"));
                if(view.Attack.Unblockable)
                {
                    var label=Text("本次攻击不可抵挡；仍可用数值防御。","body");label.name="attack-unblockable";parent.Add(label);
                }
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
            foreach(var restriction in view.DefenseRestrictions)
            {
                var card=catalog.Card(restriction.Key);
                var label=Text(card.Name+"："+DefenseRestrictionText(restriction.Value),"muted"); label.name="defense-restriction-"+card.Color; parent.Add(label);
            }
            if (defenseCardId != "" && view.DefenseOptions.Any(o => o.CardId == defenseCardId))
            {
                RenderCardDetail(parent,catalog.Card(defenseCardId));
                Confirm(parent, "确认使用 " + catalog.Card(defenseCardId).Name + " 防御", () => Submit(CommandKind.Defend, defenseCardId));
            }
            else if (declineDefensePending) Confirm(parent, "确认不防御并被击败", () => Submit(CommandKind.DeclineDefense));
            else parent.Add(Button("不防御", () => { defenseCardId = ""; declineDefensePending = true; Render(); }, "quiet-button", "decline-defense"));
            return true;
        }
    }
}
