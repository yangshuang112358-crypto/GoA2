using System;
using System.IO;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Text;
using Goa2.Application;
using Goa2.Domain;
using Goa2.Infrastructure;
using Goa2.Presentation.UI3D;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object=UnityEngine.Object;

namespace Goa2.UI3D.Tests
{
    public sealed class MinionAnimationTests
    {
        static void Invoke(Board3DScene board,string name,params object[] args)=>typeof(Board3DScene).GetMethod(name,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(board,args);
        [UnityTest,Category("RequiresGraphics")]
        public IEnumerator ArcherContactAndReleaseStayContinuousOnImportedRig()
        {
            yield return new EnterPlayMode();
            yield return Run();
            yield return new ExitPlayMode();
        }
        static IEnumerator Run()
        {
            string root=Directory.GetParent(UnityEngine.Application.dataPath).Parent.FullName;
            string output=Path.Combine(root,"artifacts/minion-motion-audit");Directory.CreateDirectory(output);
            var catalog=ContentLoader.LoadDirectory(root);
            var game=LocalGameFactory.Create(catalog,"archer-geometry",new[]{"A","B","C","D"},42,true);
            var initial=game.View(0);
            Assert.That(game.Execute(0,new Command{Id="prepare",MatchId=initial.MatchId,ExpectedRevision=initial.Revision,ActorSeat=0,Kind=CommandKind.DebugPrepare,Value="sabina,wasp,brogan,arien"}).Accepted,Is.True);
            string save=game.ExportSave();var view=game.View(0);
            var bow=new UnitState{Id="pose-bow",Kind="ranged",Team=Team.Blue,Position=new Hex(-3,2)};
            var victim=new UnitState{Id="hero:1",Kind="hero",Seat=1,Team=Team.Red,Position=new Hex(-1,2)};
            view.Units.Clear();view.Units.Add(bow);view.Units.Add(victim);
            var state=new Board3DViewport();
            using(var board=new Board3DScene(catalog,view,new Hex[0],null,new Hex[0],state))
            {
                board.Render(1000,900);
                var parent=board.Camera.transform.parent;
                var model=parent.GetComponentsInChildren<Transform>().Single(t=>t.name=="minion ranged pose-bow");
                Transform Bone(string n)=>model.GetComponentsInChildren<Transform>().Single(t=>t.name==n);
                float begin=Time.realtimeSinceStartup;
                var shot=new CombatPresentationTimeline.Shot{Id=300,PrepareStarted=begin,Start=begin+2,Source=view.Units[0],Target=victim};shot.Support.Add(bow);
                state.Presentation.Combat.Shots.Add(shot);Invoke(board,"BuildCombatProjectiles");
                var held=parent.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="nocked arrow pose-bow");
                var flying=parent.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="released arrow 300 pose-bow");
                var line=parent.GetComponentsInChildren<LineRenderer>().Single(l=>l.name=="bow string pose-bow");
                var skin=model.GetComponentInChildren<SkinnedMeshRenderer>();var baked=new Mesh();
                int headIndex=Array.FindIndex(skin.bones,b=>b.name=="Head");
                var weights=skin.sharedMesh.boneWeights;var tris=skin.sharedMesh.triangles;
                var headTriangles=Enumerable.Range(0,tris.Length/3).Where(i=>Enumerable.Range(0,3).All(j=>weights[tris[i*3+j]].boneIndex0==headIndex && weights[tris[i*3+j]].weight0>.99f)).ToArray();
                Assert.That(headTriangles.Length,Is.GreaterThan(100),"Inspect actual hood geometry");
                var csv=new StringBuilder("time,progress,weight,handx,handy,handz,restx,resty,restz,tailx,taily,tailz\n");
                var samples=new[]{0,20,32,43,54,66,80,99,120,126,128,138,160,182,198};
                Vector3 lastHand=Bone("Hand.R").position,lastTail=Vector3.zero;bool wasHeld=false;
                for(int frame=0;frame<=210;frame++)
                {
                    float now=begin+frame/60f;Invoke(board,"AnimateMinionPoses",now);Invoke(board,"AnimateCombat",now);
                    yield return null; // Let Unity update the imported skinned mesh, not just bone transforms.
                    var hand=Bone("Hand.R").position;var rest=Bone("BowRest").position;var motion=state.Presentation.MinionMotions[bow.Id].Bow;
                    Assert.That(Vector3.Distance(hand,lastHand),Is.LessThan(.18f),"No one-frame hand snap at frame "+frame);lastHand=hand;
                    Assert.That(Vector3.Distance(Bone("BowGrip").position,Bone("Hand.L").position),Is.LessThan(.03f),"Bow stays in grip");
                    if(held.gameObject.activeSelf && motion.Progress>.68f)
                    {
                        float along=Vector3.Dot(rest-held.position,held.forward);
                        Assert.That(Vector3.Distance(held.position,hand),Is.LessThan(.003f),"Nock meets draw hand");
                        Assert.That(Vector3.Distance(line.GetPosition(1),hand),Is.LessThan(.003f),"String meets nock");
                        Assert.That(Vector3.Distance(held.position+held.forward*along,rest),Is.LessThan(.003f),"Arrow passes arrow rest");
                        Assert.That(along,Is.InRange(.1f,held.localScale.z),"Arrow extends past rest");
                    }
                    if(held.gameObject.activeSelf)
                    {
                        skin.BakeMesh(baked);var vertices=baked.vertices;
                        var start=skin.transform.InverseTransformPoint(held.position);
                        var end=skin.transform.InverseTransformPoint(held.position+held.forward*held.localScale.z);
                        Assert.That(headTriangles.Any(i=>Intersects(start,end,vertices[tris[i*3]],vertices[tris[i*3+1]],vertices[tris[i*3+2]])),Is.False,"Arrow shaft must not pass through hood at frame "+frame);
                        for(int segment=0;segment<2;segment++)
                        {
                            var a=skin.transform.InverseTransformPoint(line.GetPosition(segment));var b=skin.transform.InverseTransformPoint(line.GetPosition(segment+1));
                            Assert.That(headTriangles.Any(i=>Intersects(a,b,vertices[tris[i*3]],vertices[tris[i*3+1]],vertices[tris[i*3+2]])),Is.False,"String must not pass through hood at frame "+frame);
                        }
                        if(frame==99)
                        {
                            var hood=new Bounds(vertices[tris[headTriangles[0]*3]],Vector3.zero);
                            foreach(int tri in headTriangles)for(int j=0;j<3;j++)hood.Encapsulate(vertices[tris[tri*3+j]]);
                            Assert.That(headTriangles.Any(i=>Intersects(hood.center-Vector3.right*hood.size.x,hood.center+Vector3.right*hood.size.x,vertices[tris[i*3]],vertices[tris[i*3+1]],vertices[tris[i*3+2]])),Is.True,"Known ray through hood validates the geometry checker");
                        }
                    }
                    if(flying.gameObject.activeSelf && wasHeld)
                        Assert.That(Vector3.Distance(flying.position,lastTail),Is.LessThan(.20f),"Projectile originates at held nock");
                    wasHeld=held.gameObject.activeSelf;lastTail=held.position;
                    csv.AppendLine(string.Join(",",new[]{frame/60f,motion.Progress,motion.Weight,hand.x,hand.y,hand.z,rest.x,rest.y,rest.z,held.position.x,held.position.y,held.position.z}.Select(x=>x.ToString("R",System.Globalization.CultureInfo.InvariantCulture))));
                    if(samples.Contains(frame))foreach(bool side in new[]{false,true})
                    {
                        var center=Board3DGeometry.World(bow.Position,1.15f);
                        // Fixed world camera per angle avoids concealing a bad transition with a camera cut.
                        var cameraOffset=side?new Vector3(0,1.6f,6):new Vector3(4,2.5f,5);
                        board.Camera.transform.position=center+cameraOffset;board.Camera.transform.LookAt(center);
                        board.Camera.orthographicSize=1.55f;board.Camera.Render();
                        Capture(board,Path.Combine(output,"archer-"+frame.ToString("D3")+(side?"-side":"-three")+".png"));
                    }
                }
                File.WriteAllText(Path.Combine(output,"archer-samples.csv"),csv.ToString());
                Object.DestroyImmediate(baked);
                Assert.That(shot.Arrows.ContainsKey(bow.Id),Is.True);
                Assert.That(state.Presentation.MinionMotions[bow.Id].Bow.Weight,Is.LessThan(.001f));
                // Change direction for a second arrow, then simulate a long frame across
                // release before a second full-draw frame exists. Old launch data must expire.
                var second=new CombatPresentationTimeline.Shot{Id=301,PrepareStarted=begin+4,Start=begin+6,Source=bow,
                    Target=new UnitState{Id="hero:1",Kind="hero",Seat=1,Team=Team.Red,Position=new Hex(-5,2)}};
                second.Support.Add(bow);state.Presentation.Combat.Shots.Add(second);
                Invoke(board,"AnimateMinionPoses",begin+4);Invoke(board,"AnimateMinionPoses",begin+4.2f);
                Assert.That(state.Presentation.MinionMotions[bow.Id].Bow.Progress,Is.LessThan(.67f));
                Invoke(board,"AnimateMinionPoses",second.Release+.01f);
                var newLaunch=second.Arrows[bow.Id];var oldLaunch=shot.Arrows[bow.Id];
                Assert.That(Vector3.Dot(newLaunch.Direction,oldLaunch.Direction),Is.LessThan(-.8f),"A second shot uses its new direction after a long frame");
                Assert.That(Vector3.Distance(newLaunch.Tail,oldLaunch.Tail),Is.GreaterThan(.3f),"A second shot cannot borrow the first nock");
                // Imported arrow is one metre along +Z, tail at zero, with actual meshes.
                var arrow=Object.Instantiate(Resources.Load<GameObject>("UI3D/Minions/Arrow"));
                try {var renderers=arrow.GetComponentsInChildren<Renderer>();var bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);Assert.That(bounds.min.z,Is.EqualTo(0).Within(.001));Assert.That(bounds.max.z,Is.EqualTo(1).Within(.001));Assert.That(bounds.size.y,Is.LessThan(.12f));}
                finally {Object.DestroyImmediate(arrow);}
            }
            Assert.That(game.ExportSave(),Is.EqualTo(save));
        }
        static bool Intersects(Vector3 start,Vector3 end,Vector3 a,Vector3 b,Vector3 c)
        {
            var d=end-start;var e1=b-a;var e2=c-a;var p=Vector3.Cross(d,e2);float det=Vector3.Dot(e1,p);
            if(Mathf.Abs(det)<1e-8f)return false;
            float inv=1/det;var s=start-a;float u=Vector3.Dot(s,p)*inv;if(u<0 || u>1)return false;
            var q=Vector3.Cross(s,e1);float v=Vector3.Dot(d,q)*inv;if(v<0 || u+v>1)return false;
            float t=Vector3.Dot(e2,q)*inv;return t>.0001f && t<.9999f;
        }
        static void Capture(Board3DScene board,string path)
        {
            var previous=RenderTexture.active;var image=new Texture2D(1000,900,TextureFormat.RGB24,false);
            try {RenderTexture.active=board.Texture;image.ReadPixels(new Rect(0,0,1000,900),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());}
            finally {RenderTexture.active=previous;Object.DestroyImmediate(image);}
        }
    }
}
