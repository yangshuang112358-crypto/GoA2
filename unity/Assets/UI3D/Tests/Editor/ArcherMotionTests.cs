using Goa2.Presentation.UI3D;
using NUnit.Framework;

namespace Goa2.UI3D.Tests
{
    public sealed class ArcherMotionTests
    {
        [Test]public void PreparationTakesElapsedTimeAndHoldsWhileDefenseIsPending()
        {
            var motion=new ArcherMotion();motion.Advance(0,true);
            Assert.That(motion.Progress,Is.Zero);Assert.That(motion.Weight,Is.Zero);
            motion.Advance(ArcherMotion.PrepareDuration*.5f,true);
            Assert.That(motion.Progress,Is.EqualTo(.5f).Within(.0001f));
            motion.Advance(ArcherMotion.PrepareDuration,true);
            Assert.That(motion.Progress,Is.EqualTo(1));Assert.That(motion.Weight,Is.EqualTo(1));
            motion.Advance(10,true);
            Assert.That(motion.Progress,Is.EqualTo(1));Assert.That(motion.Released,Is.False);
        }

        [Test]public void CancelAndResumeRetraceTheCurrentPreparationWithoutRestarting()
        {
            var motion=new ArcherMotion();motion.Advance(0,true);motion.Advance(.85f,true);
            float prepared=motion.Progress;motion.Advance(.85f,false);
            Assert.That(motion.Progress,Is.EqualTo(prepared),"Changing a request does not teleport the pose");
            motion.Advance(1.08f,false);float reversing=motion.Progress;
            Assert.That(reversing,Is.InRange(.29f,.31f));
            motion.Advance(1.08f,true);Assert.That(motion.Progress,Is.EqualTo(reversing));
            motion.Advance(1.25f,true);
            Assert.That(motion.Progress,Is.EqualTo(reversing+.1f).Within(.0001f));
            motion.Advance(1.25f,false);motion.Advance(3,false);
            Assert.That(motion.Progress,Is.Zero);Assert.That(motion.Weight,Is.Zero);Assert.That(motion.Released,Is.False);
        }

        [Test]public void LongFrameAndSmallFramesReachTheSamePreparation()
        {
            var one=new ArcherMotion();var many=new ArcherMotion();one.Advance(0,true);many.Advance(0,true);
            one.Advance(1.36f,true);
            for(int i=1;i<=80;i++)many.Advance(i*.017f,true);
            Assert.That(one.Progress,Is.EqualTo(many.Progress).Within(.0001f));
            Assert.That(one.Weight,Is.EqualTo(many.Weight).Within(.0001f));
            one.Advance(20,true);Assert.That(one.Progress,Is.EqualTo(1));
        }

        [Test]public void FirstScheduledShotKeepsTheBowAlreadyDrawnDuringDefense()
        {
            var motion=new ArcherMotion();motion.Advance(0,true);motion.Advance(2,true);
            motion.Advance(2,true,10,3);
            Assert.That(motion.Progress,Is.EqualTo(1));Assert.That(motion.Weight,Is.EqualTo(1));
            Assert.That(motion.ShotId,Is.EqualTo(10));Assert.That(motion.Released,Is.False);
            motion.Advance(3,true,10,3);
            Assert.That(motion.Released,Is.True);Assert.That(motion.ReleaseAge,Is.Zero);
        }

        [Test]public void ReobservingTheSameClockAfterReleaseDoesNotReplayOrResetIt()
        {
            var motion=ReleasedShot();
            motion.Advance(2.5f,true,10,2);
            float weight=motion.Weight,progress=motion.Progress;
            // Scene rebuilds reuse this per-unit clock, and may sample it repeatedly in one frame.
            for(int i=0;i<3;i++)motion.Advance(2.5f,true,10,2);
            Assert.That(motion.ReleaseAge,Is.EqualTo(.5f));Assert.That(motion.Released,Is.True);
            Assert.That(motion.Weight,Is.EqualTo(weight));Assert.That(motion.Progress,Is.EqualTo(progress));
            motion.Advance(2.7f,true,10,2);
            Assert.That(motion.ReleaseAge,Is.EqualTo(.7f).Within(.0001f));Assert.That(motion.Weight,Is.LessThan(weight));
        }

        [Test]public void LongFramePastReleaseUsesAbsoluteReleaseAgeAndFinishesRecovery()
        {
            var motion=new ArcherMotion();motion.Advance(0,true,10,2);
            motion.Advance(5,true,10,2);
            Assert.That(motion.Released,Is.True);Assert.That(motion.ReleaseAge,Is.EqualTo(3));
            Assert.That(motion.Weight,Is.Zero);Assert.That(motion.Progress,Is.Zero);
            motion.Advance(5,false);Assert.That(motion.Weight,Is.Zero);
        }

        [Test]public void NewPendingDefenseRequestedDuringRecoveryEventuallyPreparesAgain()
        {
            var motion=ReleasedShot();motion.Advance(2.25f,false);
            Assert.That(motion.Weight,Is.GreaterThan(.001f));
            motion.Advance(2.35f,true);
            for(int i=1;i<=120;i++)motion.Advance(2.35f+i*.025f,true);
            Assert.That(motion.Released,Is.False,"A request received while follow-through is visible cannot be lost forever");
            Assert.That(motion.Progress,Is.GreaterThan(.95f));Assert.That(motion.Weight,Is.EqualTo(1));
            Assert.That(motion.ShotId,Is.EqualTo(-1),"Pending defense does not reuse the released shot's identity");
        }

        [Test]public void CancelledRequestDuringRecoveryDoesNotStartAnotherPreparation()
        {
            var motion=ReleasedShot();motion.Advance(2.25f,false);motion.Advance(2.35f,true);
            motion.Advance(2.45f,false);motion.Advance(5,false);
            Assert.That(motion.Progress,Is.Zero);Assert.That(motion.Weight,Is.Zero);
        }

        [Test]public void NewShotAfterALongFrameCannotSpendPreviousShotRecoveryOnItsPreparation()
        {
            var motion=ReleasedShot();
            motion.Advance(4,true,11,7);
            Assert.That(motion.Released,Is.False);Assert.That(motion.ShotId,Is.EqualTo(11));
            Assert.That(motion.Progress,Is.LessThan(.1f),"The elapsed time belonged to the prior shot, not the new arrow fetch");
            motion.Advance(4+ArcherMotion.PrepareDuration*.5f,true,11,7);
            Assert.That(motion.Progress,Is.InRange(.49f,.6f));
            motion.Advance(7,true,11,7);
            Assert.That(motion.Released,Is.True);Assert.That(motion.ReleaseAge,Is.Zero);
        }

        [Test]public void PendingDefenseAfterSettledReleaseCannotBorrowIdleTimeForItsNextArrow()
        {
            var motion=ReleasedShot();motion.Advance(4,false);
            Assert.That(motion.Weight,Is.Zero);
            motion.Advance(10,true);
            Assert.That(motion.Released,Is.False);Assert.That(motion.ShotId,Is.EqualTo(-1));
            Assert.That(motion.Progress,Is.LessThan(.1f),"Time before the new pending request is not preparation time");
            motion.Advance(10+ArcherMotion.PrepareDuration*.5f,true);
            Assert.That(motion.Progress,Is.InRange(.49f,.6f));
        }

        private static ArcherMotion ReleasedShot()
        {
            var motion=new ArcherMotion();motion.Advance(0,true,10,2);
            motion.Advance(1.7f,true,10,2);motion.Advance(2,true,10,2);
            Assert.That(motion.Released,Is.True);return motion;
        }
    }
}
