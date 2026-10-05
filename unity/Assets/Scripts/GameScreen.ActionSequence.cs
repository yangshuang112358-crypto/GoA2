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
            actionRail.Browsing=HideCardPreview;
            float width=Mathf.Clamp(Screen.width*.185f,272,296);
            bool CanChoose(ActionCardView n)=>mainFlow && !InitiativePresenting && NetworkCanAct && view.Pending?.Kind=="initiative" && view.Pending.ChooserSeat==seat && view.Pending.CandidateSeats.Contains(n.Seat) && n.IsMain && !n.Started;
            string Key(ActionCardView n)
            {
                var p=view.Players.First(x=>x.Seat==n.Seat);
                return JsonUtility.ToJson(n)+"|"+string.Join(",",(p.EffectiveBonuses??p.PermanentBonuses).OrderBy(x=>x.Key).Select(x=>x.Key+"="+x.Value))+"|"+p.BasicAttackBonus+":"+p.BasicAttackRangeBonus;
            }
            actionRail.Refresh(view,width,CanChoose,target=>Submit(CommandKind.ChooseInitiative,target:target),Key,(tile,n)=>
            {
                var card=catalog.Card(n.CardId);var p=view.Players.First(x=>x.Seat==n.Seat);
                tile.name=n.IsMain?"revealed-seat-"+(n.Seat+1):"action-node-"+n.Id;
                var slab=(ActionSlab)tile;slab.Accent=CardColor(card.Color);
                var header=Box("action-card-heading");tile.Add(header);
                var portrait=new ActionCardGlyph(card.HeroId,p.Team==Team.Blue?new Color(.3f,.65f,1):new Color(1,.36f,.4f),42,true){name="action-portrait-"+n.Id};header.Add(portrait);
                var icon=new ActionCardGlyph(card.PrimaryFamily,CardColor(card.Color),32,false,true){name="action-skill-"+n.Id};header.Add(icon);
                var title=Text(card.Name,"action-card-title");header.Add(title);
                if(n.Role!="main")
                {
                    var badge=new ActionCardGlyph(n.Role,new Color(.95f,.88f,.68f),18){name="action-role-"+n.Id};
                    badge.style.position=Position.Absolute;badge.style.right=-1;badge.style.bottom=-1;icon.Add(badge);
                }
                BuildActionNumbers(tile,card,p,n.Id);
                // One replaceable callback per persistent slab; no stacked stale hover handlers.
                slab.HoverEnter=()=>
                {
                    HideCardPreview();
                    cardPreviewDelay=slab.schedule.Execute(()=>
                    {
                        if(slab.panel==null||!slab.Hovered||newMatchPending||debugPresetsOpen||keywordGlossaryOpen)return;
                        ShowActionCardPreview(slab,card,p,n);
                    }).StartingIn(180);
                };
                slab.HoverExit=HideCardPreview;
                tile.Query<VisualElement>().ForEach(e=>{if(e!=tile)e.pickingMode=PickingMode.Ignore;});
            });
            if(view.ActionSequence.Cards.Count>0)parent.Add(actionRail);
            else if(NetworkMode&&view.Players.Any(p=>p.Plays.Count>0))
            {
                var warning=Text("服务端尚未提供行动序列，请更新服务端到同版发行包。","action-version-warning");parent.Add(warning);
            }
        }
        private void BuildActionNumbers(VisualElement tile,CardDefinition card,PlayerView player,string id)
        {
            var row=Box("action-numbers");tile.Add(row);
            void Stat(string kind,int? value,int bonus,bool infinity=false)
            {
                if(!value.HasValue&&!infinity)return;
                var chip=Box("action-number");chip.name="action-number-"+id+"-"+kind;row.Add(chip);
                chip.Add(new ActionCardGlyph(kind,new Color(.85f,.79f,.64f),16));
                var number=Text(infinity?"∞":(value!.Value+bonus).ToString(),"action-number-value");chip.Add(number);
                number.style.color=bonus>0?new Color(.35f,1,.55f):bonus<0?new Color(1,.32f,.35f):Color.white;
            }
            string key=card.PrimaryFamily=="attack"?"攻击":card.PrimaryFamily=="defense"?"防御":card.PrimaryFamily=="movement"?"移动":"";
            // Stats have stable semantic order; absent actions and zero-valued skills have no badge.
            if(card.PrimaryFamily!="skill"||card.PrimaryValue!=0)
                Stat(card.PrimaryFamily,card.PrimaryValue,card.Exclamation?0:Bonus(player,key)+(card.PrimaryCategory=="基础攻击"?player.BasicAttackBonus:0),card.Exclamation);
            Stat("initiative",card.Initiative,Bonus(player,"先攻"));
            Stat("movement",card.SecondaryMovement,Bonus(player,"移动"));
            Stat("defense",card.SecondaryDefense,Bonus(player,"防御"));
            Stat(card.Subtype=="远程"?"ranged":"range",card.SubtypeValue,Bonus(player,card.Subtype=="远程"?"远程":"范围")+(card.PrimaryCategory=="基础攻击"?player.BasicAttackRangeBonus:0));
        }
        private void ShowActionCardPreview(ActionSlab source,CardDefinition card,PlayerView player,ActionCardView node)
        {
            ShowCardPreview(source,card,player);
            var preview=cardPreview!;preview.AddToClassList("action-full-preview");
            var role=node.Role=="defense"?"用于防御":node.Role=="discard"?"弃置记录（不执行此牌效果）":node.Role=="recover"?"取回记录":node.Role=="reaction"?"反击行动":node.Role=="ultimate"?"紫卡触发":"本回合主牌";
            var detail=Text(role+" · "+(node.Results.Count>0?string.Join("；",node.Results):source.CanChoose?"队长可点击此石板选择先行":node.Started?"行动中":"等待行动"),"action-card-result");
            detail.name="action-result-"+node.Id;detail.pickingMode=PickingMode.Ignore;preview.Add(detail);
        }
    }
}
