#nullable enable
using System.Linq;
using Goa2.Presentation.UI3D;
using UnityEngine;
using UnityEngine.UIElements;
namespace Goa2.Presentation
{
    public sealed partial class GameScreen
    {
        private string stageKey="",stageTitle="",stageDetail="";
        private float stageStarted=-100;
        private bool stageWaiting;
        private bool StagePresenting=>Time.realtimeSinceStartup-stageStarted<StageBannerPolicy.Duration;
        private void ObserveStageBanner()
        {
            if(InitiativePresenting){stageStarted=-100;stageWaiting=false;return;}
            if(CombatPresenting)return;
            if(renderedView.Opening?.Status=="settled" && (openingSubmitting || physicalCoin!=null && physicalCoin.Finishing && !physicalCoin.Complete))return;
            var stage=StageBannerPolicy.Describe(renderedView,s=>HeroName(renderedView.Players.FirstOrDefault(p=>p.Seat==s)?.HeroId));
            string key=renderedView.MatchId+":"+stage.key;
            if(key==stageKey)return;
            stageDetail=StageBannerPolicy.Detail(renderedView);
            stageKey=key;stageTitle=stage.title;stageStarted=Time.realtimeSinceStartup;stageWaiting=true;
            if(mainFlow)board?.StopFollowing();
            var fx=board3DViewport.Presentation;
            if(stageStarted-fx.CoinStarted<fx.CoinDuration)fx.CoinStarted=Mathf.Max(fx.CoinStarted,stageStarted+StageBannerPolicy.Duration);
        }
        private void BuildStageBanner()
        {
            if(!StagePresenting)return;
            var banner=new VisualElement{name="stage-banner",pickingMode=PickingMode.Ignore};banner.style.position=Position.Absolute;
            banner.style.left=0;banner.style.right=0;banner.style.top=Length.Percent(35);banner.style.height=146;
            banner.style.backgroundColor=new Color(.16f,.18f,.20f,.76f);banner.style.borderTopWidth=1;banner.style.borderBottomWidth=1;
            banner.style.borderTopColor=new Color(.92f,.74f,.35f,.4f);banner.style.borderBottomColor=new Color(.92f,.74f,.35f,.4f);
            var title=new Label(stageTitle){pickingMode=PickingMode.Ignore};title.style.height=98;title.style.fontSize=30;
            title.style.whiteSpace=WhiteSpace.Normal;title.style.unityTextAlign=TextAnchor.MiddleCenter;title.style.color=new Color(1,.83f,.44f);
            title.style.unityFontStyleAndWeight=FontStyle.Bold;title.style.unityTextOutlineWidth=.5f;title.style.unityTextOutlineColor=new Color(.15f,.1f,.04f);banner.Add(title);root.Add(banner);
            var detail=new Label(stageDetail){name="stage-banner-detail",pickingMode=PickingMode.Ignore};detail.style.fontSize=18;detail.style.color=new Color(.76f,.77f,.78f);detail.style.unityTextAlign=TextAnchor.MiddleCenter;detail.style.height=32;banner.Add(detail);
            banner.schedule.Execute(()=>{float age=Time.realtimeSinceStartup-stageStarted;banner.style.opacity=Mathf.Clamp01(Mathf.Min(age/.25f,(StageBannerPolicy.Duration-age)/.4f));}).Every(16);
        }
        private void UpdateStageBanner()
        {
            if(!stageWaiting || StagePresenting)return;
            stageWaiting=false;root.Q("stage-banner")?.RemoveFromHierarchy();
            // Local free-view remains free. Returning to main flow uses its current actor.
            if(mainFlow && cameraFollow)ApplyCameraFollow();
            if(renderedView.Opening!=null)Render();
        }
        private void PreviewStageBanner(string title)
        {
            board?.StopFollowing();stageTitle=title;stageDetail=title.Contains("抛币")?"投掷决策币，确定初始决策权。":title.Contains("暗选")?"每位玩家秘密选择本回合使用的卡牌。":title.Contains("升级")?"选择技能升级方向，获得对应永久被动。":title.Contains("防御")?"被攻击的英雄选择防御方式。":title.Contains("小兵")?"比较双方兵力，处理小兵移除与战线推进。":"当前英雄选择并执行卡牌行动。";stageStarted=Time.realtimeSinceStartup;stageWaiting=true;rightExpanded=false;Render();
        }
    }
}
