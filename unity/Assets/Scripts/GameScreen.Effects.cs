#nullable enable
using System.Collections.Generic;
using System.Linq;
using Goa2.Domain;
using UnityEngine.UIElements;

namespace Goa2.Presentation
{
    public sealed partial class GameScreen
    {
        private string effectAreaId="";
        private string EffectName(ActiveEffect effect) => effect.SourceCardId!="" ? catalog.Card(effect.SourceCardId).Name : effect.Kind==EffectKind.OtherEnemyActionImmunity ? "其他敌方行动免疫" : "远程攻击免疫";
        private List<Hex> SelectedEffectArea(GameView view) => view.EffectAreas.TryGetValue(effectAreaId,out var area) ? area : new List<Hex>();
        private string BoardHint(GameView view,string hint)
        {
            var effect=view.Effects.SingleOrDefault(e => e.Id==effectAreaId);
            return effect==null ? hint : "紫色描边 · " + EffectName(effect) + " · 来源席位 " + (effect.ControllerSeat+1) + "\n" + hint;
        }
        private void RenderActiveEffects(VisualElement parent,GameView view)
        {
            if (view.Effects.Count==0) return;
            var box=Box("event-box"); parent.Add(box); box.Add(Text("持续效果", "section-title"));
            foreach(var effect in view.Effects.OrderBy(e => e.CreationOrder))
            {
                string id=effect.Id;
                box.Add(Text(EffectName(effect) + " · 来源席位 " + (effect.ControllerSeat+1),"body"));
                box.Add(Text("至第 " + effect.Window.EndRound + " 轮第 " + effect.Window.EndTurn + " 回合结束", "tiny"));
                if (effect.Window.StartRound>view.Round || effect.Window.StartTurn>view.Turn && effect.Window.StartRound==view.Round)
                    box.Add(Text("等待第 " + effect.Window.StartRound + " 轮第 " + effect.Window.StartTurn + " 回合生效", "tiny"));
                else if (!view.Units.Any(u => u.Id==effect.SourceUnitId)) box.Add(Text("来源英雄离场，当前没有覆盖区域。", "tiny"));
                else
                {
                    string meaning=effect.Kind==EffectKind.FriendlyMeleeDefeatPrevention ? "本轮范围内友方近战小兵将被击败时，可以弃一张手牌保护" : effect.Kind==EffectKind.OtherEnemyActionImmunity ? "免疫原攻击者（席位"+(effect.ExemptControllerSeat+1)+"）以外的敌方行动；友方仍可影响本人" : effect.Kind==EffectKind.ImmunityAndUnitTraversal ? "免疫其他单位的行动；移动可以穿过单位但不能停在占用格或穿地形" : effect.Kind==EffectKind.FriendlyDisplacementProtection ? "本轮本人及范围内友方单位不能被敌方英雄移动、推动或换位" : effect.Kind==EffectKind.FriendlyNearMinionDefense ? "本人及范围内友方英雄，相邻友方小兵时+1防御（不叠加邻兵数量）" : effect.Kind==EffectKind.FriendlyAttackMinionsDual ? "本队攻击时，范围内友方小兵同时视为近战与远程" : effect.Kind==EffectKind.FriendlyAttackMinionsRanged ? "本队攻击时，范围内友方小兵视为远程" : effect.Kind==EffectKind.FriendlyBasicMinionsRanged ? "本队基础攻击时，范围内友方小兵视为远程" : effect.Kind==EffectKind.NonAdjacentRangedImmunity ? "自身免疫非相邻英雄的远程攻击" : effect.Kind==EffectKind.MovementBoundary ? "敌方移动不能跨越范围边界" : effect.AreaKind==EffectAreaKind.Adjacent ? "相邻敌方英雄不能执行技能" : "范围内敌方英雄不能执行技能";
                    box.Add(Text(meaning,"tiny"));
                }
                if (effect.AreaKind==EffectAreaKind.None) continue;
                var toggle=Button(effectAreaId==id ? "收起范围" : "在地图查看范围",() => { effectAreaId=effectAreaId==id ? "" : id; Render(); },"quiet-button","effect-area-"+id);
                toggle.SetEnabled(view.EffectAreas.TryGetValue(id,out var area) && area.Count>0); box.Add(toggle);
            }
        }
    }
}
