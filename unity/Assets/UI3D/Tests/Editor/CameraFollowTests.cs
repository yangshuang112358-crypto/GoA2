using Goa2.Domain;
using Goa2.Presentation.UI3D;
using NUnit.Framework;
using UnityEngine;
namespace Goa2.UI3D.Tests
{
    public sealed class CameraFollowTests
    {
        private static GameView View(Phase phase) => new GameView {Phase=phase,ActiveSeat=1,Units=new System.Collections.Generic.List<UnitState> {
            new UnitState {Kind="hero",Seat=0,Position=new Hex(1,2)},new UnitState {Kind="hero",Seat=1,Position=new Hex(6,7)}}};
        [Test] public void PlanningTracksOwnSeatNotActiveOrChooser() {var v=View(Phase.Planning);v.Pending=new PendingChoice{ChooserSeat=1};Assert.That(CameraFollowPolicy.Target(v,0),Is.EqualTo(new Hex(1,2)));}
        [Test] public void UpgradeTracksSelfEvenAfterOwnUpgradeCompleted() {var v=View(Phase.RoundEnd);v.RoundEndStage="upgrades";Assert.That(CameraFollowPolicy.Target(v,0),Is.EqualTo(new Hex(1,2)));}
        [TestCase(Phase.Action)] [TestCase(Phase.EffectChoice)]
        public void AttackResponseFocusesChooser(Phase phase) {var v=View(phase);v.Pending=new PendingChoice{Kind="defense",ChooserSeat=0};Assert.That(CameraFollowPolicy.Target(v,0),Is.EqualTo(new Hex(1,2)));Assert.That(v.ActiveSeat,Is.EqualTo(1));}
        [TestCase("round_minion_removal")] [TestCase("action_minion_removal")] [TestCase("minion_spawn")] [TestCase("minion_return")] [TestCase("spawn_order_unresolved")]
        public void CaptainBoardChoicesOverrideActor(string kind) {var v=View(Phase.EffectChoice);v.Pending=new PendingChoice {Kind=kind,ChooserSeat=0};Assert.That(CameraFollowPolicy.Target(v,0),Is.Null);}
        [TestCase(Phase.InitiativeChoice)] [TestCase(Phase.Finished)] [TestCase(Phase.HeroSelection)]
        public void UndecidedOrFinishedUsesOverview(Phase phase) {Assert.That(CameraFollowPolicy.Target(View(phase),0),Is.Null);}
        [Test] public void DefeatedActorDoesNotInventAPosition() {var v=View(Phase.Action);v.Units.RemoveAt(1);Assert.That(CameraFollowPolicy.Target(v,0),Is.Null);}
        [Test] public void FollowSmoothlyMovesWithoutChangingZoomOrRotation() {var s=new Board3DViewport{Zoom=2};s.Rotate(1);s.Follow(new Vector3(10,0,0));s.Advance(.08f);Assert.That(s.Focus.x,Is.InRange(.1f,9.9f));Assert.That(s.Zoom,Is.EqualTo(2));for(int i=0;i<500;i++)s.Advance(.016f);Assert.That(s.Focus.x,Is.EqualTo(10).Within(.001));Assert.That(s.Yaw,Is.EqualTo(30));}
        [Test] public void ManualPanStopsPendingCameraMotion() {var s=new Board3DViewport{Zoom=3};s.Follow(new Vector3(10,0,0),1);s.Advance(.04f);s.StopFollowing();s.Focus=new Vector3(-2,0,5);float zoom=s.Zoom;s.Advance(1);Assert.That(s.Focus,Is.EqualTo(new Vector3(-2,0,5)));Assert.That(s.Zoom,Is.EqualTo(zoom));}
        [Test] public void WheelOverridesOverviewZoomButRetainsFollow() {var s=new Board3DViewport{Zoom=3};s.Follow(new Vector3(10,0,0),1);s.ManualZoom();s.Zoom=4;s.Advance(.1f);Assert.That(s.Zoom,Is.EqualTo(4));Assert.That(s.Focus.x,Is.GreaterThan(0));}
    }
}
