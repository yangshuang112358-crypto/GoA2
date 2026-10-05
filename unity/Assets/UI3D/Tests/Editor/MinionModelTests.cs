using System.IO;
using System.Collections;
using System.Linq;
using Goa2.Application;
using Goa2.Domain;
using Goa2.Infrastructure;
using Goa2.Presentation.UI3D;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Goa2.UI3D.Tests
{
    public sealed class MinionModelTests
    {
        [TestCase("Melee")]
        [TestCase("Ranged")]
        [TestCase("Heavy")]
        public void ExportedModelHasReadableGeometryAndAnEditableSkeleton(string kind)
        {
            var model=Resources.Load<GameObject>("UI3D/Minions/"+kind);
            Assert.That(model,Is.Not.Null);
            var renderer=model.GetComponentInChildren<SkinnedMeshRenderer>();
            Assert.That(renderer,Is.Not.Null);
            Assert.That(renderer.sharedMesh.vertexCount,Is.InRange(1000,30000));
            Assert.That(renderer.bones.Length,Is.GreaterThanOrEqualTo(10));
            Assert.That(renderer.sharedMaterials.Any(m=>m.name.StartsWith("TeamCloth")),Is.True);
            Assert.That(renderer.sharedMesh.colors32.Length,Is.EqualTo(renderer.sharedMesh.vertexCount));
            Assert.That(model.GetComponentsInChildren<Collider>().Length,Is.Zero);
            // FBX uses centimeters and a converted local axis; inspect the instantiated world bounds.
            var instance=Object.Instantiate(model);
            try { Assert.That(instance.GetComponentInChildren<SkinnedMeshRenderer>().bounds.size.y,Is.InRange(1.5f,2.2f)); }
            finally { Object.DestroyImmediate(instance); }
        }

        [UnityTest, Category("RequiresGraphics")]
        public IEnumerator BoardUsesModelsWithoutMutatingTheProjectedGameOrTokenPicking()
        {
            yield return new EnterPlayMode();
            RunBoardCheck();
            yield return new ExitPlayMode();
        }

        private static void RunBoardCheck()
        {
            var root=Directory.GetParent(UnityEngine.Application.dataPath).Parent.FullName;
            var catalog=ContentLoader.LoadDirectory(root);
            var game=LocalGameFactory.Create(catalog,"minion-model-check",new[]{"A","B","C","D"},42,true);
            var initial=game.View(0);
            var result=game.Execute(0,new Command{Id="prepare",MatchId=initial.MatchId,ExpectedRevision=initial.Revision,ActorSeat=0,Kind=CommandKind.DebugPrepare,Value="brogan,wasp,shargatha,arien"});
            Assert.That(result.Accepted,Is.True);
            string before=game.ExportSave();var view=game.View(0);
            var state=new Board3DViewport();
            using(var board=new Board3DScene(catalog,view,new Hex[0],null,new Hex[0],state))
            {
                Assert.That(board.ModeledMinionCount,Is.EqualTo(view.Units.Count(u=>!u.Seat.HasValue)));
                Assert.That(board.TokenCount,Is.EqualTo(view.Units.Count));
                Assert.That(board.ModeledHeroCount,Is.EqualTo(4));
                var coin=Resources.FindObjectsOfTypeAll<Transform>().Single(t=>t.gameObject.scene==board.Camera.gameObject.scene && t.name=="decision coin");
                var coinRenderers=coin.GetComponentsInChildren<Renderer>();var coinBounds=coinRenderers[0].bounds;
                foreach(var r in coinRenderers)coinBounds.Encapsulate(r.bounds);
                Assert.That(coinBounds.size.y,Is.LessThan(.20f),"Minted coin lies flat on tray before animation");
                Assert.That(coinBounds.size.x,Is.EqualTo(.965f).Within(.03f));
                var rock=Resources.FindObjectsOfTypeAll<MeshFilter>().Single(m=>m.gameObject.scene==board.Camera.gameObject.scene && m.sharedMesh!=null && m.sharedMesh.name=="connected symmetric rocks").sharedMesh;
                Assert.That(rock.vertices.All(v=>!float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsNaN(v.z)),Is.True);
                var center=(Board3DGeometry.World(new Hex(0,0))+Board3DGeometry.World(new Hex(0,1)))*.5f;
                string Key(Vector3 v)=>Mathf.RoundToInt(v.x*1000)+":"+Mathf.RoundToInt(v.y*1000)+":"+Mathf.RoundToInt(v.z*1000);
                var points=rock.vertices.Select(Key).ToHashSet();
                foreach(var v in rock.vertices)Assert.That(points.Contains(Key(new Vector3(center.x*2-v.x,v.y,center.z*2-v.z))),Is.True,"Rock mesh retains exact central symmetry");
                Assert.That(board.Labels.Any(x=>x.text=="重"),Is.True);
                Assert.That(board.Labels.Any(x=>x.text=="远"),Is.True);
                board.Render(1600,1000);
                Capture(board,Path.Combine(root,"artifacts/minion-models/unity-board.png"));
                foreach(var token in board.Labels.Where(t=>view.Units.Any(u=>!u.Seat.HasValue && u.Position==t.cell)))
                {
                    var point=board.Project(token.cell,new Vector2(1600,1000),token.top);
                    Assert.That(board.Hit(point,new Vector2(1600,1000))?.Position,Is.EqualTo(token.cell));
                }
            }
            Assert.That(game.ExportSave(),Is.EqualTo(before));
            // Presentation-only lineup on real terrain, distinct from an actual match state.
            var lineup=game.View(0);
            lineup.Units.Clear();
            var kinds=new[]{"melee","ranged","heavy"};
            for(int i=0;i<3;i++)
            {
                lineup.Units.Add(new UnitState{Id="preview-blue-"+i,Kind=kinds[i],Team=Team.Blue,Position=new Hex(-3+i*2,2)});
                lineup.Units.Add(new UnitState{Id="preview-red-"+i,Kind=kinds[i],Team=Team.Red,Position=new Hex(-1+i*2,-2)});
            }
            var display=new Board3DViewport{Initialized=true,Focus=Vector3.zero,Zoom=3.6f};
            using(var board=new Board3DScene(catalog,lineup,new Hex[0],null,new Hex[0],display))
            {
                board.Render(1600,1000);
                Assert.That(board.ModeledMinionCount,Is.EqualTo(6));
                Capture(board,Path.Combine(root,"artifacts/minion-models/unity-lineup.png"));
                for(int i=0;i<6;i++) display.Rotate(1);
                display.Advance(1);
                board.Render(1600,1000);
                Capture(board,Path.Combine(root,"artifacts/minion-models/unity-lineup-reverse.png"));
            }
            foreach(string pose in new[]{"support","guard"}){
                display.MinionPreviewPose=pose;
                using(var board=new Board3DScene(catalog,lineup,new Hex[0],null,new Hex[0],display)){
                    float now=Time.realtimeSinceStartup;
                    foreach(var motion in display.Presentation.MinionMotions.Values){motion.LastTime=now-2;for(int i=0;i<=100;i++)motion.Advance(now-2+i*.02f);}
                    board.Render(1600,1000);Capture(board,Path.Combine(root,"artifacts/minion-models/unity-pose-"+pose+".png"));
                }
            }
            Assert.That(game.ExportSave(),Is.EqualTo(before));
        }

        private static void Capture(Board3DScene board,string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var previous=RenderTexture.active;
            var image=new Texture2D(1600,1000,TextureFormat.RGB24,false);
            try
            {
                RenderTexture.active=board.Texture;
                image.ReadPixels(new Rect(0,0,1600,1000),0,0);image.Apply();
                File.WriteAllBytes(path,image.EncodeToPNG());
            }
            finally {RenderTexture.active=previous;Object.Destroy(image);}
        }
    }
}
