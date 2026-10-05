using Goa2.Domain;
using Goa2.Presentation.UI3D;
using NUnit.Framework;
using UnityEngine;
using System.Collections;
using UnityEngine.TestTools;
namespace Goa2.UI3D.Tests
{
    public sealed class PhysicalCoinTests
    {
        [UnitySetUp] public IEnumerator Enter(){yield return new EnterPlayMode();}
        [UnityTearDown] public IEnumerator Leave(){yield return new ExitPlayMode();}
        [Test,Category("RequiresGraphics")]
        public void HostCoinFallsByPhysicsAndResultMatchesLastFrame()
        {
            var original=Random.state;
            try
            {
                Random.InitState(7104);using var stage=new PhysicalCoinStage("draft:1");CoinMotion latest=null;Team? result=null;bool stuck=false;
                stage.FrameReady=f=>latest=f;stage.Settled=(t,q)=>result=t;stage.Stuck=()=>stuck=true;
                stage.Tick(true,false,.02f);Assert.That(latest,Is.Null,"Banner gate prevents throw");
                Assert.That(stage.ArtBounds.size.x,Is.EqualTo(1.6f).Within(.02f));
                var old=RenderTexture.active;RenderTexture.active=stage.Texture;var capture=new Texture2D(stage.Texture.width,stage.Texture.height,TextureFormat.RGBA32,false);capture.ReadPixels(new Rect(0,0,capture.width,capture.height),0,0);capture.Apply();RenderTexture.active=old;
                int opaque=0;foreach(var color in capture.GetPixels32())if(color.a>100)opaque++;
                System.IO.Directory.CreateDirectory("../artifacts/coin-physics");System.IO.File.WriteAllBytes("../artifacts/coin-physics/render.png",capture.EncodeToPNG());Object.Destroy(capture);
                Assert.That(opaque,Is.GreaterThan(1000),"Coin geometry is visible in transparent render texture");
                for(int i=0;i<940&&!result.HasValue&&!stuck;i++)stage.Tick(true,true,.02f);
                Assert.That(result.HasValue || stuck,Is.True);Assert.That(latest,Is.Not.Null);
                Assert.That(latest.Time,Is.GreaterThan(.5f));Assert.That(latest.Sequence,Is.GreaterThan(4));
                if(result.HasValue){float up=1-2*(latest.Rotation[0]*latest.Rotation[0]+latest.Rotation[2]*latest.Rotation[2]);Assert.That(result==Team.Red?up:-up,Is.GreaterThan(.85f));}
            }
            finally{Random.state=original;}
        }
        [Test,Category("RequiresGraphics")]
        public void BevelledEdgeUsuallySettlesWithoutForcingEitherFace()
        {
            var original=Random.state;int flat=0;
            try{
                for(int seed=0;seed<8;seed++){
                    Random.InitState(4300+seed);using var stage=new PhysicalCoinStage("sample:"+seed);bool done=false;
                    stage.Settled=(_,__)=>{flat++;done=true;};stage.Stuck=()=>done=true;
                    for(int step=0;step<940&&!done;step++)stage.Tick(true,true,.02f,false);
                    Assert.That(done,Is.True,"Every sample reaches a result or the explicit reroll state");
                }
                Assert.That(flat,Is.GreaterThanOrEqualTo(6),"Raised gem must not make the entire rim a thick standing cylinder");
            }finally{Random.state=original;}
        }
        [Test,Category("RequiresGraphics")]
        public void ViewerNeverReportsOrStartsIndependentSimulation()
        {
            using var stage=new PhysicalCoinStage("draft:2");int reports=0;stage.FrameReady=_=>reports++;stage.Settled=(_,__)=>reports++;stage.Stuck=()=>reports++;
            stage.AcceptFrame(new CoinMotion{TossId="draft:2",Sequence=1,Position=new[]{1f,.12f,1f}});
            for(int i=0;i<15;i++)stage.Tick(false,true,.08f);
            Assert.That(reports,Is.Zero);stage.Park(Team.Red,"0,0,0,1");Assert.That(stage.Finishing,Is.True);
            stage.DisplaySide(Team.Red,false);stage.Tick(false,true,.02f);
            Assert.That(stage.ArtBounds.center.x,Is.EqualTo(0).Within(.01f));Assert.That(stage.ArtBounds.center.z,Is.EqualTo(0).Within(.01f));
        }
    }
}
