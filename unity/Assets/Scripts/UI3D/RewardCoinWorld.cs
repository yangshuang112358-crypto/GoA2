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
    // Separate local physics: coins can bounce off terrain, never move game units or decide rewards.
    public sealed class RewardCoinWorld:IDisposable
    {
        private sealed class Coin{public Rigidbody Body=null!;public CombatPresentationTimeline.Reward Reward=null!;public Vector3 HomeFrom;public bool Homing;public Vector3 Scale;}
        private readonly List<Coin> coins=new List<Coin>();private readonly HashSet<long> spawned=new HashSet<long>();
        private readonly List<Object> owned=new List<Object>();private readonly GameObject root;private readonly Scene scene;private readonly PhysicsScene physics;
        private readonly ContentCatalog catalog;private readonly CombatPresentationTimeline timeline;private float accumulator;
        public int Count=>coins.Count;
        public RewardCoinWorld(ContentCatalog catalog,CombatPresentationTimeline timeline)
        {
            this.catalog=catalog;this.timeline=timeline;
            scene=SceneManager.CreateScene("reward-physics-"+Guid.NewGuid().ToString("N"),new CreateSceneParameters(LocalPhysicsMode.Physics3D));physics=scene.GetPhysicsScene();
            root=new GameObject("Reward coins physical scene"){hideFlags=HideFlags.HideAndDontSave};SceneManager.MoveGameObjectToScene(root,scene);
            var floor=new GameObject("reward ground");floor.transform.SetParent(root.transform,false);floor.transform.position=Vector3.down*.12f;floor.AddComponent<BoxCollider>().size=new Vector3(80,.2f,80);
            foreach(var cell in catalog.Cells.Where(c=>c.Obstacle))
            {
                var wall=new GameObject("reward-only terrain collider");wall.transform.SetParent(root.transform,false);wall.transform.position=Board3DGeometry.World(cell.Position);
                var mesh=Board3DGeometry.Prism(6,-30);owned.Add(mesh);var collider=wall.AddComponent<MeshCollider>();collider.sharedMesh=mesh;collider.convex=true;
                wall.transform.localScale=new Vector3(.95f,Board3DScene.WallHeight,.95f);
            }
        }
        public void Tick(GameView view,float now)
        {
            foreach(var reward in timeline.Rewards.Where(r=>r.Start<=now && r.Arrival>now))
                if(spawned.Add(reward.Id))for(int i=0;i<Mathf.Min(reward.Amount,12) && coins.Count<64;i++)Spawn(reward,i);
            accumulator+=Mathf.Min(Time.unscaledDeltaTime,.08f);while(accumulator>=.02f){physics.Simulate(.02f);accumulator-=.02f;}
            for(int i=coins.Count-1;i>=0;i--)
            {
                var coin=coins[i];float age=now-coin.Reward.Start;
                if(age>=3.8f || !timeline.Rewards.Contains(coin.Reward)){Destroy(coin.Body.gameObject);coins.RemoveAt(i);continue;}
                if(age<3)continue;
                if(!coin.Homing){coin.Homing=true;coin.HomeFrom=coin.Body.position;coin.Body.isKinematic=true;coin.Body.detectCollisions=false;}
                var hero=view.Units.FirstOrDefault(u=>u.Seat==coin.Reward.Seat);
                var team=view.Players.First(p=>p.Seat==coin.Reward.Seat).Team;
                var fallback=catalog.Cells.Where(c=>c.Spawn==(team==Team.Blue?"blueHeroSpawn":"redHeroSpawn")).Select(c=>Board3DGeometry.World(c.Position)).ToList();
                var to=hero!=null?Board3DGeometry.World(hero.Position,Board3DScene.HeroHeight*.72f):fallback.Count>0?fallback.Aggregate(Vector3.zero,(a,b)=>a+b)/fallback.Count+Vector3.up:Vector3.up;
                float t=Mathf.Clamp01((age-3)/.8f),ease=t*t;
                var at=Vector3.Lerp(coin.HomeFrom,to,ease)+Vector3.up*Mathf.Sin(t*Mathf.PI)*.6f;
                var rotation=Quaternion.AngleAxis(Time.unscaledDeltaTime*400,Vector3.up)*coin.Body.transform.rotation;
                coin.Body.position=at;coin.Body.rotation=rotation;coin.Body.transform.SetPositionAndRotation(at,rotation);coin.Body.transform.localScale=coin.Scale*Mathf.Lerp(1,.03f,ease);
            }
        }
        private void Spawn(CombatPresentationTimeline.Reward reward,int index)
        {
            var asset=Resources.Load<GameObject>("UI3D/GoldCoins/C-WaveEye");if(asset==null)return;
            var go=new GameObject("reward "+reward.Id+" coin "+index){layer=30,hideFlags=HideFlags.HideAndDontSave};go.transform.SetParent(root.transform,false);
            var art=Object.Instantiate(asset,go.transform,false);art.transform.localScale=Vector3.one;art.transform.localPosition=Vector3.zero;
            var renderers=art.GetComponentsInChildren<Renderer>();var bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);
            float scale=.965f/Mathf.Max(bounds.size.x,bounds.size.z);art.transform.localScale=Vector3.one*scale;art.transform.localPosition=-bounds.center*scale;
            foreach(var t in art.GetComponentsInChildren<Transform>()){t.gameObject.layer=30;t.gameObject.hideFlags=HideFlags.HideAndDontSave;}
            foreach(var r in renderers)r.sharedMaterials=r.sharedMaterials.Select(m=>{
                bool dark=m.name.StartsWith("Oxidized");var mat=new Material(Resources.Load<Shader>("UI3D/Coin")){color=Board3DScene.ColorOf(dark?"#654221":"#E2AE49")};mat.SetFloat("_Metallic",.9f);mat.SetFloat("_Roughness",dark?.55f:.24f);owned.Add(mat);return mat;
            }).ToArray();
            var mesh=Board3DGeometry.Prism(24,0);var vertices=mesh.vertices;for(int i=0;i<vertices.Length;i++)vertices[i]=new Vector3(vertices[i].x*.4825f,(vertices[i].y-.5f)*.13f,vertices[i].z*.4825f);mesh.vertices=vertices;mesh.RecalculateBounds();owned.Add(mesh);
            var collider=go.AddComponent<MeshCollider>();collider.sharedMesh=mesh;collider.convex=true;
            var material=new PhysicsMaterial("reward gold bounce"){bounciness=.36f,dynamicFriction=.45f,staticFriction=.5f};owned.Add(material);collider.material=material;
            var rng=new System.Random(unchecked((int)reward.Id*31+index*991));float R(float min,float max)=>min+(max-min)*(float)rng.NextDouble();
            go.transform.position=reward.Origin+new Vector3(R(-.2f,.2f),index*.10f,R(-.2f,.2f));go.transform.rotation=Quaternion.Euler(R(-40,40),R(0,360),R(-40,40));
            var body=go.AddComponent<Rigidbody>();body.mass=.12f;body.linearDamping=.12f;body.angularDamping=.2f;body.maxAngularVelocity=30;body.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;
            body.linearVelocity=new Vector3(R(-2.1f,2.1f),R(3,5),R(-2.1f,2.1f));body.angularVelocity=new Vector3(R(-15,15),R(-15,15),R(-15,15));
            coins.Add(new Coin{Body=body,Reward=reward,Scale=go.transform.localScale});
        }
        public void Dispose(){Destroy(root);foreach(var item in owned)Destroy(item);if(scene.isLoaded)SceneManager.UnloadSceneAsync(scene);coins.Clear();}
        private static void Destroy(Object item){if(UnityEngine.Application.isPlaying)Object.Destroy(item);else Object.DestroyImmediate(item);}
    }
}
