using Goa2.Domain;
using Goa2.Rules;
using NUnit.Framework;

namespace Goa2.Tests
{
    public sealed class EffectTimelineTests
    {
        [Test]
        public void ThisTurnIsActiveImmediatelyAndExpiresAtThatTurnBoundary()
        {
            var window=EffectTimeline.Create(3,2,4,EffectDuration.ThisTurn)!;
            Assert.That(EffectTimeline.Active(window,3,1), Is.False); Assert.That(EffectTimeline.Active(window,3,2), Is.True);
            Assert.That(EffectTimeline.Active(window,3,3), Is.False); Assert.That(EffectTimeline.EndsAtBoundary(window,3,1), Is.False);
            Assert.That(EffectTimeline.EndsAtBoundary(window,3,2), Is.True);
        }
        [Test]
        public void NextTurnWaitsAndNeverCrossesTheRoundBoundary()
        {
            var window=EffectTimeline.Create(2,3,4,EffectDuration.NextTurn)!;
            Assert.That(EffectTimeline.Active(window,2,3), Is.False); Assert.That(EffectTimeline.Active(window,2,4), Is.True);
            Assert.That(EffectTimeline.EndsAtBoundary(window,2,3), Is.False); Assert.That(EffectTimeline.EndsAtBoundary(window,2,4), Is.True);
            Assert.That(EffectTimeline.Active(window,3,1), Is.False);
            Assert.That(EffectTimeline.Create(2,4,4,EffectDuration.NextTurn), Is.Null);
        }
        [Test]
        public void ThisRoundStartsAtExecutionAndIncludesOnlyRemainingTurns()
        {
            var window=EffectTimeline.Create(1,2,4,EffectDuration.ThisRound)!;
            Assert.That(EffectTimeline.Active(window,1,1), Is.False); Assert.That(EffectTimeline.Active(window,1,2), Is.True);
            Assert.That(EffectTimeline.Active(window,1,4), Is.True); Assert.That(EffectTimeline.Active(window,2,1), Is.False);
            Assert.That(EffectTimeline.EndsAtBoundary(window,1,3), Is.False); Assert.That(EffectTimeline.EndsAtBoundary(window,1,4), Is.True);
        }
        [Test]
        public void InvalidDurationCoordinatesAreRejected()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => EffectTimeline.Create(0,1,4,EffectDuration.ThisTurn));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => EffectTimeline.Create(1,0,4,EffectDuration.ThisTurn));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => EffectTimeline.Create(1,5,4,EffectDuration.NextTurn));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => EffectTimeline.Create(1,1,4,(EffectDuration)99));
        }
    }
}
