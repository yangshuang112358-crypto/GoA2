#nullable enable
using System;
using System.Collections.Generic;
using Goa2.Domain;
using UnityEngine;
using UnityEngine.UIElements;
namespace Goa2.Presentation.UI3D {
 // Camera-facing presentation controls. All legality and private data come from the host projection.
 public sealed class SkillWheel : VisualElement {
  public const float Size=520;
  private float opened=Time.realtimeSinceStartup;private bool closing;
  public readonly Button Confirm;
  public readonly Button Alternative;
  public SkillWheel(Team team,string caption,Action confirm,Action alternative) {
   name="skill-wheel";pickingMode=PickingMode.Ignore;style.position=Position.Absolute;style.width=Size;style.height=Size;style.transformOrigin=new TransformOrigin(260,260,0);
   var steel=new VisualElement{name="carved-wheel-rim",pickingMode=PickingMode.Ignore};
   steel.style.position=Position.Absolute;steel.style.left=78;steel.style.top=78;steel.style.width=364;steel.style.height=364;
   steel.style.backgroundImage=new StyleBackground(SkillDiscArtwork.Texture("wheel"));Add(steel);
   Confirm=new Button(confirm){name="wheel-confirm",text=caption};Confirm.style.position=Position.Absolute;Confirm.style.left=210;Confirm.style.top=210;Confirm.style.width=100;Confirm.style.height=100;Confirm.style.fontSize=23;Confirm.style.whiteSpace=WhiteSpace.Normal;Confirm.style.unityTextAlign=TextAnchor.MiddleCenter;
   Color hue=team==Team.Blue ? new Color(.16f,.55f,1) : new Color(.95f,.18f,.25f);
   Confirm.style.backgroundColor=new Color(hue.r,hue.g,hue.b,.28f);Confirm.style.color=Color.white;
   Confirm.style.borderTopLeftRadius=50;Confirm.style.borderTopRightRadius=50;Confirm.style.borderBottomLeftRadius=50;Confirm.style.borderBottomRightRadius=50;
   Confirm.style.borderTopWidth=3;Confirm.style.borderBottomWidth=3;Confirm.style.borderLeftWidth=3;Confirm.style.borderRightWidth=3;
   Confirm.style.borderTopColor=hue*.7f;Confirm.style.borderBottomColor=hue*.7f;Confirm.style.borderLeftColor=hue*.7f;Confirm.style.borderRightColor=hue*.7f;Add(Confirm);
   Confirm.generateVisualContent+=c=>{var p=c.painter2D;p.strokeColor=new Color(hue.r,hue.g,hue.b,.65f);p.lineWidth=1;float a=Time.realtimeSinceStartup*25;for(int i=0;i<3;i++){p.BeginPath();p.Arc(new Vector2(50,50),43,a+i*120,a+i*120+75);p.Stroke();}};
   Alternative=new Button(alternative){name="wheel-alternative"};Alternative.style.position=Position.Absolute;Alternative.style.left=145;Alternative.style.top=480;Alternative.style.width=230;Alternative.style.height=34;Alternative.style.marginLeft=0;Alternative.style.marginTop=0;Alternative.style.fontSize=18;Alternative.style.whiteSpace=WhiteSpace.NoWrap;Alternative.style.backgroundColor=new Color(.09f,.14f,.18f);Alternative.style.color=Color.white;Alternative.style.borderTopColor=hue;Alternative.style.borderBottomColor=hue;Alternative.style.borderLeftColor=hue;Alternative.style.borderRightColor=hue;Add(Alternative);
   RegisterCallback<PointerDownEvent>(e=>e.StopPropagation());RegisterCallback<WheelEvent>(e=>e.StopPropagation());
   schedule.Execute(()=>{float t=Mathf.Clamp01((Time.realtimeSinceStartup-opened)/.23f);float scale=closing ? 1-t : 1-Mathf.Pow(1-t,3);style.opacity=scale;style.scale=new Scale(new Vector3(Mathf.Max(.01f,scale),Mathf.Max(.01f,scale),1));Confirm.MarkDirtyRepaint();if(closing && t>=1)RemoveFromHierarchy();}).Every(16);
  }
  public void ResumeOpen(float started){opened=started;}
  public void Collapse(){if(closing)return;closing=true;opened=Time.realtimeSinceStartup;}
  public void AddSkill(SkillDisc disc,int index){float a=(-90+index*72)*Mathf.Deg2Rad;disc.style.left=260+Mathf.Cos(a)*166-78;disc.style.top=260+Mathf.Sin(a)*166-78;Add(disc);}
 }
 public sealed class SkillDisc : VisualElement {
  public Action<Vector2>? Inspect;
  private readonly SkillWheelState.Motion motion;private readonly Color rim;private readonly Label caption;private readonly VisualElement badges;
  private readonly bool pressed,discarded;private readonly float flipAt;private Vector2 tilt;private bool hover;private float last=Time.realtimeSinceStartup;
  private Vector2 center=>new Vector2(78,78+motion.Press*5-motion.Hover*5);
  public SkillDisc(string color,CardDefinition? card,PlayerView player,CardZone zone,bool preview,bool allowed,SkillWheelState.Motion motion,float flipAt,Action click,Action right) {
   this.motion=motion;this.flipAt=flipAt;pressed=zone==CardZone.Selected || zone==CardZone.PlayedResolved || zone==CardZone.PlayedUnresolved || preview;discarded=zone==CardZone.Discarded;
   rim=Board3DScene.ColorOf(color switch {"gold"=>"#E9BD54","silver"=>"#CAD3E0","red"=>"#E25464","green"=>"#52C586","purple"=>"#B16DE8",_=>"#589CED"});
   if(!motion.Ready){motion.Ready=true;motion.Press=pressed?1:0;motion.Flip=discarded && flipAt<=0 ? Mathf.PI:0;}
   name="skill-"+color;style.position=Position.Absolute;style.width=156;style.height=156;style.overflow=Overflow.Visible;
   caption=new Label(card!=null && card.Name.Length==4 ? card.Name.Substring(0,2)+"\n"+card.Name.Substring(2) : card?.Name ?? "?"){name="skill-caption",pickingMode=PickingMode.Ignore};caption.style.position=Position.Absolute;caption.style.left=39;caption.style.top=51;caption.style.width=78;caption.style.height=54;caption.style.fontSize=19;caption.style.whiteSpace=WhiteSpace.Normal;caption.style.unityTextAlign=TextAnchor.MiddleCenter;caption.style.unityFontStyleAndWeight=FontStyle.Bold;caption.style.color=new Color(1,.94f,.80f);caption.style.unityTextOutlineColor=new Color(.02f,.03f,.035f);caption.style.unityTextOutlineWidth=.7f;caption.style.marginLeft=0;caption.style.marginRight=0;Add(caption);
   badges=new VisualElement{pickingMode=PickingMode.Ignore};badges.StretchToParentSize();Add(badges);
   if(card!=null){
    int Bonus(string key)=>(player.EffectiveBonuses ?? player.PermanentBonuses).TryGetValue(key,out int b)?b:0;
    void Badge(string slot,string kind,int? value,int bonus,bool infinity=false){if(!value.HasValue && !infinity)return;var b=new SkillBadge(kind,infinity?"∞":kind=="spark" && value==0 ? "" : (value!.Value+bonus).ToString(),bonus){name="badge-"+slot};var bounds=SkillDiscArtwork.BadgeRect(slot);b.style.left=bounds.x;b.style.top=bounds.y;badges.Add(b);}
    // Fixed semantic positions: movement / defense above, primary / range below, initiative at foot.
    Badge("movement","boot",card.SecondaryMovement,Bonus("移动"));
    Badge("defense","shield",card.SecondaryDefense,Bonus("防御"));
    string family=card.PrimaryFamily=="attack"?"sword":card.PrimaryFamily=="defense"?"shield":card.PrimaryFamily=="movement"?"boot":"spark";
    string key=family=="sword"?"攻击":family=="shield"?"防御":family=="boot"?"移动":"";
    Badge("primary",family,card.PrimaryValue,card.Exclamation?0:Bonus(key)+(card.PrimaryCategory=="基础攻击"?player.BasicAttackBonus:0),card.Exclamation);
    Badge("range",card.Subtype=="远程"?"arrow":"range",card.SubtypeValue,Bonus(card.Subtype=="远程"?"远程":"范围")+(card.PrimaryCategory=="基础攻击"?player.BasicAttackRangeBonus:0));
    Badge("initiative","hourglass",card.Initiative,Bonus("先攻"));

   }
   RegisterCallback<PointerEnterEvent>(_=>hover=true);RegisterCallback<PointerLeaveEvent>(_=>{hover=false;tilt=Vector2.zero;});
   RegisterCallback<PointerMoveEvent>(e=>tilt=new Vector2((e.localPosition.x-78)/78,(e.localPosition.y-78)/78));
   RegisterCallback<PointerDownEvent>(e=>{if(e.button==1){right();Inspect?.Invoke(e.position);}else if(e.button==0 && allowed)click();e.StopPropagation();});
   generateVisualContent+=Draw;
   schedule.Execute(()=>{float now=Time.realtimeSinceStartup,dt=Mathf.Min(.05f,now-last);last=now;float blend=1-Mathf.Exp(-15*dt);motion.Hover=Mathf.Lerp(motion.Hover,hover?1:0,blend);motion.Velocity+=((pressed?1:0)-motion.Press)*190*dt-motion.Velocity*20*dt;motion.Press=Mathf.Clamp(motion.Press+motion.Velocity*dt,-.08f,1.08f);motion.Flip=Mathf.Lerp(motion.Flip,discarded && now>=flipAt ? Mathf.PI:0,1-Mathf.Exp(-9*dt));float face=Mathf.Abs(Mathf.Cos(motion.Flip));caption.style.display=Mathf.Cos(motion.Flip)<0?DisplayStyle.None:DisplayStyle.Flex;badges.style.display=caption.style.display;caption.style.opacity=1-.48f*motion.Press;badges.style.opacity=1-.35f*motion.Press;badges.style.translate=new Translate(tilt.x*motion.Hover*3,motion.Press*5-motion.Hover*5);badges.style.rotate=new Rotate(tilt.x*motion.Hover*2);badges.style.scale=new Scale(new Vector3(Mathf.Max(.01f,face)*(1-.04f*motion.Press),1-.04f*motion.Press,1));caption.style.translate=new Translate(tilt.x*motion.Hover*3,motion.Press*5-motion.Hover*5+tilt.y*motion.Hover*3);caption.style.scale=new Scale(new Vector3(Mathf.Max(.01f,face),1,1));MarkDirtyRepaint();}).Every(16);
  }
  private Vector2 Project(Vector2 v){var q=Quaternion.Euler(tilt.y*motion.Hover*14,tilt.x*motion.Hover*14+motion.Flip*Mathf.Rad2Deg,0)*new Vector3(v.x,v.y,0);float f=300/(300-q.z);return center+new Vector2(q.x,q.y)*f*(1-.04f*motion.Press);}
  private void Disc(Painter2D p,float radius,Color color){p.fillColor=color;p.BeginPath();for(int i=0;i<64;i++){float a=i*Mathf.PI/32;var point=Project(new Vector2(Mathf.Cos(a)*radius,Mathf.Sin(a)*radius));if(i==0)p.MoveTo(point);else p.LineTo(point);}p.ClosePath();p.Fill();}
  private void Draw(MeshGenerationContext context){
   bool back=Mathf.Cos(motion.Flip)<0;
   var p=context.painter2D;
   Disc(p,72+motion.Hover*2,new Color(rim.r,rim.g,rim.b,.08f+.22f*motion.Hover));
   float shade=back?1:1-.38f*motion.Press;
   SkillDiscArtwork.Draw(context,back?"back":"front",Project,new Color(shade,shade,shade,1));
   if(!back){
    // Card colour is an enamel inlay in the sculpted metal, not an external panel.
    p.strokeColor=rim*(1-.38f*motion.Press);p.lineWidth=4f;p.BeginPath();
    for(int i=0;i<=96;i++){float a=i*Mathf.PI/48;var point=Project(new Vector2(Mathf.Cos(a)*60,Mathf.Sin(a)*60));if(i==0)p.MoveTo(point);else p.LineTo(point);}p.ClosePath();p.Stroke();
   }
  }
 }
}
