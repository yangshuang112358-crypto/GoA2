using System.Linq;
using Goa2.Domain;
using Goa2.Presentation.UI3D;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Goa2.UI3D.Tests
{
    public sealed class SkillCardArtTests
    {
        [Test] public void WaspBranchProgressionsKeepIdentityButBaseIsDistinct(){
            SkillCardArt.Entry Art(string id)=>SkillCardArt.Find(new CardDefinition{Id=id});
            Assert.That(Art("wasp-02-回旋镖").IconKey,Is.EqualTo(Art("wasp-04-雷霆回旋镖").IconKey));
            Assert.That(Art("wasp-03-电能波").IconKey,Is.EqualTo(Art("wasp-05-电能爆炸").IconKey));
            Assert.That(Art("wasp-01-电击").IconKey,Is.Not.EqualTo(Art("wasp-02-回旋镖").IconKey));
            Assert.That(Art("wasp-02-回旋镖").IconKey,Is.Not.EqualTo(Art("wasp-03-电能波").IconKey));
            Assert.That(Art("wasp-02-回旋镖").NameKey,Is.Not.EqualTo(Art("wasp-04-雷霆回旋镖").NameKey));
        }
        [TestCase(null,0)][TestCase(1,1)][TestCase(2,2)][TestCase(3,3)][TestCase(4,4)]
        public void TierGemsUseCardLevelAndNeverInventGoldOrSilverLevels(int? level,int count){
            Assert.That(SkillCardArt.TierCount(new CardDefinition{Level=level}),Is.EqualTo(count));
            Assert.That(SkillCardArt.Find(null),Is.Null);
        }
        [TestCase("shield","12",0)][TestCase("sword","-3",-8)][TestCase("hourglass","15",7)][TestCase("shield","∞",0)]
        public void LiveValuesHaveRealCavityAssetsAndKeepTheirExactMeaning(string kind,string value,int bonus){
            var badge=new SkillBadge(kind,value,bonus,"red");
            Assert.That(badge.HasCarvedValue,Is.True);Assert.That(badge.CarvedTexture.width,Is.GreaterThanOrEqualTo(256));
            Assert.That(badge.Q<Label>("badge-number").text,Is.EqualTo(value));
            if(bonus!=0)Assert.That(SkillDiscArtwork.R2B(kind+"-"+SkillDiscArtwork.ValueKey(value)+"-fill"),Is.Not.Null);
        }
        [Test] public void FutureOutOfAtlasValuesRemainReadableAndAreNeverClamped(){
            var badge=new SkillBadge("sword","128",4);
            Assert.That(badge.HasCarvedValue,Is.False);Assert.That(badge.Q<Label>("badge-number").text,Is.EqualTo("128"));
            Assert.That(badge.Q<Label>("badge-number").style.opacity.value,Is.EqualTo(1));
        }
        [Test] public void SkillWithoutNumberDoesNotInventZero(){
            var badge=new SkillBadge("spark","",0,"blue");Assert.That(badge.HasCarvedValue,Is.True);
            Assert.That(badge.Q<Label>("badge-number").text,Is.Empty);
        }
    }
}
