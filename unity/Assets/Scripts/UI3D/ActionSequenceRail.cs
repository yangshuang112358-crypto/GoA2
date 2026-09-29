#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Goa2.Domain;
using UnityEngine;
using UnityEngine.UIElements;

namespace Goa2.Presentation.UI3D
{
    // Persistent across GameScreen.Render: public facts update content; motion/scroll stay local.
    public sealed class ActionSequenceRail : VisualElement
    {
        private sealed class Entry
        {
            public ActionCardView Data=null!;public ActionSlab Slab=null!;
            public float X,Y,TargetY,Height=240,Width,Age,Opacity=1,Travel;
            public int Depth;public string ContentKey="";
        }
        private readonly VisualElement layer=new VisualElement(), links=new VisualElement();
        private readonly Dictionary<string,Entry> entries=new Dictionary<string,Entry>();
        private readonly Dictionary<string,ActionGroupCoin> coins=new Dictionary<string,ActionGroupCoin>();
        private List<ActionCardView> ordered=new List<ActionCardView>();
        private string match="",sequence="",focus="";private long revision=-1;
        private float scroll,scrollGoal,lastTime,totalHeight;private bool snap,autoFocus,seen;
        private readonly Action<string> sound;
        public bool FreeBrowsing {get;private set;}
        public float ScrollOffset=>scroll;
        public int CoinCount=>coins.Count;
        public string FocusId=>focus;
        public static bool Grouped(ActionCardView a,ActionCardView b)=>a.IsMain&&b.IsMain&&!a.Started&&!b.Started&&a.Initiative==b.Initiative;
        public ActionSequenceRail(Action<string> sound)
        {
            this.sound=sound;name="revealed-zone";AddToClassList("action-sequence-rail");
            style.position=Position.Absolute;style.left=8;style.top=104;style.bottom=12;style.overflow=Overflow.Hidden;
            layer.style.position=Position.Absolute;layer.style.left=0;layer.style.top=0;layer.style.right=0;Add(layer);
            links.pickingMode=PickingMode.Ignore;links.StretchToParentSize();layer.Add(links);links.generateVisualContent+=DrawLinks;
            RegisterCallback<WheelEvent>(e=>{ScrollBy(e.delta.y*55);e.StopImmediatePropagation();e.PreventDefault();},TrickleDown.TrickleDown);
            RegisterCallback<PointerDownEvent>(e=>e.StopPropagation());
            lastTime=Time.realtimeSinceStartup;schedule.Execute(Animate).Every(16);
        }
        public void ScrollBy(float amount){FreeBrowsing=true;autoFocus=false;scrollGoal=Mathf.Clamp(scrollGoal+amount,0,MaxScroll());}
        private float MaxScroll()=>Mathf.Max(0,Mathf.Max(totalHeight-resolvedStyle.height,entries.Values.Where(e=>e.Data.ParentId=="").Select(e=>e.TargetY).DefaultIfEmpty(0).Max()));
        public void Refresh(GameView view,float width,Func<ActionCardView,bool> canChoose,Action<int> choose,
            Func<ActionCardView,string> contentKey,Action<VisualElement,ActionCardView> build)
        {
            var trace=view.ActionSequence;bool reset=!seen||match!=view.MatchId||view.Revision<revision;
            bool newSequence=sequence!=trace.Id;bool animate=seen&&!reset;
            if(reset||newSequence){entries.Clear();coins.Clear();layer.Clear();layer.Add(links);scroll=scrollGoal=0;FreeBrowsing=false;focus="";}
            snap=reset;seen=true;match=view.MatchId;revision=view.Revision;sequence=trace.Id;
            style.width=width;ordered=trace.Cards;
            var ids=new HashSet<string>(ordered.Select(n=>n.Id));
            foreach(var id in entries.Keys.Where(id=>!ids.Contains(id)).ToArray()){entries[id].Slab.RemoveFromHierarchy();entries.Remove(id);}
            bool added=false,reordered=false;
            foreach(var n in ordered)
            {
                if(!entries.TryGetValue(n.Id,out var e))
                {
                    e=new Entry{Data=n,Opacity=animate?0:1};entries[n.Id]=e;
                    var copy=e;e.Slab=new ActionSlab(()=>{if(copy.Slab.CanChoose)choose(copy.Data.Seat);});
                    layer.Add(e.Slab);e.Y=animate?(newSequence?-650-entries.Count*60:180):0;e.X=animate&&!newSequence?95:0;added=true;
                    e.Slab.RegisterCallback<GeometryChangedEvent>(_=>Reflow());
                }
                e.Data=n;e.Slab.Notched=n.IsMain;e.Slab.Active=trace.FocusId==n.Id&&!n.Resolved;
                e.Slab.CanChoose=canChoose(n);e.Slab.Resolved=n.Resolved;
                int depth=0;var p=n.ParentId;var visited=new HashSet<string>();
                while(p!=""&&visited.Add(p)){depth++;p=ordered.FirstOrDefault(x=>x.Id==p)?.ParentId??"";}
                e.Depth=depth;e.Width=width-16-Math.Min(depth,3)*24;
                e.Slab.style.width=e.Width;
                string key=contentKey(n)+":"+e.Slab.CanChoose;
                if(key!=e.ContentKey){e.Slab.Clear();build(e.Slab,n);e.ContentKey=key;}
                float oldY=e.TargetY;Reflow();if(Mathf.Abs(oldY-e.TargetY)>3&&e.Age>.4f){e.Travel=Time.realtimeSinceStartup;reordered=true;}
            }
            Reflow();
            if(focus!=trace.FocusId){focus=trace.FocusId;FreeBrowsing=false;autoFocus=true;}
            if(reset){autoFocus=true;scroll=scrollGoal=FocusY();foreach(var e in entries.Values){e.Y=e.TargetY;e.X=0;e.Opacity=1;}}
            if(animate){if(added)sound(newSequence?"drop":"insert");else if(reordered)sound("move");}
            UpdateCoins();snap=false;
        }
        private float FocusY()=>entries.TryGetValue(focus,out var e)?e.TargetY:0;
        private void Reflow()
        {
            float y=16;
            for(int i=0;i<ordered.Count;i++)
            {
                var n=ordered[i];if(!entries.TryGetValue(n.Id,out var e))continue;
                float measured=e.Slab.resolvedStyle.height;if(!float.IsNaN(measured)&&measured>60)e.Height=measured;
                e.TargetY=y;e.Slab.style.top=y;e.Slab.style.left=8+Math.Min(e.Depth,3)*24;
                var next=i+1<ordered.Count?ordered[i+1]:null;
                y+=e.Height+(next!=null&&next.ParentId!=""?14:next!=null&&Grouped(n,next)?16:52);
                if(snap)e.Y=e.TargetY;
            }
            totalHeight=y;layer.style.height=y+resolvedStyle.height;
            if(autoFocus)scrollGoal=Mathf.Max(0,FocusY()-16);
        }
        private void UpdateCoins()
        {
            var keys=new HashSet<string>();
            for(int i=0;i+1<ordered.Count;i++)if(Grouped(ordered[i],ordered[i+1]))
            {
                string key=ordered[i].Id+">"+ordered[i+1].Id;keys.Add(key);
                if(!coins.ContainsKey(key)){var c=new ActionGroupCoin(()=>sound("tick"));coins[key]=c;layer.Add(c);}
            }
            foreach(var key in coins.Keys.Where(k=>!keys.Contains(k)).ToArray()){coins[key].RemoveFromHierarchy();coins.Remove(key);}
        }
        private void Animate()
        {
            float now=Time.realtimeSinceStartup,dt=Mathf.Clamp(now-lastTime,0,.05f);lastTime=now;
            scroll=Mathf.Lerp(scroll,scrollGoal,1-Mathf.Exp(-dt*9));layer.style.translate=new Translate(0,-scroll);
            foreach(var e in entries.Values)
            {
                e.Age+=dt;e.Y=Mathf.Lerp(e.Y,e.TargetY,1-Mathf.Exp(-dt*9));e.X=Mathf.Lerp(e.X,0,1-Mathf.Exp(-dt*9));e.Opacity=Mathf.Min(1,e.Opacity+dt*4);
                float age=now-e.Travel,travel=age<.65f?Mathf.Sin(Mathf.PI*age/.65f):0;
                float sway=e.Slab.Hovered?0:Mathf.Sin(now*.85f+e.Data.Sequence)*1.4f;
                e.Slab.style.translate=new Translate(e.X+sway+travel*38,e.Y-e.TargetY);
                float size=(e.Slab.Hovered?1.012f:1)+travel*.022f;e.Slab.style.scale=new Scale(new Vector3(size,size,1));
                e.Slab.style.opacity=e.Opacity;e.Slab.MarkDirtyRepaint();
            }
            foreach(var pair in coins)
            {
                var split=pair.Key.Split('>');var a=entries[split[0]];var b=entries[split[1]];
                pair.Value.style.display=Mathf.Abs(a.Y-a.TargetY)+Mathf.Abs(b.Y-b.TargetY)<6&&a.Opacity>.95f&&b.Opacity>.95f?DisplayStyle.Flex:DisplayStyle.None;
                pair.Value.style.left=8+a.Width/2-24;pair.Value.style.top=(a.Y+a.Height+b.Y)/2-24;pair.Value.Step(dt);
            }
            links.MarkDirtyRepaint();
        }
        private void DrawLinks(MeshGenerationContext c)
        {
            var p=c.painter2D;p.lineWidth=1.8f;p.strokeColor=new Color(.68f,.51f,.92f,.8f);
            foreach(var e in entries.Values)if(e.Data.ParentId!=""&&entries.TryGetValue(e.Data.ParentId,out var parent))
            {
                float x=8+Math.Min(e.Depth,3)*24-9,y=e.Y+26;
                p.BeginPath();p.MoveTo(new Vector2(18+Math.Min(parent.Depth,3)*24,parent.Y+parent.Height-6));
                p.LineTo(new Vector2(x,y-12));p.QuadraticCurveTo(new Vector2(x,y),new Vector2(x+11,y));p.Stroke();
            }
        }
    }

    public sealed class ActionSlab : VisualElement
    {
        public bool Notched,Active,CanChoose,Resolved,Hovered;
        public Color Accent=new Color(.6f,.7f,.8f);
        public ActionSlab(Action click)
        {
            AddToClassList("action-card");style.position=Position.Absolute;style.flexShrink=0;
            RegisterCallback<PointerEnterEvent>(_=>Hovered=true);RegisterCallback<PointerLeaveEvent>(_=>Hovered=false);
            RegisterCallback<PointerUpEvent>(e=>{if(e.button==0){click();e.StopPropagation();}});
            generateVisualContent+=Draw;
        }
        private void Shape(Painter2D p,float inset)
        {
            float w=resolvedStyle.width,h=resolvedStyle.height,k=inset,m=w/2,r=27;
            p.BeginPath();p.MoveTo(new Vector2(k+12,k));
            if(Notched){p.LineTo(new Vector2(m-r,k));p.QuadraticCurveTo(new Vector2(m,k+30),new Vector2(m+r,k));}
            p.LineTo(new Vector2(w-k-12,k));p.LineTo(new Vector2(w-k,k+12));p.LineTo(new Vector2(w-k,h-k-12));p.LineTo(new Vector2(w-k-12,h-k));
            if(Notched){p.LineTo(new Vector2(m+r,h-k));p.QuadraticCurveTo(new Vector2(m,h-k-30),new Vector2(m-r,h-k));}
            p.LineTo(new Vector2(k+12,h-k));p.LineTo(new Vector2(k,h-k-12));p.LineTo(new Vector2(k,k+12));p.ClosePath();
        }
        private void Draw(MeshGenerationContext c)
        {
            var p=c.painter2D;float h=resolvedStyle.height,w=resolvedStyle.width;if(w<50||h<50)return;
            Shape(p,0);p.fillColor=new Color(.085f,.12f,.16f,.98f);p.Fill();p.strokeColor=Active?new Color(.8f,.62f,1):CanChoose?new Color(1,.81f,.43f):Hovered?Color.white:Accent;p.lineWidth=Active||CanChoose?3:2;p.Stroke();
            Shape(p,5);p.strokeColor=new Color(.54f,.59f,.62f,.45f);p.lineWidth=1;p.Stroke();
            p.strokeColor=new Color(.44f,.5f,.55f,.12f);p.lineWidth=1;
            for(int i=0;i<26;i++){float x=13+(i*71%(int)(w-30)),y=26+(i*53%(int)(h-50));p.BeginPath();p.MoveTo(new Vector2(x,y));p.LineTo(new Vector2(Mathf.Min(w-12,x+20),y-4));p.Stroke();}
            if(Active||CanChoose){p.strokeColor=Active?new Color(.76f,.5f,1,.7f):new Color(1,.8f,.4f,.7f);p.lineWidth=2;float pulse=.5f+.5f*Mathf.Sin(Time.realtimeSinceStartup*3);p.BeginPath();p.MoveTo(new Vector2(10,20));p.LineTo(new Vector2(10,45+pulse*30));p.Stroke();}
        }
    }
    public sealed class ActionGroupCoin : VisualElement
    {
        private float angle,velocity,axis;private readonly Action sound;public float Velocity=>velocity;
        public ActionGroupCoin(Action sound)
        {
            this.sound=sound;name="action-group-coin";style.position=Position.Absolute;style.width=48;style.height=48;
            RegisterCallback<PointerEnterEvent>(_=>Spin());RegisterCallback<PointerUpEvent>(e=>{if(e.button==0)Spin();e.StopPropagation();});generateVisualContent+=Draw;
        }
        private static readonly System.Random random=new System.Random(); // UI randomness never consumes rule RNG.
        public void Spin(){axis=(float)random.NextDouble()*Mathf.PI*2;velocity=(random.Next(2)==0?-1:1)*(12+(float)random.NextDouble()*7);sound();}
        public void Step(float dt){angle+=velocity*dt;velocity*=Mathf.Exp(-dt*2.1f);if(Mathf.Abs(velocity)<.1f)angle=Mathf.Lerp(angle,Mathf.Round(angle/(Mathf.PI*2))*Mathf.PI*2,1-Mathf.Exp(-dt*5));MarkDirtyRepaint();}
        private Vector2 Point(float x,float y){float u=Mathf.Cos(axis),v=Mathf.Sin(axis),a=Mathf.Cos(angle),k=1-a;return new Vector2(24+(a+u*u*k)*x+u*v*k*y,24+u*v*k*x+(a+v*v*k)*y);}
        private void Draw(MeshGenerationContext c)
        {
            var p=c.painter2D;
            foreach(float r in new[]{22f,19f,14f}){p.BeginPath();for(int i=0;i<40;i++){var v=Point(Mathf.Cos(i*Mathf.PI/20)*r,Mathf.Sin(i*Mathf.PI/20)*r);if(i==0)p.MoveTo(v);else p.LineTo(v);}p.ClosePath();p.fillColor=r==22?new Color(.51f,.43f,.29f):r==19?new Color(.82f,.74f,.51f):new Color(.22f,.3f,.34f);p.Fill();p.lineWidth=1;p.strokeColor=new Color(.94f,.87f,.68f);p.Stroke();}
            p.BeginPath();p.MoveTo(Point(0,-10));p.LineTo(Point(8,0));p.LineTo(Point(0,10));p.LineTo(Point(-8,0));p.ClosePath();p.strokeColor=new Color(.81f,.93f,.95f);p.Stroke();
        }
    }
}
