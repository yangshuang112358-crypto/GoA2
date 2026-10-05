#nullable enable
using Goa2.Presentation.UI3D;
using UnityEngine;
using UnityEngine.UIElements;
namespace Goa2.Presentation
{
    public sealed partial class GameScreen
    {
        private RewardCoinWorld? rewardWorld;
        private int rewardGeneration=-1;
        private bool wasCombatBusy;
        private bool CombatPresenting=>mainFlow && Time.realtimeSinceStartup<board3DViewport.Presentation.Combat.BusyUntil;
        private void UpdateCombatPresentation()
        {
            if(!HasGameView || startupFailed)return;
            var timeline=board3DViewport.Presentation.Combat;
            if(rewardGeneration!=timeline.Generation){rewardWorld?.Dispose();rewardWorld=null;rewardGeneration=timeline.Generation;}
            if(rewardWorld==null && timeline.Rewards.Count>0)rewardWorld=new RewardCoinWorld(catalog,timeline);
            rewardWorld?.Tick(renderedView,Time.realtimeSinceStartup);
            var shot=timeline.Current(Time.realtimeSinceStartup);
            if(mainFlow && shot!=null && Time.realtimeSinceStartup>=shot.Start && skillWheel!=null)skillWheel.style.display=DisplayStyle.None;
            bool busy=CombatPresenting;
            if(wasCombatBusy && !busy)Render();wasCombatBusy=busy;
        }
        private void ApplyCombatPresentationGate()
        {
            if(!CombatPresenting)return;
            root.Q("world-decisions")?.SetEnabled(false);root.Q("decision-dock")?.SetEnabled(false);
            skillWheel?.SetEnabled(false);root.Q("upgrade-zone")?.SetEnabled(false);confirmAction=null;confirmButton=null;
            var shot=board3DViewport.Presentation.Combat.Current(Time.realtimeSinceStartup);
            if(shot!=null && Time.realtimeSinceStartup>=shot.Start && skillWheel!=null)skillWheel.style.display=DisplayStyle.None;
        }
    }
}
