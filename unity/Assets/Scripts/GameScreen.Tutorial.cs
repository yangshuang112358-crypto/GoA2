#nullable enable
using System;
using System.IO;
using System.Linq;
using Goa2.Application;
using Goa2.Domain;
using Goa2.Infrastructure;
using Goa2.Presentation.UI3D;
using UnityEngine;
using UnityEngine.UIElements;
using UnityApplication = UnityEngine.Application;

namespace Goa2.Presentation
{
    public sealed partial class GameScreen
    {
        private TutorialCourse? tutorial;
        private TutorialProgress? tutorialProgress;
        private GameSession? tutorialPreviousSession;
        private int tutorialPreviousSeat;
        private bool tutorialMenu,tutorialCollapsed,tutorialHelp;
        private bool tutorialFreshSnapshot;
        private float tutorialNextBot;
        private string tutorialMessage="",tutorialObservedLine="";
        private bool TutorialActive=>tutorial!=null;
        private bool TutorialPresenting
        {
            get
            {
                if(StagePresenting || CombatPresenting || InitiativePresenting || wasCinematic)return true;
                var focus=CameraFollowPolicy.Target(renderedView,0);
                Vector3? destination=focus.HasValue?Board3DGeometry.World(focus.Value):(Vector3?)null;
                if(!destination.HasValue && (renderedView.Phase==Phase.RoundEnd || renderedView.RoundEndStage=="minion_battle"))
                {
                    var cells=catalog.Cells.Where(c=>c.Region==renderedView.CombatRegion).Select(c=>Board3DGeometry.World(c.Position)).ToArray();
                    if(cells.Length>0)destination=new Vector3((cells.Min(p=>p.x)+cells.Max(p=>p.x))*.5f,0,(cells.Min(p=>p.z)+cells.Max(p=>p.z))*.5f);
                }
                return mainFlow && cameraFollow && destination.HasValue && (board3DViewport.Focus-destination.Value).sqrMagnitude>.025f;
            }
        }
        // Never reuse hotseat, invitations, or authoritative room save paths.
        private string TutorialPath
        {
            get
            {
                var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"-goaTutorialProgress");
                return i>=0 && i+1<args.Length ? Path.GetFullPath(args[i+1]) : Path.Combine(UnityApplication.persistentDataPath,"tutorial","progress-v1.json");
            }
        }
        private void OpenTutorialMenu()
        {
            if(NetworkMode)return;
            tutorialProgress ??= TutorialProgress.Read(TutorialPath,out tutorialMessage);
            if(tutorial==null){tutorialPreviousSession=session;tutorialPreviousSeat=seat;tutorial=new TutorialCourse(catalog,tutorialProgress.ResumeChapter);session=tutorial.Session;seat=0;}
            tutorialMenu=true;rightExpanded=false;showDebug=false;ResetTutorialView();Render();
        }
        private void StartTutorialChapter(int chapter)
        {
            if(NetworkMode)return;
            CompleteTutorialChapter();
            tutorialProgress ??= TutorialProgress.Read(TutorialPath,out tutorialMessage);
            if(tutorial==null){tutorialPreviousSession=session;tutorialPreviousSeat=seat;tutorial=new TutorialCourse(catalog,chapter);}else tutorial.Start(chapter);
            session=tutorial.Session;seat=0;tutorialMenu=false;tutorialCollapsed=false;tutorialMessage="";
            tutorialProgress.ResumeChapter=chapter;SaveTutorialProgress();ResetTutorialView();Render();
        }
        private void ResetTutorialView()
        {
            scenario=null;rightExpanded=false;showDebug=false;showHotkeys=false;galleryOpen=false;historyOpen=false;publicCardsOpen=false;newMatchPending=false;debugPresetsOpen=false;
            ClearPending();wheelState.Discards.Clear();wheelContext="";worldContext="";wheelSeat=null;wheelPreview="";browsingWheels.Clear();
            cameraFollow=true;mainFlow=true;decisionAnimating=false;tutorialNextBot=Time.realtimeSinceStartup+1.5f;tutorialObservedLine="";tutorialFreshSnapshot=true;
        }
        private void SaveTutorialProgress()
        {
            try{tutorialProgress?.Write(TutorialPath);}catch(Exception e){tutorialMessage="教程进度暂时无法保存："+e.Message;Debug.LogWarning(tutorialMessage);}
        }
        private void ExitTutorial()
        {
            if(tutorial==null)return;SaveTutorialProgress();tutorial=null;tutorialMenu=false;tutorialCollapsed=false;ResetTutorialView();
            if(tutorialPreviousSession!=null){session=tutorialPreviousSession;seat=tutorialPreviousSeat;tutorialPreviousSession=null;Render();}
            else UnityApplication.Quit();
        }
        private void TutorialSignal(string id)
        {
            if(tutorial==null || tutorialMenu)return;
            if(tutorial.Signal(id)){root.Q("tutorial-coach")?.RemoveFromHierarchy();BuildTutorial();}
        }
        private void TutorialReadNext()
        {
            if(tutorial==null || tutorialMenu || StagePresenting || CombatPresenting)return;
            if(tutorial.Acknowledge()){CompleteTutorialChapter();tutorialNextBot=Time.realtimeSinceStartup+1.5f;Render();}
        }
        private void CompleteTutorialChapter()
        {
            if(tutorial?.Complete==true && tutorialProgress!=null && !tutorialProgress.Completed[tutorial.Chapter]){tutorialProgress.Completed[tutorial.Chapter]=true;SaveTutorialProgress();}
        }
        private void UpdateTutorial()
        {
            if(tutorial==null || tutorialMenu)return;
            bool presenting=TutorialPresenting;
            var coach=root.Q("tutorial-coach");if(coach!=null)coach.style.display=presenting?DisplayStyle.None:DisplayStyle.Flex;
            if(presenting || decisionAnimating || rightExpanded || galleryOpen || historyOpen || tutorialHelp || tutorialCollapsed)return;
            if(tutorial.Complete)
            {
                CompleteTutorialChapter();
                return;
            }
            if(tutorialObservedLine!=tutorial.Line.Id){tutorialObservedLine=tutorial.Line.Id;tutorialNextBot=Time.realtimeSinceStartup+1.5f;}
            if(Time.realtimeSinceStartup<tutorialNextBot)return;
            tutorialNextBot=Time.realtimeSinceStartup+1.5f;
            if(tutorial.TickOpponent()){CompleteTutorialChapter();notice="陪练完成操作。";Render();}
            else if(tutorial.Error!="" && tutorialMessage!=tutorial.Error){tutorialMessage=tutorial.Error;Render();}
        }
        private VisualElement TutorialPanel(string name)
        {
            var panel=new VisualElement{name=name};panel.style.backgroundColor=new Color(.04f,.065f,.08f,.96f);
            panel.style.paddingLeft=18;panel.style.paddingRight=18;panel.style.paddingTop=14;panel.style.paddingBottom=14;
            panel.style.borderTopWidth=2;panel.style.borderBottomWidth=2;panel.style.borderLeftWidth=2;panel.style.borderRightWidth=2;
            panel.style.borderTopColor=panel.style.borderBottomColor=panel.style.borderLeftColor=panel.style.borderRightColor=new Color(.59f,.46f,.26f);
            panel.style.borderTopLeftRadius=14;panel.style.borderTopRightRadius=14;panel.style.borderBottomLeftRadius=14;panel.style.borderBottomRightRadius=14;
            return panel;
        }
        private static Label TutorialText(string text,int size=20, bool gold=false)
        {
            var label=new Label(text);label.style.fontSize=size;label.style.whiteSpace=WhiteSpace.Normal;label.style.color=gold?new Color(1,.82f,.5f):new Color(.87f,.9f,.91f);label.style.marginBottom=10;return label;
        }
        private void BuildTutorial()
        {
            if(tutorialHelp){BuildTutorialHelp();return;}
            if(tutorial==null){BuildFirstMatchHint();return;}
            if(tutorialMenu)
            {
                var shade=new VisualElement{name="tutorial-menu"};shade.StretchToParentSize();shade.style.backgroundColor=new Color(.015f,.025f,.04f,.9f);root.Add(shade);
                var panel=TutorialPanel("tutorial-chapters");panel.style.position=Position.Absolute;panel.style.width=720;panel.style.maxWidth=new Length(94,LengthUnit.Percent);
                panel.style.left=new Length(50,LengthUnit.Percent);panel.style.top=35;panel.style.translate=new Translate(new Length(-50,LengthUnit.Percent),0);panel.style.bottom=35;shade.Add(panel);
                panel.Add(TutorialText("新手教程 · 从一张牌开始",30,true));panel.Add(TutorialText("单人练习 · 目标8—10分钟 · 可分章重练\n继续时从所选章节起点开始，真实对局与联机存档不受影响。",19));
                panel.Add(Button("继续第 "+(tutorialProgress!.ResumeChapter+1)+" 章",()=>StartTutorialChapter(tutorialProgress.ResumeChapter),"primary-button","tutorial-continue"));
                var list=new ScrollView();list.style.flexGrow=1;panel.Add(list);
                for(int i=0;i<TutorialCourse.Chapters.Length;i++){int chapter=i;var button=Button((tutorialProgress.Completed[i]?"✓  ":"")+(i+1)+"  "+TutorialCourse.Chapters[i],()=>StartTutorialChapter(chapter),"choice-button","tutorial-chapter-"+i);button.style.fontSize=22;button.style.minHeight=42;list.Add(button);}
                if(tutorialMessage!="")panel.Add(TutorialText(tutorialMessage,18));
                panel.Add(Button(tutorialPreviousSession!=null?"返回原对局":"关闭教程",ExitTutorial,"quiet-button","tutorial-exit"));return;
            }
            var coach=TutorialPanel("tutorial-coach");coach.style.position=Position.Absolute;
            if(tutorial.Line.Id=="initiative")coach.style.right=16;else coach.style.left=16;
            coach.style.top=76;coach.style.width=Screen.width<1400?344:370;coach.style.maxHeight=Screen.height-94;root.Add(coach);
            var heading=new VisualElement();heading.style.flexDirection=FlexDirection.Row;coach.Add(heading);
            var chapterTitle=TutorialText((tutorial.Chapter+1)+" / "+TutorialCourse.Chapters.Length+"  "+TutorialCourse.Chapters[tutorial.Chapter],17,true);chapterTitle.style.flexGrow=1;heading.Add(chapterTitle);
            heading.Add(Button(tutorialCollapsed?"展开":"收起",()=>{tutorialCollapsed=!tutorialCollapsed;Render();},"quiet-button","tutorial-collapse"));
            if(tutorialCollapsed)return;
            var content=new ScrollView{name="tutorial-reading"};content.style.flexShrink=1;coach.Add(content);
            var title=TutorialText(tutorial.Line.Title,22,true);title.name="tutorial-title";content.Add(title);
            if(!tutorial.Complete && tutorial.Line.Badge!="")
            {
                var card=catalog.Card(tutorial.Line.Card);var disc=new SkillDisc(card.Color,card,renderedView.Players[0],CardZone.InHand,false,false,new SkillWheelState.Motion(),0,()=>{},()=>{});
                disc.name="tutorial-card-sample";disc.style.position=Position.Relative;disc.style.alignSelf=Align.Center;disc.style.flexShrink=0;content.Add(disc);
                disc.Inspect=at=>ShowSkillInfo(card,disc,at);disc.RegisterCallback<PointerLeaveEvent>(_=>{if(skillPopupOwner==disc)HideSkillInfo();});
                var marker=disc.Q("badge-"+tutorial.Line.Badge);
                if(marker!=null){marker.style.borderTopWidth=marker.style.borderBottomWidth=marker.style.borderLeftWidth=marker.style.borderRightWidth=2;marker.style.borderTopColor=marker.style.borderBottomColor=marker.style.borderLeftColor=marker.style.borderRightColor=new Color(1,.82f,.32f);marker.schedule.Execute(()=>marker.style.opacity=.78f+.22f*Mathf.Sin(Time.realtimeSinceStartup*3)).Every(32);}
            }
            var body=TutorialText(tutorial.Complete?"本章练习完成。下一章使用新的教学局面。":tutorial.Description,Screen.height<800?18:20);body.name="tutorial-body";content.Add(body);
            if(tutorialMessage!=""){var error=TutorialText(tutorialMessage,18);error.style.color=new Color(1,.55f,.5f);content.Add(error);}
            if(tutorial.Complete)coach.Add(Button(tutorial.Chapter+1<TutorialCourse.Chapters.Length?"下一章":"完成教程，返回目录",()=>{if(tutorial.Chapter+1<TutorialCourse.Chapters.Length)StartTutorialChapter(tutorial.Chapter+1);else OpenTutorialMenu();},"primary-button","tutorial-next"));
            else if(tutorial.Line.Reading)coach.Add(Button("继续",TutorialReadNext,"primary-button","tutorial-next"));
            var actions=new VisualElement();actions.style.flexDirection=FlexDirection.Row;coach.Add(actions);
            actions.Add(Button("重试",()=>StartTutorialChapter(tutorial.Chapter),"quiet-button","tutorial-retry"));
            actions.Add(Button("目录",OpenTutorialMenu,"quiet-button","tutorial-menu-open"));
            actions.Add(Button("回到行动",ReturnMainFlow,"quiet-button","tutorial-focus"));
            coach.Query<Button>().ForEach(b=>{b.style.fontSize=19;b.style.minHeight=36;b.style.paddingTop=5;b.style.paddingBottom=5;});
            if(TutorialPresenting)coach.style.display=DisplayStyle.None;
        }
        private void BuildFirstMatchHint()
        {
            tutorialProgress ??= TutorialProgress.Read(TutorialPath,out tutorialMessage);
            if(!tutorialProgress.Hints || !tutorialProgress.Completed.All(x=>x) || rightExpanded || galleryOpen || historyOpen)return;
            string key=renderedView.Pending?.Kind ?? (renderedView.Phase==Phase.Planning?"planning":renderedView.Phase==Phase.HeroSelection?"heroes":renderedView.Phase==Phase.Deployment?"deployment":"");
            string hint=key switch {
                "heroes"=>"这是选英雄环节。决策币决定先选队伍，双方按1—2—1顺序选；点英雄查看介绍，本队轮到时选择。",
                "deployment"=>"双方队长分别安排本队两名英雄出生。队员等待队长选择；进入暗选后各自操控自己。",
                "planning"=>"四人各自确认后才揭牌。拖动镜头可自由观看；空格回到自己的选牌。",
                "initiative"=>"本队有多张牌先攻并列，轮到指定队长决定先执行哪张。跨队先后由决策币决定。",
                "hero_respawn"=>"被击败后，到自己的后续行动时选择合法出生点，然后继续处理已出的牌。",
                "forced_discard"=>"这是牌文要求的弃牌。先读上方来源效果，再选择手牌；是否允许选择被击败，以当前选项为准。",
                "defense"=>"防御会消耗一张手牌。先看公开攻击算式，再比较可用牌的防御值与牌文。",_=>""};
            if(hint=="" || tutorialProgress.SeenHints.Contains(key))return;
            var box=TutorialPanel("first-match-hint");box.style.position=Position.Absolute;box.style.left=300;box.style.top=15;box.style.width=470;root.Add(box);
            box.Add(TutorialText(hint,18));box.Add(Button("知道了",()=>{tutorialProgress.SeenHints=tutorialProgress.SeenHints.Concat(new[]{key}).ToArray();SaveTutorialProgress();Render();},"quiet-button","first-hint-close"));
            box.schedule.Execute(()=>box.style.display=StagePresenting || CombatPresenting || InitiativePresenting?DisplayStyle.None:DisplayStyle.Flex).Every(50);
        }
        private void ToggleTutorialHints()
        {
            tutorialProgress ??= TutorialProgress.Read(TutorialPath,out tutorialMessage);
            tutorialProgress.Hints=!tutorialProgress.Hints;SaveTutorialProgress();Render();
        }
        private void BuildTutorialHelp()
        {
            var shade=new VisualElement{name="tutorial-help"};shade.StretchToParentSize();shade.style.backgroundColor=new Color(0,0,0,.72f);root.Add(shade);
            var panel=TutorialPanel("tutorial-help-content");panel.style.position=Position.Absolute;panel.style.left=new Length(20,LengthUnit.Percent);panel.style.right=new Length(20,LengthUnit.Percent);panel.style.top=45;panel.style.bottom=45;shade.Add(panel);
            panel.Add(TutorialText("操作与规则速查",28,true));var scroll=new ScrollView();scroll.style.flexGrow=1;panel.Add(scroll);
            scroll.Add(TutorialText("移动：左上鞋子；防御：右上盾牌；主要行动：左下；范围／距离：右下；先攻：底部，大者先行动。\n\n主要行动与次要移动二选一；次要防御在被攻击时使用。绿字增加、红字减少，具体牌文限制始终生效。\n\n空格／右下：回到当前行动；Enter：执行当前确认；滚轮缩放；中键拖动。预选可撤回，已提交效果不能撤回。\n\n同队先攻并列由队长选顺序；跨队并列按决策币。快速移动的起点、终点区域不能有敌方英雄。被击败者在自己的后续行动时选择出生点。\n\n水晶因本队英雄被击败而损失该英雄实际等级的生命。推进累计皇冠或抵达敌方泉水也能获胜。真实等级、金币扣费、卡牌升级均在轮末处理。\n\n联机中帮助只在本机显示，不会暂停其他玩家。",20));
            panel.Add(Button("关闭帮助",()=>{tutorialHelp=false;Render();},"primary-button","tutorial-help-close"));
        }
    }
}
