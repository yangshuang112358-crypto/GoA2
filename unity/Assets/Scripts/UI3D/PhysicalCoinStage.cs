#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Goa2.Domain;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;
namespace Goa2.Presentation.UI3D
{
    // One normalized, isolated physics arena. Only the authenticated host calls Simulate.
    // Viewers interpolate authored frames; no local result is produced on their machines.
    public sealed class PhysicalCoinStage:IDisposable
    {
        private readonly Scene scene;
        private readonly PhysicsScene physics;
        private readonly GameObject root;
        private readonly Rigidbody body;
        private readonly Camera camera;
        private readonly List<Object> owned=new List<Object>();
        private CoinMotion? remote;
        private float accumulator,elapsed,stable,sendAt,parkAt=-1,parkDuration=1.8f;
        private Vector3 parkFrom;
        private Quaternion parkRotation,parkTo;
        private long sequence;
        private bool started,reported;
        public readonly string TossId;
        public readonly RenderTexture Texture;
        public bool Finishing=>parkAt>=0;
        public float ParkProgress=>parkAt<0?0:Mathf.Clamp01((Time.realtimeSinceStartup-parkAt)/parkDuration);
        public bool Complete=>Finishing && ParkProgress>=1;
        public Action<CoinMotion>? FrameReady;
        public Action<Team,Quaternion>? Settled;
        public Action? Stuck;
        public PhysicalCoinStage(string tossId)
        {
            TossId=tossId;
            scene=SceneManager.CreateScene("coin-physics-"+Guid.NewGuid().ToString("N"),new CreateSceneParameters(LocalPhysicsMode.Physics3D));physics=scene.GetPhysicsScene();
            root=new GameObject("Isolated physical coin stage"){hideFlags=HideFlags.HideAndDontSave};SceneManager.MoveGameObjectToScene(root,scene);
            var coin=new GameObject("physical decision coin");coin.transform.SetParent(root.transform,false);
            CreateArt(coin.transform);
            var collider=coin.AddComponent<MeshCollider>();collider.sharedMesh=DiskCollider();collider.convex=true;
            var material=new PhysicsMaterial("minted coin collision"){dynamicFriction=.45f,staticFriction=.6f,bounciness=.28f,frictionCombine=PhysicsMaterialCombine.Average,bounceCombine=PhysicsMaterialCombine.Average};owned.Add(material);collider.material=material;
            body=coin.AddComponent<Rigidbody>();body.mass=.18f;body.linearDamping=.14f;body.angularDamping=.18f;body.maxAngularVelocity=65;body.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;body.isKinematic=true;
            Boundary("transparent landing plane",new Vector3(0,-.1f,0),new Vector3(12,.2f,8));
            Boundary("left",new Vector3(-6,2,0),new Vector3(.2f,5,8));Boundary("right",new Vector3(6,2,0),new Vector3(.2f,5,8));
            Boundary("front",new Vector3(0,2,-4),new Vector3(12,5,.2f));Boundary("back",new Vector3(0,2,4),new Vector3(12,5,.2f));
            camera=new GameObject("coin camera").AddComponent<Camera>();camera.transform.SetParent(root.transform,false);camera.enabled=false;camera.orthographic=true;camera.orthographicSize=5.2f;
            camera.transform.rotation=Quaternion.Euler(68,0,0);camera.transform.position=new Vector3(0,1,0)-camera.transform.forward*20;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.clear;camera.cullingMask=1<<29;camera.nearClipPlane=.1f;camera.farClipPlane=50;
            Texture=new RenderTexture(1024,768,24){name="physical coin transparent output",antiAliasing=2,hideFlags=HideFlags.HideAndDontSave};Texture.Create();camera.targetTexture=Texture;camera.aspect=4f/3;
            body.position=new Vector3(0,1,0);body.rotation=Quaternion.Euler(0,0,0);
        }
        private Mesh DiskCollider()
        {
            var vertices=new List<Vector3>();var triangles=new List<int>();const int n=32;
            for(int side=0;side<2;side++)for(int i=0;i<n;i++){float a=i*2*Mathf.PI/n;vertices.Add(new Vector3(Mathf.Cos(a)*.8f,side==0?-.25f:.25f,Mathf.Sin(a)*.8f));}
            for(int i=0;i<n;i++){int j=(i+1)%n;triangles.AddRange(new[]{i,j,i+n,j,j+n,i+n});if(i>0&&i<n-1)triangles.AddRange(new[]{0,i+1,i,n,n+i,n+i+1});}
            var mesh=new Mesh{name="32 sided convex minted disc"};mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();owned.Add(mesh);return mesh;
        }
        private void Boundary(string name,Vector3 position,Vector3 scale)
        {var go=new GameObject(name);go.transform.SetParent(root.transform,false);go.transform.localPosition=position;go.AddComponent<BoxCollider>().size=scale;}
        private void CreateArt(Transform parent)
        {
            var asset=Resources.Load<GameObject>("UI3D/DecisionCoin");
            if(asset==null)throw new InvalidOperationException("DecisionCoin asset missing");
            var art=Object.Instantiate(asset,parent,false);art.transform.localScale=Vector3.one;art.transform.localPosition=Vector3.zero;art.transform.localRotation=asset.transform.localRotation;
            var renderers=art.GetComponentsInChildren<Renderer>();var bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);
            float factor=1.6f/Mathf.Max(bounds.size.x,bounds.size.z);art.transform.localScale=Vector3.one*factor;art.transform.localPosition=-bounds.center*factor;
            foreach(var t in art.GetComponentsInChildren<Transform>()){t.gameObject.layer=29;t.gameObject.hideFlags=HideFlags.HideAndDontSave;}
            foreach(var r in renderers)r.sharedMaterials=r.sharedMaterials.Select(source=>{
                string name=source.name;bool red=name.StartsWith("RedGem"),blue=name.StartsWith("BlueGem"),dark=name.StartsWith("Oxidized");
                var m=new Material(Resources.Load<Shader>("UI3D/Coin")){color=Board3DScene.ColorOf(red?"#C82642":blue?"#236BD6":dark?"#493019":name.StartsWith("Polished")?"#E6B950":"#BA791D")};
                m.SetFloat("_Metallic",red||blue?.2f:.92f);m.SetFloat("_Roughness",red||blue?.12f:dark?.62f:.24f);m.SetFloat("_Gem",red||blue?1:0);owned.Add(m);return m;
            }).ToArray();
        }
        public void Resume(CoinMotion frame)
        {
            if(frame.TossId!=TossId)return;
            started=true;elapsed=frame.Time;sequence=frame.Sequence;Apply(frame);body.isKinematic=false;
            body.linearVelocity=V(frame.Velocity);body.angularVelocity=V(frame.AngularVelocity);sendAt=elapsed;
        }
        public void AcceptFrame(CoinMotion frame)
        {
            if(frame.TossId!=TossId || remote!=null && frame.Sequence<=remote.Sequence)return;
            remote=frame;started=true;body.isKinematic=true;
        }
        public void Park(Team side,string finalPose="")
        {
            if(Finishing)return;
            body.isKinematic=true;parkAt=Time.realtimeSinceStartup;parkFrom=body.position;parkRotation=body.rotation;
            parkTo=side==Team.Red?Quaternion.identity:Quaternion.Euler(180,0,0);
            // Authoritative final pose survives reconnect even if the last motion frame was lost.
            var q=finalPose.Split(',');if(q.Length==4 && q.All(x=>float.TryParse(x,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out _)))
                parkRotation=new Quaternion(Parse(q[0]),Parse(q[1]),Parse(q[2]),Parse(q[3]));
        }
        public void DisplaySide(Team side,bool animate)
        {
            body.isKinematic=true;parkFrom=body.position;parkRotation=body.rotation;parkTo=side==Team.Red?Quaternion.identity:Quaternion.Euler(180,0,0);
            parkDuration=.32f;parkAt=Time.realtimeSinceStartup-(animate?0:1);
        }
        private static float Parse(string s)=>float.Parse(s,System.Globalization.CultureInfo.InvariantCulture);
        public void Tick(bool simulate,bool allowed,float delta=-1)
        {
            if(Finishing)
            {
                float t=ParkProgress,ease=1-Mathf.Pow(1-t,3);SetPose(Vector3.Lerp(parkFrom,new Vector3(0,.25f,0),ease),Quaternion.Slerp(parkRotation,parkTo,ease));
            }
            else if(simulate && allowed && !reported)
            {
                if(!started)
                {
                    started=true;body.isKinematic=false;body.position=new Vector3(UnityEngine.Random.Range(-1.8f,1.8f),3.4f,-.8f);
                    body.rotation=UnityEngine.Random.rotationUniform;body.linearVelocity=new Vector3(UnityEngine.Random.Range(-1.2f,1.2f),3.5f,UnityEngine.Random.Range(-.5f,1.8f));
                    body.angularVelocity=new Vector3(UnityEngine.Random.Range(12f,22f),UnityEngine.Random.Range(-5f,5f),UnityEngine.Random.Range(-10f,10f));
                }
                accumulator+=Mathf.Min(delta<0?Time.unscaledDeltaTime:delta,.08f);
                while(accumulator>=.02f)
                {
                    physics.Simulate(.02f);elapsed+=.02f;accumulator-=.02f;
                    bool still=body.linearVelocity.sqrMagnitude<.015f && body.angularVelocity.sqrMagnitude<.015f && body.position.y<.45f;
                    stable=still?stable+.02f:0;
                }
                if(elapsed>=sendAt){EmitFrame();sendAt=elapsed+.075f;}
                float up=Vector3.Dot(body.rotation*Vector3.up,Vector3.up);
                if(stable>.65f && Mathf.Abs(up)>.85f)
                {EmitFrame();reported=true;Settled?.Invoke(up>0?Team.Red:Team.Blue,body.rotation);}
                else if(elapsed>=18){EmitFrame();reported=true;Stuck?.Invoke();}
            }
            else if(remote!=null && !simulate)
            {SetPose(Vector3.Lerp(body.position,V(remote.Position),1-Mathf.Exp(-Time.unscaledDeltaTime*18)),Quaternion.Slerp(body.rotation,Q(remote.Rotation),1-Mathf.Exp(-Time.unscaledDeltaTime*18)));}
            camera.Render();
        }
        public void RetryResult(){reported=false;}
        // Explicit local sandbox sample only; never determines a real toss result.
        public void PreviewStuck(){started=true;reported=true;body.isKinematic=true;SetPose(new Vector3(0,.8f,0),Quaternion.Euler(0,0,90));}
        public Bounds ArtBounds {get{var renderers=body.GetComponentsInChildren<Renderer>();var b=renderers[0].bounds;foreach(var r in renderers)b.Encapsulate(r.bounds);return b;}}
        public void SetParkingFraming(float progress)
        {
            camera.orthographicSize=Mathf.Lerp(5.2f,1.05f,progress);
            camera.transform.position=Vector3.up*Mathf.Lerp(1,.25f,progress)-camera.transform.forward*20;
        }
        private void SetPose(Vector3 position,Quaternion rotation){body.position=position;body.rotation=rotation;body.transform.SetPositionAndRotation(position,rotation);}
        private void Apply(CoinMotion f){SetPose(V(f.Position),Q(f.Rotation));}
        private void EmitFrame()=>FrameReady?.Invoke(new CoinMotion{TossId=TossId,Sequence=++sequence,Time=elapsed,Position=A(body.position),Rotation=new[]{body.rotation.x,body.rotation.y,body.rotation.z,body.rotation.w},Velocity=A(body.linearVelocity),AngularVelocity=A(body.angularVelocity)});
        private static float[] A(Vector3 v)=>new[]{v.x,v.y,v.z};
        private static Vector3 V(float[] v)=>new Vector3(v[0],v[1],v[2]);
        private static Quaternion Q(float[] v)=>new Quaternion(v[0],v[1],v[2],v[3]);
        public void Dispose()
        {
            camera.targetTexture=null;Texture.Release();Destroy(Texture);Destroy(root);foreach(var item in owned)Destroy(item);if(scene.isLoaded)SceneManager.UnloadSceneAsync(scene);
        }
        private static void Destroy(Object o){if(UnityEngine.Application.isPlaying)Object.Destroy(o);else Object.DestroyImmediate(o);}
    }
}
