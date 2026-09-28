#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Goa2.Domain;
using Goa2.Presentation.UI3D;
using UnityEngine;
using UnityEngine.UIElements;
namespace Goa2.Presentation {
 public sealed partial class GameScreen {
  private readonly SkillWheelState wheelState=new SkillWheelState();
  private SkillWheel? skillWheel;private VisualElement? wheelFrame;
  private float wheelOpened;private bool wheelNeedsFocus;private float wheelFocusedBeat=-1;
  private int? wheelSeat;private string wheelContext="",wheelPreview="";private bool wheelDecline;
  private VisualElement? skillPopup;
  private static bool WheelDiscard(GameView v)=>v.Pending?.Kind=="forced_discard" || v.Pending?.Kind=="optional_discard" || v.Pending?.Kind=="minion_protection";
  private List<string> WheelChoices(GameView v)=>v.Pending?.Kind=="forced_discard" ? v.ForcedDiscardCards : v.Pending?.Kind=="optional_discard" ? v.OptionalDiscardCards : v.Pending?.Kind=="minion_protection" ? v.MinionProtectionCards : new List<string>();
  private void ObserveWheel() {
   if(wheelState.Observe(renderedView,Time.realtimeSinceStartup)){wheelContext="";wheelSeat=null;wheelPreview="";wheelDecline=false;wheelFocusedBeat=-1;}
   string context=renderedView.MatchId+":"+seat+":"+renderedView.Round+":"+renderedView.Turn+":"+renderedView.Phase+":"+renderedView.Pending?.Id;
   if(context!=wheelContext){wheelOpened=Time.realtimeSinceStartup;wheelContext=context;wheelPreview="";wheelDecline=false;wheelSeat=renderedView.Phase==Phase.Planning ? seat : WheelDiscard(renderedView) && renderedView.Pending!.ChooserSeat==seat ? seat : (int?)null;wheelNeedsFocus=WheelDiscard(renderedView) && wheelSeat.HasValue;}
   if(wheelState.Discards.Count>0){var beat=wheelState.Discards.Peek();wheelSeat=beat.Seat;if(wheelFocusedBeat!=beat.Start){wheelFocusedBeat=beat.Start;wheelNeedsFocus=true;}}
  }
  private void ToggleHeroWheel(int target) {
   if(wheelState.Discards.Count>0)return;
   HideHeroHover();skillPopup?.RemoveFromHierarchy();skillPopup=null;
   if(wheelSeat==target){wheelSeat=null;wheelPreview="";wheelDecline=false;if(confirmButton==skillWheel?.Confirm){confirmButton=null;confirmAction=null;}skillWheel?.Collapse();return;}
   wheelOpened=Time.realtimeSinceStartup;wheelSeat=target;wheelPreview="";wheelDecline=false;Render();
   var unit=renderedView.Units.FirstOrDefault(u=>u.Seat==target);
   if(unit!=null)root.schedule.Execute(()=>board?.FollowAt(Board3DGeometry.World(unit.Position),board3DViewport.Enabled ? 3f : 2.6f)).StartingIn(30);
  }
  private void WheelPick(string id) {
   if(!NetworkCanAct || wheelState.Discards.Count>0 || wheelSeat!=seat)return;
   if(renderedView.Phase==Phase.Planning && !renderedView.Players[seat].Confirmed){
    var c=renderedView.OwnCards.Single(x=>x.CardId==id);
    if(c.Zone==CardZone.Selected)Submit(CommandKind.CancelCardSelection);else if(c.Zone==CardZone.InHand)Submit(CommandKind.SelectCard,id);return;
   }
   if(WheelChoices(renderedView).Contains(id)){wheelDecline=false;wheelPreview=wheelPreview==id ? "" : id;Render();}
  }
  private void WheelConfirm() {
   if(!NetworkCanAct || wheelState.Discards.Count>0 || wheelSeat!=seat)return;
   if(renderedView.Phase==Phase.Planning){if(!renderedView.QuickSelection && !renderedView.Players[seat].Confirmed && renderedView.OwnCards.Any(c=>c.Zone==CardZone.Selected))Submit(CommandKind.ConfirmCard);return;}
   if(wheelDecline && renderedView.CanDeclineRetaliationDiscard){wheelDecline=false;Submit(CommandKind.DeclineRetaliationDiscard);return;}
   if(!WheelChoices(renderedView).Contains(wheelPreview))return;
   var kind=renderedView.Pending!.Kind=="forced_discard" ? CommandKind.ForcedDiscard : renderedView.Pending.Kind=="optional_discard" ? CommandKind.ChooseOptionalDiscard : CommandKind.ChooseMinionProtection;
   string id=wheelPreview;wheelPreview="";Submit(kind,id);
  }
  private void WheelAlternative() {
   if(!NetworkCanAct || wheelSeat!=seat || wheelState.Discards.Count>0)return;
   if(renderedView.Pending?.Kind=="optional_discard")Submit(CommandKind.ChooseOptionalDiscard,"skip");
   else if(renderedView.Pending?.Kind=="minion_protection")Submit(CommandKind.ChooseMinionProtection,"skip");
   else if(renderedView.CanDeclineRetaliationDiscard){wheelPreview="";wheelDecline=!wheelDecline;Render();}
  }
  private void ShowSkillInfo(CardDefinition? card) {
   if(card==null)return;HideHeroHover();skillPopup?.RemoveFromHierarchy();
   var popup=Box("hero-hover");popup.name="skill-description";skillPopup=popup;popup.style.position=Position.Absolute;popup.style.width=Mathf.Min(580,Screen.width-48);popup.style.maxHeight=Screen.height-48;popup.style.right=24;popup.style.top=24;
   popup.Add(Button("关闭",()=>{skillPopup?.RemoveFromHierarchy();skillPopup=null;},"quiet-button"));var scroll=new ScrollView();scroll.style.flexShrink=1;scroll.style.minHeight=0;popup.Add(scroll);RenderCardDetail(scroll,card,false);root.Add(popup);
  }
  private void BuildSkillWheel() {
   skillWheel=null;wheelFrame=null;if(board==null || !wheelSeat.HasValue)return;
   int target=wheelSeat.Value;var player=renderedView.Players.Single(p=>p.Seat==target);
   var unit=renderedView.Units.FirstOrDefault(u=>u.Seat==target);Hex location;if(unit!=null)location=unit.Position;else if(!wheelState.LastPositions.TryGetValue(target,out location))return;
   bool beat=wheelState.Discards.Count>0;var eventBeat=beat ? wheelState.Discards.Peek() : null;
   bool own=target==seat && !beat,planning=renderedView.Phase==Phase.Planning;
   bool canConfirm=own && (planning ? !renderedView.QuickSelection && !player.Confirmed && renderedView.OwnCards.Any(c=>c.Zone==CardZone.Selected) : WheelChoices(renderedView).Contains(wheelPreview) || wheelDecline);
   string caption=beat ? "弃牌" : canConfirm ? wheelDecline ? "确认被击败？" : "确认？" : own && planning ? player.Confirmed ? "已确认" : "选牌" : WheelDiscard(renderedView) ? "弃牌" : "查看";
   var frame=new VisualElement{pickingMode=PickingMode.Ignore,name="skill-wheel-anchor"};frame.style.position=Position.Absolute;frame.style.width=520;frame.style.height=520;frame.style.transformOrigin=new TransformOrigin(0,0,0);board.Add(frame);wheelFrame=frame;
   var wheel=new SkillWheel(player.Team,caption,WheelConfirm,WheelAlternative);skillWheel=wheel;wheel.ResumeOpen(beat ? eventBeat!.Start : wheelOpened);frame.Add(wheel);wheel.Confirm.SetEnabled(canConfirm);wheel.Confirm.style.display=canConfirm?DisplayStyle.Flex:DisplayStyle.None;
   bool alt=own && (renderedView.Pending?.Kind=="optional_discard" || renderedView.Pending?.Kind=="minion_protection" || renderedView.CanDeclineRetaliationDiscard);
   wheel.Alternative.style.display=alt ? DisplayStyle.Flex:DisplayStyle.None;wheel.Alternative.text=renderedView.CanDeclineRetaliationDiscard ? "不弃牌，选择被击败" : "不弃牌，继续";
   if(canConfirm){confirmButton=wheel.Confirm;confirmAction=WheelConfirm;}
   for(int i=0;i<HeroPlate.Colors.Length;i++){
    string color=HeroPlate.Colors[i];var known=SkillWheelState.KnownCard(catalog,renderedView,seat,target,color);var card=known==null ? null : catalog.Card(known.CardId);
    var zone=known?.Zone ?? (player.DiscardColors.Contains(color)?CardZone.Discarded:CardZone.InHand);
    bool allowed=own && card!=null && (planning ? !player.Confirmed && (zone==CardZone.InHand || zone==CardZone.Selected) : WheelChoices(renderedView).Contains(card.Id));
    var scheduled=wheelState.Discards.FirstOrDefault(b=>b.Seat==target && b.Color==color);float flipAt=scheduled!=null ? scheduled.Start+1.15f : 0;
    if(flipAt>0)zone=CardZone.Discarded;
    var disc=new SkillDisc(color,card,player,zone,card?.Id==wheelPreview,allowed,wheelState.Get(target,color),flipAt,()=>{if(card!=null)WheelPick(card.Id);},()=>ShowSkillInfo(card));wheel.AddSkill(disc,i);
   }
   if(wheelNeedsFocus){wheelNeedsFocus=false;root.schedule.Execute(()=>board?.FollowAt(Board3DGeometry.World(location),board3DViewport.Enabled ? 3f : 2.6f)).StartingIn(30);}
   frame.schedule.Execute(()=>{
    if(board==null || frame.panel==null)return;Vector2 size=board.contentRect.size;
    // Limit to the visible screen and leave the settings drawer usable.
    size.y=Mathf.Min(size.y,root.worldBound.yMax-board.worldBound.yMin-24);
    var drawer=root.Q<VisualElement>("operation-panel");
    if(drawer!=null && drawer.worldBound.width>0)size.x=Mathf.Min(size.x,drawer.worldBound.xMin-board.worldBound.xMin-8);
    float scale=Mathf.Clamp(Mathf.Min((size.x-12)/520,(size.y-12)/520),.05f,1);
    var point=board.ProjectHero(location);
    float radius=260*scale;point.x=Mathf.Clamp(point.x,radius+6,Mathf.Max(radius+6,size.x-radius-6));point.y=Mathf.Clamp(point.y,radius+6,Mathf.Max(radius+6,size.y-radius-6));
    frame.style.left=point.x-radius;frame.style.top=point.y-radius;frame.style.scale=new Scale(new Vector3(scale,scale,1));
    if(eventBeat!=null && Time.realtimeSinceStartup-eventBeat.Start>=2.1f)wheel.Collapse();
    if(eventBeat!=null && Time.realtimeSinceStartup-eventBeat.Start>=2.4f && wheelState.Discards.Count>0 && wheelState.Discards.Peek()==eventBeat){wheelState.Discards.Dequeue();wheelSeat=null;wheelContext="";Render();}
   }).Every(16);
  }
 }
}
