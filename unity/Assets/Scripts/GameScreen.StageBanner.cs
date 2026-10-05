#nullable enable
using System.Linq;
using Goa2.Presentation.UI3D;
using UnityEngine;
using UnityEngine.UIElements;
namespace Goa2.Presentation
{
    public sealed partial class GameScreen
    {
        private string stageKey="",stageTitle="";
        private float stageStarted=-100;
        private bool stageWaiting;
        private bool StagePresenting=>Time.realtimeSinceStartup-stageStarted<1.15f;
        private void ObserveStageBanner()
        {
            var stage=StageBannerPolicy.Describe(renderedView,s=>HeroName(renderedView.Players.FirstOrDefault(p=>p.Seat==s)?.HeroId));
            string key=renderedView.MatchId+":"+stage.key;
            if(key==stageKey)return;
            stageKey=key;stageTitle=stage.title;stageStarted=Time.realtimeSinceStartup;stageWaiting=true;
            if(mainFlow)board?.StopFollowing();
            var fx=board3DViewport.Presentation;
            if(stageStarted-fx.CoinStarted<fx.CoinDuration)fx.CoinStarted=Mathf.Max(fx.CoinStarted,stageStarted+1.15f);
        }
        private void BuildStageBanner()
        {
            if(!StagePresenting)return;
            var banner=new VisualElement{name="stage-banner",pickingMode=PickingMode.Ignore};banner.style.position=Position.Absolute;
            banner.style.left=0;banner.style.right=0;banner.style.top=Length.Percent(37);banner.style.height=110;
            banner.style.backgroundColor=new Color(.16f,.18f,.20f,.76f);banner.style.borderTopWidth=1;banner.style.borderBottomWidth=1;
            banner.style.borderTopColor=new Color(.92f,.74f,.35f,.4f);banner.style.borderBottomColor=new Color(.92f,.74f,.35f,.4f);
            var title=new Label(stageTitle){pickingMode=PickingMode.Ignore};title.StretchToParentSize();title.style.fontSize=30;
            title.style.whiteSpace=WhiteSpace.Normal;title.style.unityTextAlign=TextAnchor.MiddleCenter;title.style.color=new Color(1,.83f,.44f);
            title.style.unityFontStyleAndWeight=FontStyle.Bold;title.style.unityTextOutlineWidth=.5f;title.style.unityTextOutlineColor=new Color(.15f,.1f,.04f);banner.Add(title);root.Add(banner);
            banner.schedule.Execute(()=>{float age=Time.realtimeSinceStartup-stageStarted;banner.style.opacity=Mathf.Clamp01(Mathf.Min(age/.18f,(1.15f-age)/.3f));}).Every(16);
        }
        private void UpdateStageBanner()
        {
            if(!stageWaiting || StagePresenting)return;
            stageWaiting=false;root.Q("stage-banner")?.RemoveFromHierarchy();
            // Local free-view remains free. Returning to main flow uses its current actor.
            if(mainFlow && cameraFollow)ApplyCameraFollow();
        }
        private void PreviewStageBanner(string title)
        {
            board?.StopFollowing();stageTitle=title;stageStarted=Time.realtimeSinceStartup;stageWaiting=true;rightExpanded=false;Render();
        }
    }
}
