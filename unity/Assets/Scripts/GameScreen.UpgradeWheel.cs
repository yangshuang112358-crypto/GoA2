#nullable enable
using System;
using System.Linq;
using Goa2.Domain;
using Goa2.Presentation.UI3D;
using UnityEngine;
using UnityEngine.UIElements;
namespace Goa2.Presentation
{
    public sealed partial class GameScreen
    {
        private string upgradeWheelKey="";
        private float upgradeWheelOpened;
        private void BuildUpgradeWheel()
        {
            var view=renderedView;
            if(!mainFlow || board==null || view.UpgradeOptions.Count==0 || wheelState.Discards.Count>0)return;
            var player=view.Players.Single(p=>p.Seat==seat);
            var unit=view.Units.FirstOrDefault(u=>u.Seat==seat);
            if(unit==null)return;
            PrepareUpgradeSelection(view);
            string key=view.MatchId+":"+seat+":"+view.Round+":"+string.Join(",",view.UpgradeOptions.Select(o=>o.CardId+"/"+o.HeroLevel));
            if(key!=upgradeWheelKey){upgradeWheelKey=key;upgradeWheelOpened=Time.realtimeSinceStartup;}
            var anchor=new VisualElement{name="upgrade-zone",pickingMode=PickingMode.Ignore};
            anchor.style.position=Position.Absolute;anchor.style.width=640;anchor.style.height=640;anchor.style.transformOrigin=new TransformOrigin(0,0,0);
            // Render rebuilds before the first layout/projection tick; never flash at (0,0).
            anchor.style.opacity=0;board.Add(anchor);
            var ring=new VisualElement{name="upgrade-wheel",pickingMode=PickingMode.Ignore};ring.StretchToParentSize();ring.style.transformOrigin=new TransformOrigin(320,320,0);anchor.Add(ring);
            ring.generateVisualContent+=c=>{
                var p=c.painter2D;
                foreach(var rim in new[]{(238f,8f,new Color(.09f,.13f,.16f)),(237f,2f,new Color(.72f,.62f,.39f)),(229f,1f,new Color(.43f,.53f,.58f))}){
                    p.strokeColor=rim.Item3;p.lineWidth=rim.Item2;p.BeginPath();p.Arc(new Vector2(320,320),rim.Item1,0,360);p.Stroke();
                }
            };
            var colors=new[]{"red","green","blue","purple"}.Where(color=>view.UpgradeOptions.Any(o=>o.Color==color)).ToArray();
            if(!colors.Contains("purple")){
                for(int g=0;g<colors.Length;g++)foreach(int branch in new[]{0,2}){
                    var arc=new VisualElement{name="upgrade-arc-"+g+"-"+branch,pickingMode=PickingMode.Ignore};arc.StretchToParentSize();ring.Add(arc);
                    float origin=UpgradeWheelLayout.Angle(colors.Length,g,1),target=UpgradeWheelLayout.Angle(colors.Length,g,branch);
                    arc.generateVisualContent+=context=>{
                        var p=context.painter2D;float sign=Mathf.Sign(target-origin),from=origin+sign*10,to=target-sign*10;
                        // Inner orbit leaves the arrows visible between the large numbered skill rims.
                        Vector2 Point(float angle)=>new Vector2(320,320)+new Vector2(Mathf.Cos(angle*Mathf.Deg2Rad),Mathf.Sin(angle*Mathf.Deg2Rad))*166;
                        foreach(bool shadow in new[]{true,false}){
                            p.strokeColor=shadow?new Color(.06f,.08f,.10f,.95f):new Color(1,.77f,.30f);p.lineWidth=shadow?9:4;p.BeginPath();p.MoveTo(Point(from));
                            for(int i=1;i<=24;i++)p.LineTo(Point(Mathf.Lerp(from,to,i/24f)));p.Stroke();
                        }
                        var tip=Point(to);var tangent=sign*new Vector2(-Mathf.Sin(to*Mathf.Deg2Rad),Mathf.Cos(to*Mathf.Deg2Rad));var normal=new Vector2(-tangent.y,tangent.x);
                        p.fillColor=new Color(1,.85f,.44f);p.BeginPath();p.MoveTo(tip);p.LineTo(tip-tangent*11+normal*6);p.LineTo(tip-tangent*11-normal*6);p.ClosePath();p.Fill();
                    };
                }
            }
            for(int group=0;group<colors.Length;group++){
                string color=colors[group];var options=view.UpgradeOptions.Where(o=>o.Color==color).OrderBy(o=>o.CardId,StringComparer.Ordinal).ToArray();
                bool purple=color=="purple";
                for(int branch=0;branch<(purple?1:3);branch++){
                    bool original=!purple && branch==1;
                    var option=original?null:options[purple || branch==0?0:1];
                    var card=catalog.Card(original?options[0].PreviousCardId:option!.CardId);
                    var previous=purple?null:catalog.Card(options[0].PreviousCardId);
                    float angle=(purple?-90:UpgradeWheelLayout.Angle(colors.Length,group,branch))*Mathf.Deg2Rad;
                    var motion=new SkillWheelState.Motion();
                    var disc=new SkillDisc(color,card,player,CardZone.InHand,upgradeCardId==card.Id,!original && NetworkCanAct,motion,0,()=>{
                        if(!NetworkCanAct || decisionAnimating || original)return;
                        upgradeCardId=upgradeCardId==card.Id?"":card.Id;upgradeColor=color;Sound(upgradeCardId==""?"cancel":"select");Render();
                    },()=>{}){name=(original?"upgrade-current-":"upgrade-card-")+card.Id};
                    disc.style.left=320+Mathf.Cos(angle)*237-78;disc.style.top=320+Mathf.Sin(angle)*237-78;
                    disc.tooltip=original?"当前技能":purple?"满级技能":"获得永久"+option!.Bonus+" +1（来自未选路线）";
                    disc.Inspect=at=>ShowUpgradeInfo(previous,card,original,disc,at);
                    disc.RegisterCallback<PointerEnterEvent>(e=>HoverSkillSound(e.position));
                    disc.RegisterCallback<PointerMoveEvent>(e=>{if(skillPopupOwner==disc)PositionSkillInfo(e.position);});
                    disc.RegisterCallback<PointerLeaveEvent>(_=>{if(skillPopupOwner==disc)HideSkillInfo();});
                    ring.Add(disc);
                }
            }
            var chosen=view.UpgradeOptions.FirstOrDefault(o=>o.CardId==upgradeCardId);
            var benefit=new Label(chosen==null?"选择升级方向，查看永久加成":chosen.Color=="purple"?"获得满级技能": "永久"+chosen.Bonus+" +1\n来自未选路线"){name="upgrade-benefit",pickingMode=PickingMode.Ignore};
            benefit.style.position=Position.Absolute;benefit.style.left=185;benefit.style.top=424;benefit.style.width=270;benefit.style.height=52;benefit.style.whiteSpace=WhiteSpace.Normal;benefit.style.fontSize=20;benefit.style.unityTextAlign=TextAnchor.MiddleCenter;benefit.style.color=new Color(.70f,1,.78f);benefit.style.backgroundColor=new Color(.04f,.07f,.08f,.85f);benefit.style.borderTopLeftRadius=6;benefit.style.borderTopRightRadius=6;benefit.style.borderBottomLeftRadius=6;benefit.style.borderBottomRightRadius=6;ring.Add(benefit);
            if(chosen!=null){
                // Dock owns confirmation; ring stays free of central action buttons.
                var captured=chosen.CardId;confirmButton=new Button(){text="升级为 "+catalog.Card(captured).Name};
                confirmAction=()=>{
                    if(!NetworkCanAct)return;
                    long revision=renderedView.Revision;string match=renderedView.MatchId;int actor=seat;
                    float start=Time.realtimeSinceStartup;decisionAnimating=true;
                    var animation=ring.schedule.Execute(()=>{float t=1-Mathf.Clamp01((Time.realtimeSinceStartup-start)/.22f);ring.style.opacity=t;ring.style.scale=new Scale(new Vector3(Mathf.Max(.01f,t),Mathf.Max(.01f,t),1));}).Every(16);
                    root.schedule.Execute(()=>{animation.Pause();decisionAnimating=false;if(mainFlow && seat==actor && renderedView.MatchId==match && renderedView.Revision==revision && upgradeCardId==captured && NetworkCanAct)Submit(CommandKind.ChooseUpgrade,captured);else Render();}).StartingIn(230);
                };
            }
            anchor.schedule.Execute(()=>{
                if(board==null || anchor.panel==null)return;
                float scale=Mathf.Clamp(Mathf.Min(board.contentRect.width/1300f,board.contentRect.height/820f),.56f,1);
                Vector2 point=board.ProjectHero(unit.Position);anchor.style.left=point.x-320*scale;anchor.style.top=point.y-320*scale;anchor.style.scale=new Scale(new Vector3(scale,scale,1));anchor.style.opacity=1;
                if(!decisionAnimating){float t=Mathf.Clamp01((Time.realtimeSinceStartup-upgradeWheelOpened)/.28f),s=1-Mathf.Pow(1-t,3);ring.style.opacity=t;ring.style.scale=new Scale(new Vector3(Mathf.Max(.01f,s),Mathf.Max(.01f,s),1));}
            }).Every(16);
        }
        private void ShowUpgradeInfo(CardDefinition? previous,CardDefinition next,bool original,VisualElement owner,Vector2 pointer)
        {
            HideHeroHover();HideCardPreview();HideSkillInfo();
            var popup=Box("hero-hover");popup.AddToClassList("skill-cursor-detail");popup.name="skill-description";skillPopup=popup;skillPopupOwner=owner;
            popup.style.width=Mathf.Min(480,root.contentRect.width-24);
            if(previous!=null && !original){RenderCardDetail(popup,previous,false);var arrow=Text("↓","upgrade-comparison-arrow");arrow.style.fontSize=32;arrow.style.color=new Color(1,.78f,.26f);arrow.style.unityTextAlign=TextAnchor.MiddleCenter;popup.Add(arrow);}
            RenderCardDetail(popup,next,false);
            var option=renderedView.UpgradeOptions.FirstOrDefault(o=>o.CardId==next.Id);
            if(!original && option!=null && !string.IsNullOrEmpty(option.Bonus)){
                var benefit=Text("此次升级获得：永久"+option.Bonus+" +1（来自未选路线）","body");benefit.name="upgrade-detail-benefit";benefit.style.color=new Color(.40f,1,.55f);popup.Add(benefit);
            }
            popup.pickingMode=PickingMode.Ignore;popup.Query<VisualElement>().ForEach(e=>e.pickingMode=PickingMode.Ignore);
            // Scale the complete comparison as a unit if the viewport is short; no clipped card text.
            popup.style.transformOrigin=new TransformOrigin(0,0,0);
            skillPopupPointer=pointer;popup.RegisterCallback<GeometryChangedEvent>(_=>{
                float scale=Mathf.Min(1,(root.contentRect.height-24)/Mathf.Max(1,popup.resolvedStyle.height));popup.style.scale=new Scale(new Vector3(scale,scale,1));PositionSkillInfo(skillPopupPointer);
            });root.Add(popup);PositionSkillInfo(pointer);
        }
    }
}
