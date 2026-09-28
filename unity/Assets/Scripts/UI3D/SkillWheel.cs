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
   var steel=new VisualElement{pickingMode=PickingMode.Ignore};steel.StretchToParentSize();Add(steel);
   steel.generateVisualContent+=c=>{var p=c.painter2D;foreach(var rim in new[]{(173f,12f,new Color(.12f,.15f,.18f)),(171f,3f,new Color(.63f,.67f,.7f)),(163f,2f,new Color(.27f,.31f,.34f))}){p.strokeColor=rim.Item3;p.lineWidth=rim.Item2;p.BeginPath();p.Arc(new Vector2(260,260),rim.Item1,0,360);p.Stroke();}for(int i=0;i<40;i++){float a=i*Mathf.PI/20;p.strokeColor=new Color(.75f,.8f,.85f,.35f);p.lineWidth=1;p.BeginPath();p.MoveTo(new Vector2(260+Mathf.Cos(a)*166,260+Mathf.Sin(a)*166));p.LineTo(new Vector2(260+Mathf.Cos(a)*170,260+Mathf.Sin(a)*170));p.Stroke();}};
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
  private readonly SkillWheelState.Motion motion;private readonly Color rim;private readonly Label caption;private readonly VisualElement badges;
  private readonly bool pressed,discarded;private readonly float flipAt;private Vector2 tilt;private bool hover;private float last=Time.realtimeSinceStartup;
  private Vector2 center=>new Vector2(78,78+motion.Press*5-motion.Hover*5);
  public SkillDisc(string color,CardDefinition? card,PlayerView player,CardZone zone,bool preview,bool allowed,SkillWheelState.Motion motion,float flipAt,Action click,Action right) {
   this.motion=motion;this.flipAt=flipAt;pressed=zone==CardZone.Selected || zone==CardZone.PlayedResolved || zone==CardZone.PlayedUnresolved || preview;discarded=zone==CardZone.Discarded;
   rim=Board3DScene.ColorOf(color switch {"gold"=>"#E9BD54","silver"=>"#CAD3E0","red"=>"#E25464","green"=>"#52C586",_=>"#589CED"});
   if(!motion.Ready){motion.Ready=true;motion.Press=pressed?1:0;motion.Flip=discarded && flipAt<=0 ? Mathf.PI:0;}
   name="skill-"+color;style.position=Position.Absolute;style.width=156;style.height=156;style.overflow=Overflow.Visible;
   caption=new Label(card!=null && card.Name.Length==4 ? card.Name.Substring(0,2)+"\n"+card.Name.Substring(2) : card?.Name ?? "?"){pickingMode=PickingMode.Ignore};caption.style.position=Position.Absolute;caption.style.left=39;caption.style.top=51;caption.style.width=78;caption.style.height=54;caption.style.fontSize=19;caption.style.whiteSpace=WhiteSpace.Normal;caption.style.unityTextAlign=TextAnchor.MiddleCenter;caption.style.unityFontStyleAndWeight=FontStyle.Bold;caption.style.color=Color.white;caption.style.marginLeft=0;caption.style.marginRight=0;Add(caption);
   badges=new VisualElement{pickingMode=PickingMode.Ignore};badges.StretchToParentSize();Add(badges);
   if(card!=null){
    int Bonus(string key)=>(player.EffectiveBonuses ?? player.PermanentBonuses).TryGetValue(key,out int b)?b:0;
    void Badge(string slot,string kind,int? value,int bonus,float x,float y,bool infinity=false){if(!value.HasValue && !infinity)return;var b=new SkillBadge(kind,infinity?"∞":kind=="spark" && value==0 ? "" : (value!.Value+bonus).ToString(),bonus){name="badge-"+slot};b.style.left=x;b.style.top=y;badges.Add(b);}
    // Fixed semantic positions: movement / defense above, primary / range below, initiative at foot.
    Badge("movement","boot",card.SecondaryMovement,Bonus("移动"),-8,9);
    Badge("defense","shield",card.SecondaryDefense,Bonus("防御"),106,9);
    string family=card.PrimaryFamily=="attack"?"sword":card.PrimaryFamily=="defense"?"shield":card.PrimaryFamily=="movement"?"boot":"spark";
    string key=family=="sword"?"攻击":family=="shield"?"防御":family=="boot"?"移动":"";
    Badge("primary",family,card.PrimaryValue,card.Exclamation?0:Bonus(key)+(card.PrimaryCategory=="基础攻击"?player.BasicAttackBonus:0),-8,96,card.Exclamation);
    Badge("range",card.Subtype=="远程"?"arrow":"range",card.SubtypeValue,Bonus(card.Subtype=="远程"?"远程":"范围")+(card.PrimaryCategory=="基础攻击"?player.BasicAttackRangeBonus:0),106,96);
    Badge("initiative","hourglass",card.Initiative,Bonus("先攻"),49,121);

   }
   RegisterCallback<PointerEnterEvent>(_=>hover=true);RegisterCallback<PointerLeaveEvent>(_=>{hover=false;tilt=Vector2.zero;});
   RegisterCallback<PointerMoveEvent>(e=>tilt=new Vector2((e.localPosition.x-78)/78,(e.localPosition.y-78)/78));
   RegisterCallback<PointerDownEvent>(e=>{if(e.button==1)right();else if(e.button==0 && allowed)click();e.StopPropagation();});
   generateVisualContent+=Draw;
   schedule.Execute(()=>{float now=Time.realtimeSinceStartup,dt=Mathf.Min(.05f,now-last);last=now;float blend=1-Mathf.Exp(-15*dt);motion.Hover=Mathf.Lerp(motion.Hover,hover?1:0,blend);motion.Velocity+=((pressed?1:0)-motion.Press)*190*dt-motion.Velocity*20*dt;motion.Press=Mathf.Clamp(motion.Press+motion.Velocity*dt,-.08f,1.08f);motion.Flip=Mathf.Lerp(motion.Flip,discarded && now>=flipAt ? Mathf.PI:0,1-Mathf.Exp(-9*dt));float face=Mathf.Abs(Mathf.Cos(motion.Flip));caption.style.display=Mathf.Cos(motion.Flip)<0?DisplayStyle.None:DisplayStyle.Flex;badges.style.display=caption.style.display;caption.style.opacity=1-.48f*motion.Press;badges.style.opacity=1-.35f*motion.Press;badges.style.translate=new Translate(tilt.x*motion.Hover*3,motion.Press*5-motion.Hover*5);badges.style.rotate=new Rotate(tilt.x*motion.Hover*2);caption.style.translate=new Translate(tilt.x*motion.Hover*3,motion.Press*5-motion.Hover*5+tilt.y*motion.Hover*3);caption.style.scale=new Scale(new Vector3(Mathf.Max(.01f,face),1,1));MarkDirtyRepaint();}).Every(16);
  }
  private Vector2 Project(Vector2 v){var q=Quaternion.Euler(tilt.y*motion.Hover*14,tilt.x*motion.Hover*14+motion.Flip*Mathf.Rad2Deg,0)*new Vector3(v.x,v.y,0);float f=300/(300-q.z);return center+new Vector2(q.x,q.y)*f*(1-.04f*motion.Press);}
  private void Disc(Painter2D p,float radius,Color color){p.fillColor=color;p.BeginPath();for(int i=0;i<64;i++){float a=i*Mathf.PI/32;var point=Project(new Vector2(Mathf.Cos(a)*radius,Mathf.Sin(a)*radius));if(i==0)p.MoveTo(point);else p.LineTo(point);}p.ClosePath();p.Fill();}
  private void Draw(MeshGenerationContext c){var p=c.painter2D;Disc(p,57+motion.Hover*3,new Color(rim.r,rim.g,rim.b,.12f+.28f*motion.Hover));Disc(p,54,new Color(.06f,.07f,.08f));Disc(p,51,rim*(1-.48f*motion.Press));Disc(p,46,new Color(.09f,.13f,.17f));bool back=Mathf.Cos(motion.Flip)<0;Disc(p,43,back?new Color(.3f,.34f,.37f):new Color(.13f,.2f,.25f)*(1-.4f*motion.Press));
   for(int i=0;i<32;i++){float a=i*Mathf.PI/16,b=(i+1)*Mathf.PI/16;p.strokeColor=Color.Lerp(new Color(.1f,.12f,.14f),new Color(.95f,.92f,.8f),(.5f-.5f*Mathf.Sin(a))*.7f);p.lineWidth=3;p.BeginPath();p.MoveTo(Project(new Vector2(Mathf.Cos(a)*49,Mathf.Sin(a)*49)));p.LineTo(Project(new Vector2(Mathf.Cos(b)*49,Mathf.Sin(b)*49)));p.Stroke();}
   if(back){foreach(int sign in new[]{-1,1}){p.strokeColor=new Color(.05f,.07f,.09f);p.lineWidth=8;p.BeginPath();p.MoveTo(Project(new Vector2(-20,-20*sign)));p.LineTo(Project(new Vector2(20,20*sign)));p.Stroke();p.strokeColor=new Color(.55f,.6f,.63f);p.lineWidth=2;p.BeginPath();p.MoveTo(Project(new Vector2(-18,-20*sign+3)));p.LineTo(Project(new Vector2(20,18*sign+3)));p.Stroke();}}
  }
 }
 public sealed class SkillBadge : VisualElement {
  public SkillBadge(string kind,string value,int bonus){
   pickingMode=PickingMode.Ignore;style.position=Position.Absolute;style.width=58;style.height=40;style.overflow=Overflow.Visible;
   var text=new Label(value){name="badge-number",pickingMode=PickingMode.Ignore};text.style.position=Position.Absolute;text.style.left=0;text.style.top=-13;text.style.width=58;text.style.height=29;text.style.fontSize=24;text.style.unityTextAlign=TextAnchor.MiddleCenter;text.style.unityFontStyleAndWeight=FontStyle.Bold;text.style.color=bonus>0?new Color(.35f,1,.55f):bonus<0?new Color(1,.32f,.35f):Color.white;text.style.unityTextOutlineColor=new Color(.025f,.025f,.025f);text.style.unityTextOutlineWidth=1;text.style.marginLeft=0;text.style.marginRight=0;text.style.marginTop=0;text.style.marginBottom=0;text.style.paddingLeft=0;text.style.paddingRight=0;text.style.paddingTop=0;text.style.paddingBottom=0;Add(text);
   generateVisualContent+=c=>{var p=c.painter2D;
    void Poly(Color color,params Vector2[] points){p.fillColor=color;p.BeginPath();p.MoveTo(points[0]);for(int i=1;i<points.Length;i++)p.LineTo(points[i]);p.ClosePath();p.Fill();}
    void Line(Color color,float width,params Vector2[] points){p.strokeColor=color;p.lineWidth=width;p.BeginPath();p.MoveTo(points[0]);for(int i=1;i<points.Length;i++)p.LineTo(points[i]);p.Stroke();}
    var dark=new Color(.055f,.065f,.075f);var gold=new Color(.76f,.65f,.43f);var light=new Color(.98f,.87f,.61f);
    Poly(new Color(0,0,0,.6f),new Vector2(0,11),new Vector2(9,3),new Vector2(48,3),new Vector2(59,13),new Vector2(54,36),new Vector2(29,41),new Vector2(6,37));
    Poly(gold,new Vector2(1,8),new Vector2(11,1),new Vector2(46,1),new Vector2(57,10),new Vector2(52,33),new Vector2(29,38),new Vector2(7,33));
    Poly(new Color(.18f,.22f,.25f),new Vector2(5,10),new Vector2(12,5),new Vector2(45,5),new Vector2(52,12),new Vector2(48,30),new Vector2(29,34),new Vector2(11,30));
    Poly(new Color(.10f,.13f,.16f),new Vector2(5,17),new Vector2(52,17),new Vector2(48,30),new Vector2(29,34),new Vector2(11,30));
    Line(light,1.5f,new Vector2(2,8),new Vector2(11,1),new Vector2(46,1),new Vector2(56,10));
    Line(new Color(.31f,.25f,.17f),2,new Vector2(7,32),new Vector2(29,38),new Vector2(52,33));
    // Small feathered metal shoulders, integrated into the plaque rather than text boxes.
    for(int j=0;j<3;j++){float y=22+j*4;Line(gold,1.3f,new Vector2(1+j,y),new Vector2(9+j,y+3));}
    Vector2[] glyph;
    if(kind=="sword")glyph=new[]{new Vector2(10,30),new Vector2(7,27),new Vector2(12,21),new Vector2(8,17),new Vector2(10,15),new Vector2(13,18),new Vector2(21,6),new Vector2(23,7),new Vector2(18,22),new Vector2(22,25),new Vector2(20,27),new Vector2(16,24)};
    else if(kind=="shield")glyph=new[]{new Vector2(8,10),new Vector2(16,6),new Vector2(24,10),new Vector2(22,23),new Vector2(16,30),new Vector2(9,23)};
    else if(kind=="boot")glyph=new[]{new Vector2(10,7),new Vector2(20,7),new Vector2(18,21),new Vector2(25,24),new Vector2(25,29),new Vector2(8,29),new Vector2(8,25),new Vector2(11,20)};
    else if(kind=="hourglass")glyph=new[]{new Vector2(8,7),new Vector2(24,7),new Vector2(23,11),new Vector2(18,18),new Vector2(23,25),new Vector2(24,30),new Vector2(8,30),new Vector2(9,25),new Vector2(14,18),new Vector2(9,11)};
    else if(kind=="arrow")glyph=new[]{new Vector2(7,26),new Vector2(17,15),new Vector2(13,12),new Vector2(25,6),new Vector2(23,20),new Vector2(20,17),new Vector2(10,29)};
    else glyph=new[]{new Vector2(16,5),new Vector2(19,14),new Vector2(27,18),new Vector2(19,21),new Vector2(16,31),new Vector2(13,21),new Vector2(5,18),new Vector2(13,14)};
    for(int i=0;i<glyph.Length;i++)glyph[i]+=new Vector2(13,4);
    var shadow=new Vector2[glyph.Length];for(int i=0;i<glyph.Length;i++)shadow[i]=glyph[i]+new Vector2(1.5f,2);Poly(dark,shadow);Poly(gold,glyph);
    Line(light,1.2f,glyph[0],glyph[1],glyph[2]);
    if(kind=="shield")Line(dark,1.5f,new Vector2(29,14),new Vector2(29,29));
    if(kind=="boot")Line(dark,2,new Vector2(24,18),new Vector2(31,18));
    if(kind=="hourglass"){Poly(new Color(.20f,.28f,.31f),new Vector2(25,14),new Vector2(33,14),new Vector2(29,20));Line(light,1.5f,new Vector2(25,31),new Vector2(33,31));}
    if(kind=="range"){p.strokeColor=light;p.lineWidth=1;p.BeginPath();p.Arc(new Vector2(29,22),11,0,360);p.Stroke();}
   };
  }
 }
}
