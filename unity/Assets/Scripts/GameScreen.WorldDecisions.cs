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
        private string actionChoice="",worldContext="";
        private bool worldOptionsOpen,actionRingClosed;
        private int worldPage;
        private void ObserveWorldDecisions()
        {
            string context=renderedView.MatchId+":"+seat+":"+renderedView.Revision;
            if(context==worldContext)return;
            worldContext=context;actionChoice="";worldOptionsOpen=false;actionRingClosed=false;worldPage=0;
        }
        private void PickAction(string value)
        {
            if(!NetworkCanAct)return;
            Sound("select");debugTeleport=false;actionChoice=value;chosenCell=null;moveMode=value=="secondary" ? MoveMode.Secondary : value=="fast" ? MoveMode.Fast : (MoveMode?)null;
            wheelSeat=null;worldOptionsOpen=false;Render();
            if(value=="secondary")TutorialSignal("move-preview");
        }
        private void ReturnWorldChoice()
        {
            Sound("cancel");
            chosenCell=null;wheelPreview="";wheelDecline=false;
            if(renderedView.Phase==Phase.Action){actionChoice="";moveMode=null;passPending=false;actionRingClosed=false;}
            else worldOptionsOpen=!worldOptionsOpen;
            Render();
        }
        private List<Hex>? PrimaryPreviewCells(GameView view)
        {
            if(actionChoice!="primary" || view.Phase!=Phase.Action || view.ActiveSeat!=seat || view.PrimaryPreview==null)return null;
            return view.PrimaryPreview.Kind=="attack_target" ? view.Units.Where(u=>view.PrimaryPreview.Targets.Contains(u.Id)).Select(u=>u.Position).ToList() : view.PrimaryPreview.Cells;
        }
        private void RenderActionSource(VisualElement parent,GameView view)
        {
            if(view.ActiveSeat!=seat){parent.Add(Text("等待"+PlayerName(view.ActiveSeat!.Value)+"行动。"));return;}
            var played=view.Players[seat].Revealed.Single(c=>c.Zone==CardZone.PlayedUnresolved);
            var card=catalog.Card(played.CardId);
            if(actionChoice=="")
            {
                parent.Add(Text(card.Name+" · 选择行动"));
                var primary=Button(card.PrimaryCategory,()=>PickAction("primary"),"choice-button","begin-primary");
                primary.SetEnabled(view.CanBeginPrimary && !view.PrimaryImmediatelySkips);
                primary.tooltip=view.PrimaryImmediatelySkips ? "没有合法目标，不会消耗本次行动" : view.PrimaryRestriction!="" ? "受到技能限制" : "预览主要行动";parent.Add(primary);
                var secondary=Button("次要移动",()=>PickAction("secondary"),"choice-button","action-secondary");secondary.SetEnabled(view.CanStartSecondaryMoveWithPrelude || view.SecondaryMoves.Count>0);parent.Add(secondary);
                var fast=Button("快速移动",()=>PickAction("fast"),"choice-button","action-fast");fast.SetEnabled(view.CanStartFastMoveWithPrelude || view.FastMoves.Count>0);parent.Add(fast);
                parent.Add(Button("放弃此牌",()=>PickAction("pass"),"quiet-button","action-pass"));return;
            }
            if(actionChoice=="pass"){Confirm(parent,"确认放弃此牌行动",()=>Submit(CommandKind.Pass));return;}
            if(actionChoice=="primary")
            {
                var preview=view.PrimaryPreview;
                if(preview==null)
                {
                    parent.Add(Text("确认启动“"+card.Name+"”。开始后按牌文处理前置步骤；已发生效果不能撤回。"));
                    Confirm(parent,"启动主要行动",()=>Submit(CommandKind.BeginPrimary));return;
                }
                parent.Add(Text(preview.Kind=="attack_target" ? "选择高亮目标，再点击右侧确认按钮。确认前可返回选择其他行动。" : "选择高亮落点，再点击右侧确认。确认前可返回选择其他行动。"));
                if(chosenCell.HasValue && PrimaryPreviewCells(view)!.Contains(chosenCell.Value))
                {
                    var target=view.Units.FirstOrDefault(u=>u.Position==chosenCell.Value);
                    var kind=preview.Kind=="attack_target" ? CommandKind.CommitPrimaryAttack : preview.Kind=="effect_move" ? CommandKind.CommitPrimaryMove : CommandKind.CommitPrimaryPlacement;
                    Confirm(parent,preview.Kind=="attack_target" ? "确认攻击" : "确认落点",()=>Submit(kind,preview.Kind=="attack_target" ? target!.Id : "",destination:chosenCell!.Value));
                }
                if(preview.Optional && preview.Kind=="effect_move")parent.Add(Button("不移动，执行后续",()=>Submit(CommandKind.CommitPrimaryMove,"skip"),"quiet-button","primary-preview-skip"));
                return;
            }
            bool prelude=moveMode==MoveMode.Secondary ? view.CanStartSecondaryMoveWithPrelude : view.CanStartFastMoveWithPrelude;
            if(prelude){parent.Add(Text("本次移动有行动前能力。确认启动后先处理紫卡效果，之后选择落点。"));Confirm(parent,"启动移动前置流程",()=>Submit(CommandKind.Move,"begin",mode:moveMode!.Value));return;}
            parent.Add(Text("选择高亮落点。右侧确认前可以更换落点或返回选择行动。"));
            var moves=moveMode==MoveMode.Secondary ? view.SecondaryMoves : view.FastMoves;
            if(chosenCell.HasValue && moves.Any(m=>m.Destination==chosenCell.Value))Confirm(parent,"确认移动至 "+chosenCell.Value,()=>Submit(CommandKind.Move,destination:chosenCell!.Value,mode:moveMode!.Value));
        }
        private void RenderSidebar(VisualElement parent,GameView view)
        {
            parent.Add(Text("对局操作已迁至战场圆环与地图确认。","muted"));
            if(!NetworkMode && !TutorialActive)RenderDebugGuideShortcut(parent,view);
            if(view.CanUpgradeEngine)parent.Add(Button("采用当前规则",()=>Submit(CommandKind.UpgradeEngine,GameState.CurrentEngineVersion.ToString()),"quiet-button","upgrade-engine"));
            RenderActiveEffects(parent,view);RenderRecentEvents(parent,view);
        }
        private void BuildWorldDecisions()
        {
            flowDetails="";var view=renderedView;if(!DecisionFlow || board==null || showDebug && rightExpanded || debugAttack || debugTeleport)return;
            var source=new VisualElement();RenderDecisionSource(source,view);
            var labels=source.Query<Label>().ToList().Where(l=>l.GetFirstAncestorOfType<ScrollView>()==null && !InsideCardDetail(l)).Select(l=>l.text).Where(t=>!string.IsNullOrWhiteSpace(t)).ToList();
            flowDetails=string.Join("\n",labels);
            var options=source.Query<Button>().ToList().Where(b=>b!=confirmButton && b.text!="取消" && !InsideCardDetail(b)).ToList();
            bool cards=WheelDiscard(view) || view.Pending?.Kind=="defense" || WheelRecovery(view);
            if(cards)options.Clear();
            bool mine=view.Phase==Phase.Action ? view.ActiveSeat==seat : view.Pending!=null ? view.Pending.ChooserSeat==seat : true;
            bool geometry=LegalCells(view).Count>0 && !debugTeleport && !debugAttack;
            // All operational controls share one network gate, independently of settings.
            var layer=new VisualElement{name="world-decisions",pickingMode=PickingMode.Ignore};layer.StretchToParentSize();root.Add(layer);
            if(view.UpgradeOptions.Count>0)
            {
                // Upgrade ring is built after this detached decision source.
                return;
            }
            if(cards)return;
            if(view.Phase==Phase.Action && actionChoice=="" && actionRingClosed)return;
            if(wheelState.Discards.Count>0)return;
            bool expanded=!geometry || worldOptionsOpen;
            if(options.Count>0 && mine)
            {
                if(expanded)BuildContextRing(layer,options,view.Pending?.ChooserSeat ?? view.ActiveSeat ?? seat);
                else layer.Add(Button("步骤选项",()=>{worldOptionsOpen=true;Render();},"world-options-toggle","world-options-toggle"));
            }
            if(worldOptionsOpen && geometry)layer.Add(Button("继续选择地图目标",()=>{worldOptionsOpen=false;Render();},"world-options-toggle","world-options-close"));
        }
        private static bool InsideCardDetail(VisualElement element)
        {
            for(var p=element.parent;p!=null;p=p.parent)if(p.ClassListContains("card-detail"))return true;return false;
        }
        private void BuildContextRing(VisualElement layer,List<Button> options,int actor)
        {
            const int perPage=6;int pages=(options.Count+perPage-1)/perPage;worldPage=Mathf.Clamp(worldPage,0,pages-1);
            var frame=new VisualElement{name="action-wheel",pickingMode=PickingMode.Ignore};frame.AddToClassList("world-action-wheel");layer.Add(frame);AnchorWorldControl(frame,null,actor,430);
            frame.generateVisualContent+=ctx=>{var p=ctx.painter2D;p.strokeColor=new Color(.56f,.64f,.72f,.75f);p.lineWidth=5;p.BeginPath();p.Arc(new Vector2(215,215),155,0,360);p.Stroke();p.strokeColor=new Color(.2f,.45f,.7f,.3f);p.lineWidth=12;p.BeginPath();p.Arc(new Vector2(215,215),148,0,360);p.Stroke();};
            var shown=options.Skip(worldPage*perPage).Take(perPage).ToList();
            for(int i=0;i<shown.Count;i++)
            {
                var button=shown[i];button.RemoveFromHierarchy();button.AddToClassList("world-action-disc");if(button.tooltip=="")button.tooltip=button.text;if(button.text.Length>10)button.style.fontSize=20;float a=(-90+360f*i/shown.Count)*Mathf.Deg2Rad;
                button.style.left=215+Mathf.Cos(a)*155-65;button.style.top=215+Mathf.Sin(a)*155-65;frame.Add(button);
            }
            if(pages>1){var next=Button((worldPage+1)+" / "+pages+" ›",()=>{worldPage=(worldPage+1)%pages;Render();},"world-ring-page","world-ring-page");frame.Add(next);}
        }
        private void AnchorWorldControl(VisualElement element,Hex? cell,int actor,float size)
        {
            var currentBoard=board;void UpdateAnchor()
            {
                if(currentBoard==null || currentBoard!=board)return;
                var hero=renderedView.Units.FirstOrDefault(u=>u.Seat==actor);
                var point=cell.HasValue ? root.WorldToLocal(currentBoard.PanelCenter(cell.Value)) : hero!=null ? root.WorldToLocal(currentBoard.LocalToWorld(currentBoard.ProjectHero(hero.Position))) : new Vector2(root.contentRect.width*.56f,root.contentRect.height*.5f);
                bool regional=renderedView.Phase==Phase.RoundEnd || renderedView.Pending?.Kind=="minion_spawn" || renderedView.Pending?.Kind=="minion_return" || renderedView.Pending?.Kind=="round_minion_removal" || renderedView.Pending?.Kind=="action_minion_removal";
                if(!cell.HasValue && regional)
                {
                    var points=catalog.Cells.Where(c=>c.Region==renderedView.CombatRegion).Select(c=>root.WorldToLocal(currentBoard.PanelCenter(c.Position))).ToList();
                    if(points.Count>0)point=points.Aggregate(Vector2.zero,(a,b)=>a+b)/points.Count;
                }
                float scale=Mathf.Clamp(root.contentRect.height/1000f,.72f,1f);
                element.style.position=Position.Absolute;element.style.width=size;element.style.height=size;
                element.style.left=point.x-size*.5f;element.style.top=point.y-size*.5f-(cell.HasValue ? 55 : 0);element.style.scale=new Scale(new Vector3(scale,scale,1));
            }
            element.schedule.Execute(UpdateAnchor).Every(16);
        }
    }
}
