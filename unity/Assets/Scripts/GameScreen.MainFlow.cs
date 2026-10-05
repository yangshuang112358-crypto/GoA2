#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Goa2.Domain;
using Goa2.Presentation.UI3D;
using UnityEngine;
using UnityEngine.UIElements;
namespace Goa2.Presentation
{
    public sealed partial class GameScreen
    {
        private bool mainFlow=true,decisionAnimating;
        private string flowMatch="",flowDetails="";
        private readonly HashSet<int> browsingWheels=new HashSet<int>();
        private VisualElement? decisionDock;
        private void ObserveMainFlow()
        {
            if(flowMatch==renderedView.MatchId)return;
            board3DViewport.MinionPreviewPose="";
            flowMatch=renderedView.MatchId;mainFlow=true;cameraFollow=true;browsingWheels.Clear();decisionAnimating=false;
        }
        private void LeaveMainFlow()
        {
            if(!mainFlow)return;
            if(wheelSeat.HasValue){browsingWheels.Add(wheelSeat.Value);lastBrowsedSeat=wheelSeat;}
            mainFlow=false;cameraFollow=false;cameraFocusVersion++;board?.StopFollowing();
            ClearPending();actionRingClosed=true;wheelSeat=null;wheelPreview="";wheelDecline=false;
            confirmButton=null;confirmAction=null;
        }
        private void ReturnMainFlow()
        {
            if(decisionAnimating)return;
            while(wheelState.Discards.Count>0 && Time.realtimeSinceStartup-wheelState.Discards.Peek().Start>2.4f)wheelState.Discards.Dequeue();
            browsingWheels.Clear();mainFlow=true;cameraFollow=true;cameraFocusVersion++;
            ClearPending();wheelContext="";wheelSeat=null;wheelPreview="";wheelDecline=false;
            bottomExpanded=true;HideHeroHover();HideSkillInfo();Render();ApplyCameraFollow(true);
        }
        private void BuildMainFlowStatus()
        {
            var panel=Button("",ReturnMainFlow,"main-flow-status","main-flow-status");
            panel.Add(Text("第 "+renderedView.Round+" 轮 · 第 "+renderedView.Turn+" / 4 回合","flow-round"));
            string Hero(int who)=>HeroName(renderedView.Players.FirstOrDefault(p=>p.Seat==who)?.HeroId);
            panel.Add(Text(MainFlowPolicy.Summary(renderedView,Hero),"flow-summary"));
            bool mine=MainFlowPolicy.NeedsInput(renderedView,seat) && NetworkCanAct;
            string instruction=mine ? MainFlowPolicy.Instruction(renderedView,seat) : MainFlowPolicy.Summary(renderedView,Hero);
            if(mine && renderedView.Phase==Phase.Action && actionChoice!="")instruction=actionChoice=="pass"?"放弃此牌":chosenCell.HasValue?"决定本次行动目标":actionChoice=="primary"?"执行主要行动":"选择移动落点";
            if(mine && (wheelPreview!="" || wheelDecline))instruction=wheelDecline ? renderedView.Pending?.Kind=="defense" ? "不防御" : "不弃牌，选择被击败" : MainFlowPolicy.Step(renderedView.Pending?.Kind ?? "");
            if(mine && renderedView.UpgradeOptions.Count>0)instruction="选择升级技能 · 还可升级 "+UpgradeWheelLayout.Remaining(renderedView,seat)+" 次";
            if(NetworkMode && !NetworkCanAct)instruction=networkBusy?"正在等待操作回执":uncertainCommand!=""?"操作结果待核实，请在设置中查询原操作":"连接已中断，请在设置中重连";
            var prompt=Text((mine?"到你行动 · ":"")+instruction,"flow-prompt");prompt.EnableInClassList("flow-mine",mine);panel.Add(prompt);
            if(!mainFlow)panel.Add(Text("回到当前行动","flow-return"));
            panel.tooltip="点击或空格：回到当前行动；暗选时返回自己的选牌界面"+(flowDetails==""?"":"\n\n"+flowDetails);
            panel.Query<VisualElement>().ForEach(e=>{if(e!=panel)e.pickingMode=PickingMode.Ignore;});root.Add(panel);
        }
        private bool CanWithdrawPreview()=>chosenHero!=null || chosenCell.HasValue || wheelPreview!="" || wheelDecline || upgradeCardId!="" || actionChoice!="" || moveMode.HasValue || goldTransferAmount>=0 || deploymentSeat>=0 || (renderedView.Pending?.Kind=="primary_option" && primaryOptionChoice!="");
        private void WithdrawPreview()
        {
            // Only unsubmitted UI state is cancelled. Never send a rollback command.
            ClearPending();primaryOptionChoice="";wheelPreview="";wheelDecline=false;actionRingClosed=false;Sound("cancel");Render();
        }
        private void BuildDecisionDock()
        {
            decisionDock=null;
            if(!mainFlow || wheelState.Discards.Count>0 || !NetworkCanAct || ScenarioRunning)return;
            var captured=confirmAction;bool canConfirm=captured!=null && confirmButton!=null && confirmButton.enabledSelf;
            bool canBack=CanWithdrawPreview();
            if(!canConfirm && !canBack)return;
            if(confirmButton!=null)confirmButton.style.display=DisplayStyle.None;
            var dock=new VisualElement{name="decision-dock"};dock.AddToClassList("decision-dock");root.Add(dock);decisionDock=dock;
            string caption=confirmButton?.text ?? "确认";
            var yes=new DecisionStoneButton(true,()=>AnimateDecision(captured),caption){name="flow-confirm"};
            var back=new DecisionStoneButton(false,()=>AnimateDecision(WithdrawPreview),"撤回选择"){name="flow-withdraw"};
            dock.Add(yes);dock.Add(back);yes.SetEnabled(canConfirm);back.SetEnabled(canBack);
            confirmButton=yes;confirmAction=()=>AnimateDecision(captured);
            if(decisionAnimating)dock.SetEnabled(false);
            float started=Time.realtimeSinceStartup;
            dock.schedule.Execute(()=>{if(decisionAnimating)return;float t=Mathf.Clamp01((Time.realtimeSinceStartup-started)/.26f);dock.style.translate=new Translate(240*Mathf.Pow(1-t,3),0);dock.style.opacity=t;}).Every(16);
        }
        private void AnimateDecision(Action? action)
        {
            if(action==null || decisionAnimating || !NetworkCanAct)return;
            decisionAnimating=true;confirmAction=null;decisionDock?.SetEnabled(false);
            var dock=decisionDock;long revision=renderedView.Revision;string match=renderedView.MatchId,selection=DecisionSelectionKey();int actor=seat;
            float start=Time.realtimeSinceStartup;
            var animation=root.schedule.Execute(()=>{float t=Mathf.Clamp01((Time.realtimeSinceStartup-start)/.16f);if(dock!=null){dock.style.translate=new Translate(240*t*t,0);dock.style.opacity=1-t;}}).Every(16);
            root.schedule.Execute(()=>{animation.Pause();decisionAnimating=false;if(mainFlow && seat==actor && renderedView.MatchId==match && renderedView.Revision==revision && selection==DecisionSelectionKey() && NetworkCanAct)action();else Render();}).StartingIn(170);
        }
        private string DecisionSelectionKey()=>string.Join("|",chosenHero,chosenCell?.ToString(),moveMode,initiativeSeat,passPending,deploymentSeat,
            defenseCardId,discardCardId,declineDefensePending,declineRetaliationPending,goldTransferTarget,goldTransferAmount,
            upgradeCardId,actionChoice,wheelPreview,wheelDecline,primaryOptionChoice,returnUnitId,spawnUnitId);
    }
}
