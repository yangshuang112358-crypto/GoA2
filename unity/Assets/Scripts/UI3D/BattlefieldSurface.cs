#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Goa2.Domain;
using UnityEngine;
using UnityEngine.UIElements;

namespace Goa2.Presentation.UI3D
{
    // Board-only adapter boundary: immutable-in-use seat view + legal cells + selection callback.
    // The host owns Pending/intent confirmation; this class cannot submit a command.
    public sealed class BattlefieldSurface : VisualElement
    {
        private readonly Board3DViewport state;
        private readonly HexBoard? fallback;
        private readonly Image? image;
        private Board3DScene? scene;
        private readonly List<(Label label,Hex cell,float top)> labels=new List<(Label,Hex,float)>();
        private readonly List<(Vector2 from,Vector2 to)> leaders=new List<(Vector2,Vector2)>();
        private VisualElement? leaderLayer;
        private readonly HashSet<Hex> legal;
        private readonly Action<Hex> choose;
        private bool connected=true,dragging;
        private int pointerId;
        private Vector2 lastPointer;
        private float lastAnimationTime;
        public Action? ViewportChanged;
        public Action? ManualPan;
        public Action<int?,Vector2>? HeroHover;
        public Action<int>? HeroClick;
        public Action? EmptyClick;
        private readonly List<(HeroPlate plate,Hex cell)> heroPlates=new List<(HeroPlate,Hex)>();
        public int RotationStep => state.Step;
        public bool Connected => connected;
        public Board3DScene? Scene => scene;
        public float HexRadius => fallback?.HexRadius ?? (scene==null ? 0 : contentRect.height/(2*scene.Camera.orthographicSize));

        public BattlefieldSurface(ContentCatalog catalog,GameView view,IEnumerable<Hex> legal,Hex? selected,
            Action<Hex> choose,Action<CellDefinition> hover,BoardViewport oldState,IEnumerable<Hex> effectArea,Board3DViewport state,int ownSeat=0)
        {
            this.state=state;this.legal=new HashSet<Hex>(legal);this.choose=choose;
            name="hex-board";AddToClassList("hex-board"); style.overflow=Overflow.Hidden;
            if(!state.Enabled)
            {
                fallback=new HexBoard(catalog,view,legal,selected,h=> {if(connected) choose(h);},hover,oldState,effectArea,ownSeat);
                fallback.EmptyClick=()=>EmptyClick?.Invoke();fallback.HeroClick=who=>HeroClick?.Invoke(who);fallback.HeroHover=(who,at)=>HeroHover?.Invoke(who,at);fallback.ManualPan=()=>ManualPan?.Invoke();fallback.ViewportChanged=()=>ViewportChanged?.Invoke();Add(fallback);return;
            }
            image=new Image {pickingMode=PickingMode.Ignore,scaleMode=ScaleMode.StretchToFill};
            image.StretchToParentSize();Add(image);
            leaderLayer=new VisualElement{pickingMode=PickingMode.Ignore};leaderLayer.StretchToParentSize();Add(leaderLayer);
            leaderLayer.generateVisualContent+=c=>{var p=c.painter2D;p.strokeColor=new Color(.8f,.84f,.88f,.7f);p.lineWidth=1;foreach(var line in leaders){p.BeginPath();p.MoveTo(line.from);p.LineTo(line.to);p.Stroke();}};
            schedule.Execute(()=>
            {
                float now=Time.realtimeSinceStartup;
                bool moved=state.Advance(now-lastAnimationTime);
                if(scene!=null)Repaint(moved);
                lastAnimationTime=now;
            }).Every(16);

            RegisterCallback<AttachToPanelEvent>(_=>
            {
                scene=new Board3DScene(catalog,view,this.legal,connected ? selected : null,effectArea,state);
                foreach(var token in scene.Labels)
                {
                    var hero=view.Units.FirstOrDefault(u=>u.Seat.HasValue && u.Position==token.cell);
                    if(hero!=null) {var plate=new HeroPlate(catalog,view,view.Players.Single(p=>p.Seat==hero.Seat),ownSeat);plate.pickingMode=PickingMode.Position;var heroCell=token.cell;int heroSeat=hero.Seat!.Value;
                        plate.RegisterCallback<PointerDownEvent>(e=>{if(e.button==1){HeroHover?.Invoke(heroSeat,e.position);e.StopPropagation();}});
                        plate.RegisterCallback<PointerDownEvent>(e=>{if(e.button==0){if(connected && this.legal.Contains(heroCell))choose(heroCell);else HeroClick?.Invoke(heroSeat);e.StopPropagation();}});
                        Add(plate);heroPlates.Add((plate,token.cell));continue;}
                    var label=new Label(token.text.Replace("·","\n")) {pickingMode=PickingMode.Ignore};label.AddToClassList("unit-label");
                    label.style.color=Color.white;label.style.unityTextOutlineColor=new Color(.04f,.07f,.12f);label.style.unityTextOutlineWidth=.45f;
                    Add(label);labels.Add((label,token.cell,token.top));
                }
                lastAnimationTime=Time.realtimeSinceStartup;Repaint();
            });
            RegisterCallback<DetachFromPanelEvent>(_=>
            {
                image.image=null;scene?.Dispose();scene=null;
                foreach(var entry in labels) entry.label.RemoveFromHierarchy();labels.Clear();foreach(var plate in heroPlates)plate.plate.RemoveFromHierarchy();heroPlates.Clear();dragging=false;
            });
            RegisterCallback<GeometryChangedEvent>(_=>Repaint());
            RegisterCallback<PointerDownEvent>(e=>
            {
                if(e.button==1 && scene!=null) {var hit=scene.Hit(e.localPosition,contentRect.size);var hero=hit==null ? null : view.Units.FirstOrDefault(u=>u.Position==hit.Position && u.Seat.HasValue);if(hero!=null){HeroHover?.Invoke(hero.Seat,e.position);e.StopPropagation();return;}}
                if(e.button==1 || e.button==2) {dragging=true;pointerId=e.pointerId;lastPointer=e.localPosition;this.CapturePointer(pointerId);e.StopPropagation();return;}
                if(e.button==0) {if(!SelectAt(e.localPosition) && scene!=null){var hit=scene.Hit(e.localPosition,contentRect.size);var hero=hit==null?null:view.Units.FirstOrDefault(u=>u.Position==hit.Position && u.Seat.HasValue);if(hero!=null)HeroClick?.Invoke(hero.Seat!.Value);else if(hit==null || !view.Units.Any(u=>u.Position==hit.Position))EmptyClick?.Invoke();}}
            });
            RegisterCallback<PointerMoveEvent>(e=>
            {
                if(scene==null) return;
                if(dragging && e.pointerId==pointerId)
                {if(((Vector2)e.localPosition-lastPointer).sqrMagnitude>0) ManualPan?.Invoke();state.Focus+=scene.Ground(lastPointer,contentRect.size)-scene.Ground(e.localPosition,contentRect.size);lastPointer=e.localPosition;Repaint();e.StopPropagation();return;}
                var cell=scene.Hit(e.localPosition,contentRect.size);if(cell!=null) hover(cell);

            });

            RegisterCallback<PointerUpEvent>(e=> {if(dragging && pointerId==e.pointerId) {dragging=false;this.ReleasePointer(pointerId);e.StopPropagation();}});
            RegisterCallback<PointerCaptureOutEvent>(_=>dragging=false);
            RegisterCallback<WheelEvent>(e=> {Zoom(e.localMousePosition,Mathf.Pow(1.12f,-e.delta.y/3));e.StopPropagation();});
        }
        public Vector2 ProjectHero(Hex cell)=>fallback!=null ? this.WorldToLocal(fallback.PanelCenter(cell)) : scene?.Project(cell,contentRect.size,1) ?? contentRect.size*.5f;
        public bool SelectAt(Vector2 local)
        {
            if(!connected || dragging || scene==null) return false;
            var cell=scene.Hit(local,contentRect.size);
            if(cell==null) return false;
            if(!legal.Contains(cell.Position))
            {
                // Tall pieces can cover an allowed ground hex. A non-target piece must
                // not steal that click; still use only the host-supplied legal set.
                var ground=Board3DGeometry.HexAt(scene.Ground(local,contentRect.size));
                if(!legal.Contains(ground)) return false;
                choose(ground);return true;
            }
            choose(cell.Position);return true;
        }
        // A host adapter must clear its own preselection too, and construct a new surface from
        // the latest own Snapshot before enabling submissions after reconnect.
        public void SetConnected(bool value) {connected=value;Repaint();}
        public void Rotate(int direction) {if(!state.Enabled) return;state.Rotate(direction);lastAnimationTime=Time.realtimeSinceStartup;Repaint();}
        public void FollowAt(Vector3 target,float? zoom) {if(fallback!=null) fallback.FollowAt(new Vector2(target.x,-target.z),zoom);else state.Follow(target,zoom);}
        public void StopFollowing() {state.StopFollowing();fallback?.StopFollowing();}
        public void ResetView() {if(fallback!=null) fallback.ResetView();else {scene?.Reset();Repaint();}}
        public void FocusAt(Hex hex) {if(fallback!=null) fallback.FocusAt(hex);else {state.Focus=Board3DGeometry.World(hex);state.Zoom=3;Repaint();}}
        public void ZoomAtCenter(float factor) {if(fallback!=null) fallback.ZoomAtCenter(factor);else Zoom(contentRect.center,factor);}
        public Vector2 PanelCenter(Hex hex) => fallback!=null ? fallback.PanelCenter(hex) : this.LocalToWorld(scene?.Project(hex,contentRect.size) ?? Vector2.zero);
        private void Zoom(Vector2 pointer,float factor)
        {
            if(scene==null || contentRect.width<=0 || contentRect.height<=0) return;
            state.ManualZoom();var before=scene.Ground(pointer,contentRect.size);state.Zoom=Mathf.Clamp(state.Zoom*factor,.6f,8);Repaint();
            state.Focus+=before-scene.Ground(pointer,contentRect.size);Repaint();
        }
        private void Repaint(bool notify=true)
        {
            if(scene==null || image==null || contentRect.width<=0 || contentRect.height<=0) return;
            scene.Render(Mathf.CeilToInt(contentRect.width),Mathf.CeilToInt(contentRect.height));image.image=scene.Texture;
            var placed=new List<Rect>();leaders.Clear();
            foreach(var entry in heroPlates.OrderByDescending(e=>scene.Project(e.cell,contentRect.size,Board3DScene.HeroHeight).y)) {
                var p=scene.Project(entry.cell,contentRect.size,Board3DScene.HeroHeight+.12f);
                bool visible=p.x>=0 && p.x<=contentRect.width && p.y>=0 && p.y<=contentRect.height;
                entry.plate.style.display=visible ? DisplayStyle.Flex : DisplayStyle.None;if(!visible)continue;
                var offsets=new List<Vector2>{new Vector2(-112,-94),new Vector2(-234,-65),new Vector2(10,-65),new Vector2(-112,8),new Vector2(-112,-190)};
                foreach(var occupied in placed) {
                    offsets.Add(new Vector2(occupied.x-226-p.x,-94));offsets.Add(new Vector2(occupied.xMax+2-p.x,-94));
                    offsets.Add(new Vector2(-112,occupied.y-94-p.y));offsets.Add(new Vector2(-112,occupied.yMax+2-p.y));
                }
                Rect rect=default;float best=float.PositiveInfinity;
                foreach(var offset in offsets) {
                    var candidate=new Rect(Mathf.Clamp(p.x+offset.x,0,Mathf.Max(0,contentRect.width-224)),Mathf.Clamp(p.y+offset.y,0,Mathf.Max(0,contentRect.height-92)),224,92);
                    float cost=placed.Count(r=>r.Overlaps(candidate))*100000+Vector2.Distance(new Vector2(candidate.center.x,candidate.yMax),p);
                    if(cost<best) {best=cost;rect=candidate;}
                }
                leaders.Add((p,new Vector2(Mathf.Clamp(p.x,rect.x,rect.xMax),Mathf.Clamp(p.y,rect.y,rect.yMax))));
                placed.Add(rect);entry.plate.style.left=rect.x;entry.plate.style.top=rect.y;
            }
            leaderLayer?.MarkDirtyRepaint();
            foreach(var entry in labels)
            {
                var p=scene.Project(entry.cell,contentRect.size,entry.top);
                float pixels=contentRect.height/(2*scene.Camera.orthographicSize);
                float size=Mathf.Clamp(pixels*.65f,10,26);
                bool hero=entry.top>Board3DScene.WallHeight;
                bool offscreen=p.x<0 || p.y<0 || p.x>contentRect.width || p.y>contentRect.height;
                entry.label.style.display=offscreen || pixels<12 && !hero ? DisplayStyle.None : DisplayStyle.Flex;
                var lines=entry.label.text.Split('\n');
                float width=hero ? Mathf.Max(70,lines.Max(line=>line.Length)*size+12) : 40;
                float height=size*1.4f*lines.Length;
                entry.label.style.left=Mathf.Clamp(p.x-width/2,0,Mathf.Max(0,contentRect.width-width));
                entry.label.style.top=Mathf.Clamp(p.y-height/2,0,Mathf.Max(0,contentRect.height-height));
                entry.label.style.width=width;entry.label.style.height=height;entry.label.style.fontSize=size;
            }
            if(notify)ViewportChanged?.Invoke();
        }
    }
}
