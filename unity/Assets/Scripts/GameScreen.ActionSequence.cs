#nullable enable
using System.Linq;
using Goa2.Domain;
using Goa2.Presentation.UI3D;
using UnityEngine;
using UnityEngine.UIElements;
namespace Goa2.Presentation
{
    public sealed partial class GameScreen
    {
        private ActionSequenceRail? actionRail;
        private ActionSequenceAudio? actionAudio;
        private void BuildActionSequence(VisualElement parent,GameView view)
        {
            if(actionAudio==null)actionAudio=gameObject.AddComponent<ActionSequenceAudio>();
            if(actionRail==null)actionRail=new ActionSequenceRail(cue=>actionAudio.Play(cue));
            float width=Mathf.Clamp(Screen.width*.31f,380,500);
            bool CanChoose(ActionCardView n)=>NetworkCanAct && view.Pending?.Kind=="initiative" && view.Pending.ChooserSeat==seat && view.Pending.CandidateSeats.Contains(n.Seat) && n.IsMain && !n.Started;
            string Key(ActionCardView n)
            {
                var p=view.Players.First(x=>x.Seat==n.Seat);
                return JsonUtility.ToJson(n)+"|"+string.Join(",",(p.EffectiveBonuses??p.PermanentBonuses).OrderBy(x=>x.Key).Select(x=>x.Key+"="+x.Value))+"|"+p.BasicAttackBonus+":"+p.BasicAttackRangeBonus;
            }
            actionRail.Refresh(view,width,CanChoose,target=>Submit(CommandKind.ChooseInitiative,target:target),Key,(tile,n)=>
            {
                var card=catalog.Card(n.CardId);var p=view.Players.First(x=>x.Seat==n.Seat);
                tile.name=n.IsMain?"revealed-seat-"+(n.Seat+1):"action-node-"+n.Id;
                ((ActionSlab)tile).Accent=CardColor(card.Color);
                var title=Text(card.Name,"action-card-title");tile.Add(title);
                string role=n.Role=="defense"?"◈ 防御":n.Role=="discard"?"↓ 弃置":n.Role=="recover"?"↑ 取回":n.Role=="ultimate"?"✦ 紫卡触发":n.Role=="reaction"?"↪ 反击":"";
                tile.Add(Text(HeroName(card.HeroId)+" · "+ColorName(card.Color)+"色 · "+(card.Level.HasValue?"卡牌 "+card.Level+" 级":"基础牌")+(role==""?"":" · "+role),"action-card-owner"));
                CompactCardNumbers(tile,card,p,true,"action-"+n.Id);
                var rules=RulesText(CardTextMarkup.Description(card),"action-card-rules");rules.name="action-rules-"+n.Id;tile.Add(rules);
                tile.Add(Text("升级被动 · "+(string.IsNullOrWhiteSpace(card.Passive)?"无":card.Passive),"action-card-passive"));
                string results=n.Results.Count==0?(CanChoose(n)?"队长选择 · 点击此牌先行动":n.Started?"行动中":"等待行动"):string.Join("；",n.Results);
                var footer=Text(results,"action-card-result");footer.name="action-result-"+n.Id;tile.Add(footer);
                var team=new VisualElement {name="revealed-team-"+(n.Seat+1)};team.AddToClassList("action-team-stripe");
                team.style.backgroundColor=p.Team==Team.Blue?new Color(.2f,.5f,.9f):new Color(.85f,.22f,.28f);tile.Add(team);
                // Whole rules text is already on the slab; no tooltip or summary replaces it.
                tile.Query<VisualElement>().ForEach(e=>{if(e!=tile)e.pickingMode=PickingMode.Ignore;});
            });
            if(view.ActionSequence.Cards.Count>0)parent.Add(actionRail);
            else if(NetworkMode&&view.Players.Any(p=>p.Plays.Count>0))
            {
                var warning=Text("服务端尚未提供行动序列，请更新服务端到同版发行包。","action-version-warning");parent.Add(warning);
            }
        }
    }
}
