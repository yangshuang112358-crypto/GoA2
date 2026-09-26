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
        private readonly Label compass=new Label();
        private readonly HashSet<Hex> legal;
        private readonly Action<Hex> choose;
        private bool connected=true,dragging;
        private int pointerId;
        private Vector2 lastPointer;
        public Action? ViewportChanged;
        public int RotationStep => state.Step;
        public bool Connected => connected;
        public Board3DScene? Scene => scene;
        public float HexRadius => fallback?.HexRadius ?? (scene==null ? 0 : contentRect.height/(2*scene.Camera.orthographicSize));

        public BattlefieldSurface(ContentCatalog catalog,GameView view,IEnumerable<Hex> legal,Hex? selected,
            Action<Hex> choose,Action<CellDefinition> hover,BoardViewport oldState,IEnumerable<Hex> effectArea,Board3DViewport state)
        {
            this.state=state;this.legal=new HashSet<Hex>(legal);this.choose=choose;
            name="hex-board";AddToClassList("hex-board"); style.overflow=Overflow.Hidden;
            if(!state.Enabled)
            {
                fallback=new HexBoard(catalog,view,legal,selected,h=> {if(connected) choose(h);},hover,oldState,effectArea);
                fallback.ViewportChanged=()=>ViewportChanged?.Invoke();Add(fallback);return;
            }
            image=new Image {pickingMode=PickingMode.Ignore,scaleMode=ScaleMode.StretchToFill};
            image.StretchToParentSize();Add(image);
            compass.pickingMode=PickingMode.Ignore;compass.style.position=Position.Absolute;compass.style.left=8;compass.style.top=6;
            compass.style.fontSize=20;compass.style.color=Board3DScene.ColorOf("#DEE8EC");
            compass.style.backgroundColor=new Color(.06f,.11f,.16f,.88f);Add(compass);
            RegisterCallback<AttachToPanelEvent>(_=>
            {
                scene=new Board3DScene(catalog,view,this.legal,connected ? selected : null,effectArea,state);
                foreach(var token in scene.Labels)
                {
                    var label=new Label(token.text) {pickingMode=PickingMode.Ignore};label.AddToClassList("unit-label");
                    label.style.color=Color.white;label.style.unityTextOutlineColor=new Color(.04f,.07f,.12f);label.style.unityTextOutlineWidth=.45f;
                    Add(label);labels.Add((label,token.cell,token.top));
                }
                compass.BringToFront();Repaint();
            });
            RegisterCallback<DetachFromPanelEvent>(_=>
            {
                image.image=null;scene?.Dispose();scene=null;
                foreach(var entry in labels) entry.label.RemoveFromHierarchy();labels.Clear();dragging=false;
            });
            RegisterCallback<GeometryChangedEvent>(_=>Repaint());
            RegisterCallback<PointerDownEvent>(e=>
            {
                if(e.button==1 || e.button==2) {dragging=true;pointerId=e.pointerId;lastPointer=e.localPosition;this.CapturePointer(pointerId);e.StopPropagation();return;}
                if(e.button==0) SelectAt(e.localPosition);
            });
            RegisterCallback<PointerMoveEvent>(e=>
            {
                if(scene==null) return;
                if(dragging && e.pointerId==pointerId)
                {state.Focus+=scene.Ground(lastPointer,contentRect.size)-scene.Ground(e.localPosition,contentRect.size);lastPointer=e.localPosition;Repaint();e.StopPropagation();return;}
                var cell=scene.Hit(e.localPosition,contentRect.size);if(cell!=null) hover(cell);
            });
            RegisterCallback<PointerUpEvent>(e=> {if(dragging && pointerId==e.pointerId) {dragging=false;this.ReleasePointer(pointerId);e.StopPropagation();}});
            RegisterCallback<PointerCaptureOutEvent>(_=>dragging=false);
            RegisterCallback<WheelEvent>(e=> {Zoom(e.localMousePosition,Mathf.Pow(1.12f,-e.delta.y/3));e.StopPropagation();});
        }
        public bool SelectAt(Vector2 local)
        {
            if(!connected || dragging || scene==null) return false;
            var cell=scene.Hit(local,contentRect.size);
            if(cell==null || !legal.Contains(cell.Position)) return false;
            choose(cell.Position);return true;
        }
        // A host adapter must clear its own preselection too, and construct a new surface from
        // the latest own Snapshot before enabling submissions after reconnect.
        public void SetConnected(bool value) {connected=value;Repaint();}
        public void Rotate(int direction) {if(!state.Enabled) return;state.Step=Board3DGeometry.WrapStep(state.Step+direction);Repaint();}
        public void ResetView() {if(fallback!=null) fallback.ResetView();else {scene?.Reset();Repaint();}}
        public void FocusAt(Hex hex) {if(fallback!=null) fallback.FocusAt(hex);else {state.Focus=Board3DGeometry.World(hex);state.Zoom=3;Repaint();}}
        public void ZoomAtCenter(float factor) {if(fallback!=null) fallback.ZoomAtCenter(factor);else Zoom(contentRect.center,factor);}
        public Vector2 PanelCenter(Hex hex) => fallback!=null ? fallback.PanelCenter(hex) : this.LocalToWorld(scene?.Project(hex,contentRect.size) ?? Vector2.zero);
        private void Zoom(Vector2 pointer,float factor)
        {
            if(scene==null || contentRect.width<=0 || contentRect.height<=0) return;
            var before=scene.Ground(pointer,contentRect.size);state.Zoom=Mathf.Clamp(state.Zoom*factor,.6f,8);Repaint();
            state.Focus+=before-scene.Ground(pointer,contentRect.size);Repaint();
        }
        private void Repaint()
        {
            if(scene==null || image==null || contentRect.width<=0 || contentRect.height<=0) return;
            scene.Render(Mathf.CeilToInt(contentRect.width),Mathf.CeilToInt(contentRect.height));image.image=scene.Texture;
            foreach(var entry in labels)
            {
                var p=scene.Project(entry.cell,contentRect.size,entry.top);
                float pixels=contentRect.height/(2*scene.Camera.orthographicSize);
                float size=Mathf.Clamp(pixels*.65f,10,26);
                entry.label.style.display=pixels<12 && !char.IsDigit(entry.label.text[entry.label.text.Length-1]) ? DisplayStyle.None : DisplayStyle.Flex;
                entry.label.style.left=p.x-32;entry.label.style.top=p.y-size*.7f;
                entry.label.style.width=64;entry.label.style.height=size*1.4f;entry.label.style.fontSize=size;
            }
            compass.text=connected ? $"Q ↶  {state.Step*30}°  ↷ E" : "连接已断开 · 禁止选择";
            ViewportChanged?.Invoke();
        }
    }
}
