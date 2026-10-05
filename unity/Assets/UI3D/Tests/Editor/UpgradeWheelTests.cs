using System.Linq;
using Goa2.Domain;
using Goa2.Presentation.UI3D;
using NUnit.Framework;
namespace Goa2.UI3D.Tests
{
    public sealed class UpgradeWheelTests
    {
        [TestCase(1)][TestCase(2)][TestCase(3)]
        public void EveryDiscIsEvenlySpacedAndFirstOriginalIsAtTop(int groups)
        {
            var angles=Enumerable.Range(0,groups).SelectMany(g=>Enumerable.Range(0,3).Select(b=>(UpgradeWheelLayout.Angle(groups,g,b)+360)%360)).OrderBy(x=>x).ToArray();
            for(int i=0;i<angles.Length;i++)Assert.That((angles[(i+1)%angles.Length]-angles[i]+360)%360,Is.EqualTo(360f/angles.Length).Within(.01));
            Assert.That(UpgradeWheelLayout.Angle(groups,0,1),Is.EqualTo(-90));
        }
        [Test] public void RemainingIsNotNumberOfCandidateColors()
        {
            var view=new GameView();view.Players.Add(new PlayerView{Seat=2,Level=5});view.UpgradeOptions.Add(new UpgradeOption{HeroLevel=3});
            Assert.That(UpgradeWheelLayout.Remaining(view,2),Is.EqualTo(3));
            view.UpgradeOptions.Clear();Assert.That(UpgradeWheelLayout.Remaining(view,2),Is.Zero);
            view.Players[0].Level=8;view.UpgradeOptions.Add(new UpgradeOption{HeroLevel=8,Color="purple"});
            Assert.That(UpgradeWheelLayout.Remaining(view,2),Is.EqualTo(1));
        }
    }
}
