#nullable enable
using System.Linq;
using Goa2.Presentation.UI3D;
using UnityEngine;
using UnityEngine.UIElements;
namespace Goa2.Presentation
{
    public sealed partial class GameScreen
    {
        private bool cameraFollow=true,followOverview,followInitialized,showHotkeys;
        private float heroZoom=3,heroZoom2D=2,toastStarted=-100;
        private float? cinematicResumeZoom;
        private Label? followToast;
        private Button? followButton;
        private void SetCameraFollow(bool enabled)
        {
            if(cameraFollow==enabled) return;
            cameraFollow=enabled;toastStarted=Time.realtimeSinceStartup;
            if(enabled) ApplyCameraFollow(true);else {cinematicResumeZoom=null;board?.StopFollowing();}
            RefreshFollowControls();RequestCapture();
        }
        private void RefreshFollowControls()
        {
            if(followButton!=null) {followButton.text=cameraFollow ? "◉" : "◎";followButton.tooltip="视野跟随模式："+(cameraFollow ? "开" : "关")+"（空格）";followButton.EnableInClassList("follow-on",cameraFollow);}
            if(followToast==null) return;
            float age=Time.realtimeSinceStartup-toastStarted;
            followToast.text="视野跟随模式："+(cameraFollow ? "开" : "关");
            followToast.style.display=age<1.6f ? DisplayStyle.Flex : DisplayStyle.None;
            followToast.style.opacity=age<.18f ? Mathf.Clamp01(age/.18f) : age<1.18f ? 1 : Mathf.Clamp01((1.6f-age)/.42f);
            followToast.style.bottom=86+16*Mathf.Clamp01(age/.18f);
        }
        private void ApplyCameraFollow(bool reenabled=false)
        {
            if(board==null) return;
            if(!cameraFollow) {board.StopFollowing();return;}
            var target=CameraFollowPolicy.Target(renderedView,seat);
            bool overview=!target.HasValue;
            float? zoom=!followInitialized && !overview ? (board3DViewport.Enabled ? heroZoom : heroZoom2D) : (float?)null;
            if(overview && (reenabled || !followInitialized || !followOverview)) {
                if(followInitialized && !followOverview) {heroZoom=board3DViewport.Zoom;heroZoom2D=viewport.Zoom;}zoom=1;
            } else if(!overview && followInitialized && followOverview) zoom=board3DViewport.Enabled ? heroZoom : heroZoom2D;
            bool regional=renderedView.RoundEndStage!="upgrades" && (renderedView.Phase==Goa2.Domain.Phase.RoundEnd || renderedView.Pending?.Kind=="round_minion_removal" || renderedView.Pending?.Kind=="action_minion_removal" || renderedView.Pending?.Kind=="minion_spawn" || renderedView.Pending?.Kind=="minion_return");
            var points=catalog.Cells.Where(c=>!regional || c.Region==renderedView.CombatRegion).Select(c=>Board3DGeometry.World(c.Position)).ToList();
            if(points.Count==0)points=catalog.Cells.Select(c=>Board3DGeometry.World(c.Position)).ToList();
            var focus=target.HasValue ? Board3DGeometry.World(target.Value) : new Vector3((points.Min(p=>p.x)+points.Max(p=>p.x))*.5f,0,(points.Min(p=>p.z)+points.Max(p=>p.z))*.5f);
            if(regional)zoom=board.Scene?.ZoomForRegion(catalog.Cells.Where(c=>c.Region==renderedView.CombatRegion).Select(c=>c.Position)) ?? 2.2f;
            if(renderedView.Pending?.Kind=="hero_respawn") {
                var team=renderedView.Players.Single(p=>p.Seat==renderedView.Pending.ChooserSeat).Team;
                var spawn=catalog.Cells.Where(c=>c.Spawn==(team==Goa2.Domain.Team.Blue ? "blueHeroSpawn" : "redHeroSpawn")).Select(c=>Board3DGeometry.World(c.Position)).ToList();
                if(spawn.Count>0) {focus=spawn.Aggregate(Vector3.zero,(a,b)=>a+b)/spawn.Count;zoom=2.5f;}
            }
            var wheelUnit=wheelSeat.HasValue ? renderedView.Units.FirstOrDefault(u=>u.Seat==wheelSeat.Value) : null;
            if(wheelUnit!=null && (WheelDiscard(renderedView) || wheelState.Discards.Count>0))focus=Board3DGeometry.World(wheelUnit.Position);
            var fx=board3DViewport.Presentation;float now=Time.realtimeSinceStartup;
            if(now<fx.DeathUntil || now-fx.CoinStarted<fx.CoinDuration) {if(zoom.HasValue || !cinematicResumeZoom.HasValue)cinematicResumeZoom=zoom ?? (board3DViewport.Enabled ? board3DViewport.Zoom : viewport.Zoom);}
            else if(cinematicResumeZoom.HasValue) {zoom=zoom ?? cinematicResumeZoom;cinematicResumeZoom=null;}
            if(now<fx.DeathUntil) {focus=fx.DeathFocus;zoom=2.5f;}
            else if(now-fx.CoinStarted<fx.CoinDuration) {focus=BattlePresentationState.Center(catalog);zoom=1.6f;}
            board.FollowAt(focus,zoom);followOverview=overview;followInitialized=true;
        }
        private bool wasCinematic;
        private void UpdatePresentationFocus() {
            var fx=board3DViewport.Presentation;float now=Time.realtimeSinceStartup;
            bool active=now<fx.DeathUntil || now-fx.CoinStarted<fx.CoinDuration;
            if(active!=wasCinematic) {wasCinematic=active;if(cameraFollow && board!=null)ApplyCameraFollow();}
        }
        private void ConfirmCurrent()
        {
            if(ScenarioRunning || confirmButton==null || !confirmButton.enabledInHierarchy) return;
            var action=confirmAction;confirmAction=null;action?.Invoke();
        }
        private readonly StoneSettingsButton.Motion settingsMotion = new StoneSettingsButton.Motion();
        private void BuildCameraOverlays()
        {
            var settings=new StoneSettingsButton(()=>{if(rightExpanded) showHotkeys=false;rightExpanded=!rightExpanded;Render();},rightExpanded,settingsMotion);
            root.Add(settings);
            followButton=Button("",()=>SetCameraFollow(!cameraFollow),"follow-toggle","follow-toggle");root.Add(followButton);
            followToast=Text("","follow-toast");followToast.name="follow-toast";followToast.pickingMode=PickingMode.Ignore;root.Add(followToast);
            if(!rightExpanded && confirmAction!=null && confirmButton!=null) {
                var confirm=Button(confirmButton.text+" · Enter",ConfirmCurrent,"floating-confirm","floating-confirm");root.Add(confirm);
            }
            if(renderedView.Winner.HasValue) {
                var victory=Text((renderedView.Winner==Goa2.Domain.Team.Blue ? "蓝队" : "红队")+"获胜","world-victory");victory.name="world-victory";victory.pickingMode=PickingMode.Ignore;victory.style.display=DisplayStyle.None;root.Add(victory);
                float until=board3DViewport.Presentation.Crowns.Select(c=>c.Started+2.4f).DefaultIfEmpty(Time.realtimeSinceStartup).Max();
                victory.schedule.Execute(()=>victory.style.display=DisplayStyle.Flex).StartingIn((long)(Mathf.Max(0,until-Time.realtimeSinceStartup)*1000));
            }
            RefreshFollowControls();
            var current=board;
            root.schedule.Execute(()=>{if(board==current) ApplyCameraFollow();}).StartingIn(20);
        }
        private void RenderHotkeys(VisualElement parent)
        {
            parent.Add(Text("热键与鼠标","section-title"));
            foreach(string line in new[]{"Enter / 小键盘Enter：确认当前选择","空格：开关视野跟随","1—4 / 小键盘1—4：切换本机测试席位","Q / E：向左 / 向右旋转30°","滚轮：以鼠标位置缩放","中键 / 空白处右键拖动：平移并取消跟随","左键英雄：开关技能圆环（合法目标优先）","右键英雄：查看卡牌、状态与永久加成","右键圆环技能：查看卡牌说明","Home：全图并取消跟随","F1：关键词说明","Esc：关闭英雄信息、术语、设置或卡牌预览","悬停卡牌：查看完整描述"}) parent.Add(Text(line,"body"));
            parent.Add(Text("输入文字或打开独立弹窗时，游戏热键暂停。相机跟随不改变操控身份。","muted"));
        }
    }
}
