#nullable enable
using Goa2.Domain;
using System.Linq;
using Goa2.Presentation.UI3D;
using UnityEngine;
using UnityEngine.UIElements;
namespace Goa2.Presentation
{
    public sealed partial class GameScreen
    {
        private void PrepareFourTieDemo()
        {
            NewMatch();Submit(CommandKind.DebugPrepare,"brogan,shargatha,arien,wasp");
            Submit(CommandKind.DebugSetCoin,"blue");
            seat=0;Submit(CommandKind.SelectCard,"brogan-00-猛攻");
            seat=1;Submit(CommandKind.SelectCard,"shargatha-00-反击");
            seat=2;Submit(CommandKind.SelectCard,"arien-00-华丽刀锋");
            seat=3;rightExpanded=false;showDebug=false;mainFlow=true;cameraFollow=true;
            guideMatchId=session.View(null).MatchId;guideTitle="四人同先攻：双队队长选择与交替翻币";
            guideInstructions="1. 当前黄蜂选择蓝牌【静电封锁】，四张牌均为同一先攻，立即揭示。\n2. 先看蓝币说明，再看翻至红面；按1切到蓝队队长布罗根，在左侧行动石板选择布罗根或艾瑞恩先行。\n3. 该英雄完成行动或放弃后，红队获得先行权；按2切到红队队长夏尔加萨，选择夏尔加萨或黄蜂。\n4. 其余两张同先攻牌继续按决策币决定顺序。可分别选择不同同队英雄，观察已行动者不会再次执行。";
            notice=guideTitle+"：黄蜂选择【静电封锁】开始。完整步骤见设置→最近记录。";
            ClearPending();Render();
        }
        private bool initiativeWasPresenting;
        private long initiativeSequence=-1;
        private bool InitiativePresenting=>board3DViewport.Presentation.Conflict(Time.realtimeSinceStartup)!=null;
        private void BuildInitiativePresentation()
        {
            var beat=board3DViewport.Presentation.Conflict(Time.realtimeSinceStartup);if(beat==null)return;
            string TeamName(Team team)=>team==Team.Blue?"蓝队":"红队";
            var box=new Label{name="initiative-explanation",pickingMode=PickingMode.Ignore};
            box.style.position=Position.Absolute;box.style.left=Length.Percent(50);box.style.top=38;
            box.style.translate=new Translate(Length.Percent(-50),0);box.style.width=Mathf.Min(700,Screen.width-60);box.style.minHeight=78;
            box.style.fontSize=24;box.style.whiteSpace=WhiteSpace.Normal;box.style.unityTextAlign=TextAnchor.MiddleCenter;
            box.style.paddingTop=12;box.style.paddingBottom=12;box.style.paddingLeft=18;box.style.paddingRight=18;
            box.style.backgroundColor=new Color(.035f,.045f,.065f,.94f);box.style.color=new Color(1,.84f,.48f);
            void Refresh(){bool flipped=Time.realtimeSinceStartup>=beat.Flip+.32f;box.text="先攻相同 · 本次由"+TeamName(beat.Winner)+"先行\n"+(flipped?"决策币已翻为"+TeamName(beat.Next)+"，供下一次先攻冲突使用。":"因为本次决策币为"+TeamName(beat.Winner)+"；随后翻面交出下次决策权。");}
            Refresh();box.schedule.Execute(()=>{Refresh();box.style.display=Time.realtimeSinceStartup>=beat.Start?DisplayStyle.Flex:DisplayStyle.None;}).Every(32);root.Add(box);
            root.Q("world-decisions")?.SetEnabled(false);root.Q("decision-dock")?.SetEnabled(false);
            root.Q("upgrade-zone")?.SetEnabled(false);skillWheel?.SetEnabled(false);confirmAction=null;confirmButton=null;
        }
        private void UpdateInitiativePresentation()
        {
            bool active=InitiativePresenting;
            long sequence=board3DViewport.Presentation.Conflict(Time.realtimeSinceStartup)?.Sequence ?? -1;
            bool changed=sequence!=initiativeSequence;initiativeSequence=sequence;
            if(initiativeWasPresenting && (!active || changed))Render();
            if(active && cameraFollow)ApplyCameraFollow();
            initiativeWasPresenting=active;
        }
    }
}
