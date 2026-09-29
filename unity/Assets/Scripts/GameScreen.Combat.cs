#nullable enable
using System.Linq;
using Goa2.Domain;
using UnityEngine.UIElements;

namespace Goa2.Presentation
{
    public sealed partial class GameScreen
    {
        private string primaryOptionChoice="protect";
        private string defenseCardId = "";
        private string discardCardId = "";
        private int goldTransferTarget = -1, goldTransferAmount = -1;
        private bool declineDefensePending;
        private bool declineRetaliationPending;
        private string returnUnitId="";
        private void PrepareReturnSelection(GameView view)
        {if(!view.MinionReturns.Any(o=>o.UnitId==returnUnitId))returnUnitId=view.MinionReturns.FirstOrDefault()?.UnitId??"";}
        private static string DefenseRestrictionText(string reason) => reason switch
        {
            "unblockable" => "此攻击不可抵挡",
            "requires_ranged" => "只可抵挡远程攻击",
            "requires_non_ranged" => "只可抵挡非远程攻击",
            "requires_minimum_distance" => "攻击者距离太近",
            "requires_adjacent_friendly_minion" => "没有相邻友方小兵",
            _ => "未满足牌面防御条件"
        };
        private static string AttackFormula(AttackBreakdown attack) => "攻击 " + attack.BaseAttack + " + 加成 " + (attack.AttackBonus-attack.CardTextBonus-attack.UltimateBonus) +
            (attack.UltimateBonus==0 ? "" : " + 紫卡 " + attack.UltimateBonus) +
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
            if(choice.Kind=="minion_return")
            {
                parent.Add(Text(PlayerName(choice.ChooserSeat)+"安排小兵回归战区。","section-title"));
                if(choice.ChooserSeat!=seat){parent.Add(Text("等待该队队长选择小兵回归。","body"));return true;}
                PrepareReturnSelection(view);
                foreach(var id in view.MinionReturns.Select(o=>o.UnitId).Distinct())
                {
                    string selected=id;var unit=view.Units.Single(u=>u.Id==id);
                    var button=Button(MinionName(unit)+" · "+unit.Position,()=>{returnUnitId=selected;chosenCell=null;Render();},"choice-button");
                    if(id==returnUnitId)button.AddToClassList("chosen");parent.Add(button);
                }
                bool place=view.MinionReturns.Any(o=>o.UnitId==returnUnitId && o.Place);
                parent.Add(Text(place?"没有可行回归路线：选择战区内最近的空格放置。":"选择最短回归路线的下一格；到达战区前由同一队长继续选择。","body"));
                if(chosenCell.HasValue && view.MinionReturns.Any(o=>o.UnitId==returnUnitId && o.Destination==chosenCell.Value))
                    Confirm(parent,(place?"确认就近放置到 ":"确认回归一步到 ")+chosenCell.Value,()=>Submit(CommandKind.ChooseMinionReturn,returnUnitId,destination:chosenCell!.Value));
                return true;
            }
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
            if(choice.Kind=="gold_transfer")
            {
                var title=Text(PlayerName(choice.ChooserSeat)+"选择拿取金币。","section-title");title.name="gold-transfer-choice";parent.Add(title);
                if(choice.ChooserSeat!=seat){parent.Add(Text("等待来源英雄选择金币。","body"));return true;}
                foreach(var option in view.GoldTransfers)
                {
                    int targetSeat=option.TargetSeat,amount=option.Amount;
                    var button=Button("从"+PlayerName(targetSeat)+"拿取"+amount+"枚金币",()=>{goldTransferTarget=targetSeat;goldTransferAmount=amount;Render();},"choice-button","gold-transfer-p"+(targetSeat+1)+"-"+amount);
                    if(goldTransferTarget==targetSeat && goldTransferAmount==amount)button.AddToClassList("chosen");parent.Add(button);
                }
                var skip=Button("不拿取金币，继续后续移动",()=>{goldTransferTarget=-1;goldTransferAmount=0;Render();},"choice-button","gold-transfer-skip");
                if(goldTransferAmount==0)skip.AddToClassList("chosen");parent.Add(skip);
                if(goldTransferAmount==0 || view.GoldTransfers.Any(o=>o.TargetSeat==goldTransferTarget && o.Amount==goldTransferAmount))
                    Confirm(parent,goldTransferAmount==0 ? "确认不拿取金币" : "确认从"+PlayerName(goldTransferTarget)+"拿取"+goldTransferAmount+"枚金币",()=>Submit(CommandKind.ChooseGoldTransfer,goldTransferAmount.ToString(System.Globalization.CultureInfo.InvariantCulture),target:goldTransferTarget));
                RenderCardDetail(parent,catalog.Card(choice.Source));return true;
            }
            if(choice.Kind=="primary_option" && choice.ResumeAt=="ultimate_attack_repeat")
            {
                parent.Add(Text(PlayerName(choice.ChooserSeat)+"可再次执行基础攻击，目标必须不同；首次与重复合计最多两次。","section-title"));
                if(choice.ChooserSeat==seat)
                {
                    Confirm(parent,"重复一次基础攻击",()=>Submit(CommandKind.ChoosePrimaryOption,"repeat"));
                    parent.Add(Button("不重复，结束此牌",()=>Submit(CommandKind.ChoosePrimaryOption,"finish"),"quiet-button","ultimate-repeat-finish"));
                }
                else parent.Add(Text("等待行动者决定是否重复。","body"));
                RenderCardDetail(parent,catalog.Card(choice.Source));return true;
            }
            if(choice.Kind=="primary_option" && choice.ResumeAt=="action_minion_battle_offer")
            {
                parent.Add(Text(PlayerName(choice.ChooserSeat)+"的基础技能已执行，可在当前交战区发动一场小兵战斗。","section-title"));
                if(choice.ChooserSeat==seat)
                {
                    Confirm(parent,"发动小兵战斗",()=>Submit(CommandKind.ChoosePrimaryOption,"battle"));
                    parent.Add(Button("不发动，完成原行动",()=>Submit(CommandKind.ChoosePrimaryOption,"skip"),"quiet-button","action-battle-skip"));
                }
                else parent.Add(Text("等待阿连决定是否发动。","body"));
                RenderCardDetail(parent,catalog.Card(choice.Source));return true;
            }
            if(choice.Kind=="primary_option")
            {
                parent.Add(Text(PlayerName(choice.ChooserSeat)+"选择一项卡牌效果。","section-title"));
                if(choice.ChooserSeat!=seat){parent.Add(Text("等待来源英雄选择。","body"));RenderCardDetail(parent,catalog.Card(choice.Source));return true;}
                foreach(string option in view.PrimaryOptions)
                {
                    string selected=option;
                    parent.Add(Button(option=="protect"?"本轮保护本人和范围内友方单位":"取回此牌（弃牌堆为空）",()=>{primaryOptionChoice=selected;Render();},"quiet-button","primary-option-"+option));
                }
                if(!view.PrimaryOptions.Contains("recover"))parent.Add(Text("弃牌堆非空，本次不能取回此牌。","body"));
                if(view.PrimaryOptions.Contains(primaryOptionChoice))Confirm(parent,primaryOptionChoice=="protect"?"确认本轮位移保护":"确认取回此牌",()=>Submit(CommandKind.ChoosePrimaryOption,primaryOptionChoice));
                RenderCardDetail(parent,catalog.Card(choice.Source));return true;
            }
            if(choice.Kind=="effect_target")
            {
                bool orbital=choice.ResumeAt=="orbital_target" || choice.ResumeAt=="orbital_repeat";
                bool unitPlacement=choice.ResumeAt=="unit_placement_target" || choice.ResumeAt=="unit_placement_repeat";
                bool singlePush=choice.ResumeAt=="single_push_target";
                bool otherMove=choice.ResumeAt=="before_attack_other_move";
                bool approach=choice.ResumeAt=="approach_target" || choice.ResumeAt=="approach_repeat",approachRepeat=choice.ResumeAt=="approach_repeat";
                if(choice.ResumeAt=="before_action_target")parent.Add(Text("行动尚未执行。选择相邻且非免疫的敌方英雄；可以选择空手者跳过弃牌，有牌者由本人弃牌，然后继续原行动。","body"));
                if(choice.ResumeAt=="primary_completion_target")parent.Add(Text("基础技能已执行。紫卡可选择场上任意合法敌方英雄；可选空手者，有牌者由本人弃牌。","body"));
                bool groupPush=choice.ResumeAt=="push_all_adjacent",blockedPush=choice.ResumeAt=="blocked_push_discard_target";
                bool unitSwap=choice.ResumeAt=="unit_swap";
                bool minionRepeat=choice.ResumeAt=="friendly_minion_repeat";
                bool minionMove=choice.ResumeAt=="friendly_minion_move" || minionRepeat;
                var targetTitle=Text(PlayerName(choice.ChooserSeat)+(orbital ? (choice.Optional?"可再选择一个非相邻单位等距移动，也可不重复。":"选择范围内其他非相邻单位；移动后须保持与你的距离。") : unitPlacement ? "选择攻击距离内不在同一直线的合法单位。" : singlePush ? "选择本牌允许推动的相邻敌方单位。" : approach ? (approachRepeat?"可重复一次技能，重新选择最近的非相邻敌方单位。":"选择攻击距离内最近的非相邻敌方单位。") : groupPush ? "选择下一个要推动的敌方单位；全部推动后再处理弃牌。" : blockedPush ? "选择下一名受阻英雄，由该英雄本人弃牌。" : unitSwap ? (choice.Optional?"可与相邻友方小兵换位，也可不换位并继续此牌。":"选择本牌允许换位的单位。") : minionRepeat ? "可再选择一个合法友方小兵移动一次，也可不重复。" : minionMove ? "选择技能范围内的一个友方小兵。" : otherMove ? "选择原攻击目标旁的另一个单位移动，或跳过。" : choice.Optional ? "选择另一名敌方英雄，或跳过。" : "选择牌文作用的英雄。"),"section-title");targetTitle.name="effect-target-choice";parent.Add(targetTitle);
                if(orbital || singlePush || unitPlacement)parent.Add(Text("当前六英雄没有地图标志物，仅显示本牌的单位分支。","body"));
                if(choice.ChooserSeat!=seat) {parent.Add(Text("等待来源英雄选择目标。","body"));RenderCardDetail(parent,catalog.Card(choice.Source));return true;}
                var target=chosenCell.HasValue ? view.Units.SingleOrDefault(u=>u.Position==chosenCell.Value && view.EffectTargets.Contains(u.Id)) : null;
                if(target!=null) Confirm(parent,"确认选择 "+(target.Seat.HasValue?PlayerName(target.Seat.Value):MinionName(target)),()=>Submit(CommandKind.ChooseEffectTarget,target.Id));
                else parent.Add(Text(orbital?"点击高亮单位后确认；可选择0步，或移动1格且保持与来源英雄距离相等。":unitPlacement?"可选择本牌允许的友方或敌方单位；确认后，选择来源英雄相邻的空格放置。":singlePush?"点击高亮单位后确认；距离按牌文处理，通行能力和阻挡由规则判断。":approach?"点击高亮单位，再选择沿最短有效路径向你接近的落点。":groupPush?"点击高亮敌方单位后确认推动；已处理目标不会再次推动。":blockedPush?"点击受阻英雄后确认，再切至该英雄选弃牌。":unitSwap?"点击高亮单位后确认换位；换位不算移动，小兵离开战区后仍需回归。":minionMove?"点击高亮小兵后确认；再由你选择移动落点，也可不移动。":otherMove?"点击高亮单位后确认，再由你选择该单位的一格落点。":"点击高亮英雄后确认；后续选牌由目标英雄本人决定。","body"));
                if(choice.Optional)parent.Add(Button(orbital?"不重复，结束此牌":unitPlacement?"不重复，结束此牌":singlePush?"不推动，继续此牌":approachRepeat?"不重复，结束此牌":unitSwap?"不换位，继续此牌":minionRepeat?"不重复，结束此牌":otherMove?"不移动，继续原攻击":"跳过额外弃牌，继续原攻击",()=>Submit(CommandKind.ChooseEffectTarget,"skip"),"quiet-button","effect-target-skip"));
                RenderCardDetail(parent,catalog.Card(choice.Source));
                return true;
            }
            if(WheelRecovery(view))
            {
                parent.Add(Text(choice.Kind=="discard_attack" ? "从英雄圆环选择弃牌堆攻击牌反击；牌仍留在弃牌堆。" : choice.Kind=="recover_discard" ? "从英雄圆环选择取回卡牌；取回来源牌会取消对应持续效果。" : "从英雄圆环选择交换手牌，或选择不交换。","body"));
                return true;
            }
            if(choice.Kind=="placement")
            {
                var heading=Text(PlayerName(choice.ChooserSeat)+"选择放置落点。","section-title");heading.name="placement-choice";parent.Add(heading);
                if(choice.ResumeAt=="unit_placement")
                {var placed=view.Units.SingleOrDefault(u=>u.Id==choice.UnitId);if(placed!=null)parent.Add(Text("放置对象："+(placed.Seat.HasValue?PlayerName(placed.Seat.Value):MinionName(placed))+"。选择来源英雄相邻的空格。","body"));}
                if(choice.ChooserSeat!=seat){parent.Add(Text("等待行动英雄选择落点。","body"));return true;}
                parent.Add(Text("点击本牌允许的高亮空格后确认。放置无需移动路径，具体落点限制见卡牌描述。","body"));
                if(chosenCell.HasValue && view.Placements.Contains(chosenCell.Value))Confirm(parent,"确认放置到 "+chosenCell.Value,()=>Submit(CommandKind.ChoosePlacement,destination:chosenCell!.Value));
                RenderCardDetail(parent,catalog.Card(choice.Source));return true;
            }
            if (choice.Kind == "effect_move")
            {
                bool singlePushMove=choice.ResumeAt=="single_push_distance";
                parent.Add(Text(PlayerName(choice.ChooserSeat)+(singlePushMove?"选择推动落点。":"选择牌文移动。"),"section-title"));
                if(choice.ChooserSeat!=seat) { parent.Add(Text("等待对应角色选择牌文移动。","body"));return true; }
                bool beforeAttack=choice.ResumeAt=="before_attack";
                bool defenseMove=choice.ResumeAt=="defense_response_move";
                bool charge=choice.ResumeAt=="charge_before_attack";
                bool through=choice.ResumeAt=="strike_through_enemy";
                bool primaryMove=choice.ResumeAt=="primary_movement";
                bool requiredStraight=choice.ResumeAt=="required_straight_if_able";
                if(choice.ResumeAt=="other_unit_before_attack" || choice.ResumeAt=="target_unit_move" || choice.ResumeAt=="approach_move" || singlePushMove)
                {
                    var moved=view.Units.SingleOrDefault(u=>u.Id==choice.UnitId);
                    if(moved!=null)parent.Add(Text((singlePushMove?"推动对象：":"移动对象：")+(moved.Seat.HasValue?PlayerName(moved.Seat.Value):MinionName(moved))+(singlePushMove?"。沿直线选择高亮落点，也可不推动。":choice.ResumeAt=="approach_move"?"。向施法者接近最多2格，允许不移动。":choice.ResumeAt=="target_unit_move"?"。按卡牌距离选择普通移动落点，允许不移动；小兵离开战区后由队长选择回归。":"。移动一格后继续原目标的攻击。"),"body"));
                }
                var instruction=Text((singlePushMove ? "推动沿来源到目标的直线远离来源，距离按本牌；通常受墙和单位阻挡；通行能力由规则判断，终点必须为空。离区小兵在行动结束后先回归。" : choice.ResumeAt=="approach_move" ? "沿最短有效路径接近最多2格；可提前停下或不移动，小兵离区后先回归。" : choice.ResumeAt=="before_action_move" ? "先执行紫卡移动，最多2格，也可不移动。之后按新位置执行原行动；若瞬闪打击无穿敌路线，已移动的位置保留。" : choice.ResumeAt=="before_action_movement_destination" ? "紫卡前置移动已完成。现在从新位置选择原行动的合法落点，也可不再移动并结束此牌。" : primaryMove ? "主要移动使用卡面移动数值加被动。完成移动或留在原地后，执行本牌后续效果。" : requiredStraight ? "必须沿直线完整移动2格。没有合法路线时自动继续；当前有路线，不能跳过。" : through ? "沿直线穿过一个敌方单位移动2格。确认落点后，自动攻击途中那个敌人。" : charge ? "必须按卡牌指定距离沿直线移动，终点须邻接合法敌方目标。完成移动后选择攻击目标。" : defenseMove ? "攻击及后续已结算。从当前位置沿直线移动2格，或不移动。" : beforeAttack ? "攻击前移动。移动或跳过后再选择攻击目标。" : "完成本次牌文移动后继续卡牌效果。")+"点击高亮格后确认。","body");instruction.name="effect-move-choice";parent.Add(instruction);
                if(chosenCell.HasValue && view.EffectMoves.Any(m=>m.Destination==chosenCell.Value))
                    Confirm(parent,(singlePushMove?"确认推动至 ":"确认牌文移动至 ")+chosenCell.Value,()=>Submit(CommandKind.ChooseEffectMove,destination:chosenCell!.Value));
                if(choice.Optional)parent.Add(Button(singlePushMove ? "不推动，继续结算" : primaryMove ? "留在原地，执行后续效果" : beforeAttack ? "不移动，继续攻击" : "不移动，继续结算",()=>Submit(CommandKind.ChooseEffectMove,"skip"),"quiet-button","effect-move-skip"));
                if(choice.Source!="")RenderCardDetail(parent,catalog.Card(choice.Source));
                return true;
            }
            if (WheelDiscard(view)) {
                parent.Add(Text("弃牌操作已迁至该英雄的战场圆环。选择图标，再点击中央确认。", "body"));
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
                var heading=Text(PlayerName(choice.ChooserSeat)+(choice.Optional ? (choice.ResumeAt=="repeat_once_different_full" ? "可对不同目标重复一次完整攻击，也可停止。" : choice.ResumeAt=="repeat_once_different" ? "与敌方英雄相邻，可换一个目标再攻击一次，或停止。" : "已击败英雄，可再次攻击或停止。") : "选择攻击目标。"),"body");
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
            parent.Add(Text(choice.ChooserSeat==seat ? "在英雄技能环选择防御牌，或选择不防御，再确认。" : "等待响应方选择防御。","body"));
            if(choice.ChooserSeat==seat)foreach(var option in view.DefenseOptions) {
                var label=Text(catalog.Card(option.CardId).Name+" · "+(option.Block ? "抵挡" : "防御 "+option.Assessment.FinalDefense+" / 攻击 "+option.Assessment.AttackCompared),"muted");
                if(CardDisplay.WarnDefense(catalog.Card(option.CardId),option.Assessment))label.style.color=new UnityEngine.Color(1,.6f,.7f);parent.Add(label);
            }
            foreach(var restriction in view.DefenseRestrictions)parent.Add(Text(catalog.Card(restriction.Key).Name+"："+DefenseRestrictionText(restriction.Value),"muted"));
            return true;
        }
    }
}
