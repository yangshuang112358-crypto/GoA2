using System.Linq;
using Goa2.Domain;
using Goa2.Presentation.UI3D;
using NUnit.Framework;
using UnityEngine;
namespace Goa2.UI3D.Tests
{
    public sealed class PresentationRefreshTests
    {
        [Test] public void StageDoesNotRestartWhenOnlyLocalSelectionChanges()
        {
            var v=new GameView{Round=1,Turn=2,Phase=Phase.Action,ActiveSeat=0};
            var before=StageBannerPolicy.Describe(v,s=>"英雄"+s);v.Revision++;
            Assert.That(StageBannerPolicy.Describe(v,s=>"英雄"+s).key,Is.EqualTo(before.key));
            v.Pending=new PendingChoice{Kind="defense",ChooserSeat=1,Id="defense-a"};
            Assert.That(StageBannerPolicy.Describe(v,s=>"英雄"+s).title,Does.Contain("英雄1防御"));
            v.Pending.Id="defense-b";Assert.That(StageBannerPolicy.Describe(v,s=>s.ToString()).key,Does.Contain("defense-b"));
        }
        [Test] public void BaselineInfluenceIncludesFriendlyMeleeAndEnemyRangedButNotHeroes()
        {
            var target=new UnitState{Id="hero",Kind="hero",Team=Team.Blue,Position=new Hex(0,0)};
            var units=new[]{target,new UnitState{Id="guard",Kind="melee",Team=Team.Blue,Position=new Hex(1,0)},new UnitState{Id="bow",Kind="ranged",Team=Team.Red,Position=new Hex(2,0)},new UnitState{Id="far",Kind="melee",Team=Team.Red,Position=new Hex(2,-1)},new UnitState{Id="allyHero",Kind="hero",Team=Team.Blue,Position=new Hex(-1,0)},new UnitState{Id="friendlyHeavy",Kind="heavy",Team=Team.Blue,Position=new Hex(0,1)}};
            var result=MinionCombatBaseline.Sources(units,target);
            Assert.That(result.FriendlyGuardSources,Is.EqualTo(new[]{"guard"}));Assert.That(result.EnemySupportSources,Is.EqualTo(new[]{"bow"}));
        }
        [Test] public void HeavyRaisesBeforeFullSupportAndMotionsSurviveRerender()
        {
            var m=new MinionPoseMotion();m.Set("support",true,0);m.Advance(0);m.Advance(.016f);
            Assert.That(m.Raised,Is.InRange(0,.2));m.Set("support",true,.02f);Assert.That(m.Changed,Is.Zero);
            for(int i=2;i<100;i++)m.Advance(i*.016f);Assert.That(m.Raised,Is.GreaterThan(.99));
            m.Set("idle",false,2);for(int i=0;i<100;i++)m.Advance(2+i*.016f);Assert.That(m.Raised,Is.LessThan(.01));
        }
        [TestCase("wasp")][TestCase("shargatha")][TestCase("brogan")][TestCase("arien")][TestCase("tigerclaw")][TestCase("sabina")]
        public void HeroBlockoutImportsWithSkinnedGeometry(string hero)
        {
            var asset=Resources.Load<GameObject>("UI3D/Heroes/"+hero);Assert.That(asset,Is.Not.Null);
            var skin=asset.GetComponentInChildren<SkinnedMeshRenderer>();Assert.That(skin,Is.Not.Null);
            Assert.That(skin.sharedMesh.vertexCount,Is.InRange(1000,25000));Assert.That(skin.bones.Length,Is.GreaterThanOrEqualTo(4));
            Assert.That(asset.GetComponentsInChildren<Collider>().Length,Is.Zero);
        }
    }
}
