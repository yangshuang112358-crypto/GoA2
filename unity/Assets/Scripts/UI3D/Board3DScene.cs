#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Goa2.Domain;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Goa2.Presentation.UI3D
{
    // Receives ONLY a seat projection and public map. Never owns a GameSession.
    // Camera and meshes are isolated from the host scene and contain no colliders.
    public sealed partial class Board3DScene : IDisposable
    {
        private const int Layer = 30;
        public const float WallHeight = 1.05f, HeroHeight = WallHeight * 2;
        private readonly GameObject host;
        private readonly List<Object> owned = new List<Object>();
        private readonly Dictionary<Color, Material> materials = new Dictionary<Color, Material>();
        private readonly Dictionary<Hex, CellDefinition> cells;
        private readonly List<(Hex cell, float radius, float top, string text)> tokens = new List<(Hex,float,float,string)>();
        private readonly Board3DViewport state;
        public Camera Camera { get; }
        public RenderTexture? Texture { get; private set; }
        public IEnumerable<(Hex cell, float top, string text)> Labels => tokens.Select(t => (t.cell,t.top,t.text));
        public bool Disposed { get; private set; }
        public int TokenCount => tokens.Count;

        public Board3DScene(ContentCatalog catalog, GameView view, IEnumerable<Hex> legal, Hex? selected,
            IEnumerable<Hex> effectArea, Board3DViewport state)
        {
            this.state = state; cells = catalog.Cells.ToDictionary(c => c.Position);
            host = new GameObject("UI3D isolated board") { hideFlags = HideFlags.HideAndDontSave };
            // A private preview scene also prevents unrelated cameras and scene lights seeing this board.
            var scene = UnityEngine.SceneManagement.SceneManager.CreateScene("UI3D-"+Guid.NewGuid().ToString("N"));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(host,scene);
            Camera = new GameObject("UI3D camera").AddComponent<Camera>(); Camera.transform.SetParent(host.transform);
            Camera.enabled = false; Camera.orthographic = true; Camera.cullingMask = 1 << Layer;
            Camera.clearFlags = CameraClearFlags.SolidColor; Camera.backgroundColor = ColorOf("#101D28");
            Camera.nearClipPlane=.1f; Camera.farClipPlane=200;
            var hex=Own(Board3DGeometry.Prism(6,-30)); var cylinder=Own(Board3DGeometry.Prism(48,0));
            var ring=Own(Board3DGeometry.Ring(6,.91f)); var circle=Own(Board3DGeometry.Ring(48,.80f));
            var targets=new HashSet<Hex>(legal); var areas=new HashSet<Hex>(effectArea);
            foreach (var cell in catalog.Cells)
            {
                BuildTerrain(cell,hex);
                if (areas.Contains(cell.Position)) Add(ring,Board3DGeometry.World(cell.Position,.24f),Vector3.one*.81f,ColorOf("#C3A4FF"),"effect");
                if (targets.Contains(cell.Position)) Add(ring,Board3DGeometry.World(cell.Position,.27f),Vector3.one*.94f,ColorOf("#6EF4CD"),"legal");
                if (selected == cell.Position) Add(ring,Board3DGeometry.World(cell.Position,.29f),Vector3.one,ColorOf("#FFE39A"),"selected");
            }
            foreach (var unit in view.Units)
            {
                float radius=unit.Seat.HasValue ? .55f : .42f, height=unit.Seat.HasValue ? HeroHeight : .34f;
                string text=unit.Seat.HasValue
                    ? catalog.Heroes.FirstOrDefault(h=>h.Id==view.Players.FirstOrDefault(p=>p.Seat==unit.Seat)?.HeroId)?.Name ?? "英雄"
                    : unit.Kind=="heavy" ? "重" : unit.Kind=="ranged" ? "远" : "近";
                Add(cylinder,Board3DGeometry.World(unit.Position,.03f),new Vector3(radius,height,radius),ColorOf(unit.Team==Team.Blue ? "#559EDB" : "#D77C79"),"token "+unit.Id);
                Add(circle,Board3DGeometry.World(unit.Position,height+.04f),Vector3.one*radius,
                    ColorOf(unit.Seat.HasValue && unit.Seat==view.ActiveSeat ? "#FFE39A" : "#DDE8EA"),"token rim");
                tokens.Add((unit.Position,radius,height+.055f,text));
            }
            BuildWorldHud(catalog,view);
            if (!state.Initialized) { Reset();state.Zoom=1.6f; }
        }

        private T Own<T>(T value) where T:Object { value.hideFlags=HideFlags.HideAndDontSave; owned.Add(value); return value; }
        private GameObject Add(Mesh mesh,Vector3 at,Vector3 scale,Color color,string name)
        {
            var go=new GameObject(name) { layer=Layer, hideFlags=HideFlags.HideAndDontSave };
            go.transform.SetParent(host.transform,false); go.transform.localPosition=at; go.transform.localScale=scale;
            go.AddComponent<MeshFilter>().sharedMesh=mesh;
            if(!materials.TryGetValue(color,out var material))
            {
                var shader=Resources.Load<Shader>("UI3D/Board");
                if(shader==null) throw new InvalidOperationException("Missing UI3D board shader");
                material=Own(new Material(shader) {color=color}); materials.Add(color,material);
                if(name=="legal" || name=="selected") { material.SetInt("_ZTest",8);material.renderQueue=4000; }
            }
            go.AddComponent<MeshRenderer>().sharedMaterial=material;return go;
        }
        public static Color ColorOf(string html) { ColorUtility.TryParseHtmlString(html,out var c);return c; }
        private static Color RegionColor(CellDefinition cell)
        {
            if(cell.Obstacle) return ColorOf("#354551");
            switch(cell.Region)
            {
                case "blueFountain": return ColorOf("#365B76"); case "redFountain": return ColorOf("#75505B");
                case "blueNear": return ColorOf("#354F5B"); case "redNear": return ColorOf("#58494F");
                case "topGrass": case "bottomGrass": return ColorOf("#37564D");
                default: return ColorOf(cell.Lane ? "#68756A" : "#465B56");
            }
        }
        public void Reset()
        {
            var points=cells.Keys.Select(h=>Board3DGeometry.World(h)).ToList();
            state.Focus=points.Count==0 ? Vector3.zero : new Vector3((points.Min(p=>p.x)+points.Max(p=>p.x))*.5f,0,(points.Min(p=>p.z)+points.Max(p=>p.z))*.5f);
            state.Zoom=1;state.Initialized=true;
        }
        public void Render(int width,int height)
        {
            if(Disposed) return;
            width=Mathf.Clamp(width,1,4096);height=Mathf.Clamp(height,1,4096);
            if(Texture==null || Texture.width!=width || Texture.height!=height)
            {
                Camera.targetTexture=null;
                if(Texture!=null) {Texture.Release();Destroy(Texture);}
                Texture=new RenderTexture(width,height,24) {name="UI3D board output",hideFlags=HideFlags.HideAndDontSave,antiAliasing=2};
                Texture.Create();Camera.targetTexture=Texture;
            }
            Camera.aspect=(float)width/height;
            Camera.transform.rotation=Quaternion.Euler(55,state.Yaw,0);
            Camera.transform.position=state.Focus-Camera.transform.forward*80;
            // Fit all twelve orientations at zoom 1; pan does not silently change scale.
            var inverse=Quaternion.Inverse(Camera.transform.rotation);
            var points=cells.Keys.Select(h=>inverse*Board3DGeometry.World(h)).Concat(framingPoints.Select(p=>inverse*p)).ToList();
            float extentX=points.Count==0 ? 1 : (points.Max(p=>p.x)-points.Min(p=>p.x))*.5f+1.6f;
            float extentY=points.Count==0 ? 1 : (points.Max(p=>p.y)-points.Min(p=>p.y))*.5f+3;
            float fittedSize=Mathf.Max(extentY,extentX/Camera.aspect);
            Camera.orthographicSize=fittedSize/state.Zoom;
            AnimateWorldHud();Camera.Render();
        }
        public float ZoomForRegion(IEnumerable<Hex> region)
        {
            var inverse=Quaternion.Inverse(Camera.transform.rotation);
            var points=region.Select(h=>inverse*Board3DGeometry.World(h)).ToList();if(points.Count==0)return 1;
            float extent=Mathf.Max((points.Max(p=>p.y)-points.Min(p=>p.y))*.5f+4,((points.Max(p=>p.x)-points.Min(p=>p.x))*.5f+2)/Mathf.Max(.1f,Camera.aspect));
            return Mathf.Clamp(Camera.orthographicSize*state.Zoom/extent,.6f,8);
        }
        public Vector2 Project(Hex hex,Vector2 size,float height=0)
        {
            var v=Camera.WorldToViewportPoint(Board3DGeometry.World(hex,height));
            return new Vector2(v.x*size.x,(1-v.y)*size.y);
        }
        public Vector3 Ground(Vector2 pointer,Vector2 size,float height=0)
        {
            var ray=Camera.ViewportPointToRay(new Vector3(pointer.x/size.x,1-pointer.y/size.y,0));
            float distance=(height-ray.origin.y)/ray.direction.y;
            return ray.GetPoint(distance);
        }
        public CellDefinition? Hit(Vector2 pointer,Vector2 size)
        {
            if(size.x<=0 || size.y<=0 || pointer.x<0 || pointer.y<0 || pointer.x>size.x || pointer.y>size.y) return null;
            // Check visible cylinder tops first, then the exact ground hex. No physics raycasts.
            foreach(var token in tokens.OrderBy(t=>Vector3.Distance(Camera.transform.position,Board3DGeometry.World(t.cell,t.top))))
            {
                var delta=Ground(pointer,size,token.top)-Board3DGeometry.World(token.cell,token.top);
                if(delta.sqrMagnitude<=token.radius*token.radius) return cells[token.cell];
            }
            return cells.TryGetValue(Board3DGeometry.HexAt(Ground(pointer,size)),out var cell) ? cell : null;
        }
        public void Dispose()
        {
            if(Disposed) return;Disposed=true;
            var scene=host.scene;
            Camera.targetTexture=null;
            if(Texture!=null) { Texture.Release();Destroy(Texture);Texture=null; }
            host.SetActive(false);Destroy(host);foreach(var value in owned) Destroy(value);
            UnityEngine.SceneManagement.SceneManager.UnloadSceneAsync(scene);
        }
        private static void Destroy(Object value) { if(UnityEngine.Application.isPlaying) Object.Destroy(value);else Object.DestroyImmediate(value); }
    }
}
