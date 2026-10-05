using Goa2.Presentation.UI3D;
using NUnit.Framework;
namespace Goa2.UI3D.Tests
{
    public sealed class LevelPreviewTests
    {
        [TestCase(1,0,1)][TestCase(1,1,2)][TestCase(1,2,2)][TestCase(1,3,3)]
        [TestCase(1,6,4)][TestCase(4,8,5)][TestCase(4,9,6)][TestCase(7,7,8)]
        [TestCase(8,99,8)][TestCase(1,-1,1)]
        public void ForecastPaysIncreasingCostsWithoutChangingInput(int level,int gold,int target)
        {Assert.That(LevelPreview.Target(level,gold),Is.EqualTo(target));}
        [Test] public void RerenderDoesNotReplayArrowAndGoldLossCanReduceForecast()
        {
            var motion=new LevelPreview.Motion();motion.Observe(0,0);motion.Observe(2,10);
            Assert.That(motion.Changed,Is.EqualTo(10));motion.Observe(2,11);
            Assert.That(motion.Changed,Is.EqualTo(10));motion.Observe(1,12);
            Assert.That(motion.Count,Is.EqualTo(1));Assert.That(motion.Changed,Is.EqualTo(12));
        }
    }
}
