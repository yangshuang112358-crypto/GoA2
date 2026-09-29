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
  private VisualElement? skillPopup,skillPopupOwner;
  private Vector2 skillPopupPointer;
  private static bool WheelDiscard(GameView v)=>v.Pending?.Kind=="forced_discard" || v.Pending?.Kind=="optional_discard" || v.Pending?.Kind=="minion_protection";
  private static bool WheelRecovery(GameView v)=>v.Pending?.Kind=="recover_discard" || v.Pending?.Kind=="discard_attack" || v.Pending?.Kind=="card_swap";
  private List<string> WheelChoices(GameView v)=>v.Pending?.Kind=="recover_discard" ? v.RecoverableCards : v.Pending?.Kind=="discard_attack" ? v.DiscardAttackCards : v.Pending?.Kind=="card_swap" ? v.CardSwapOptions : v.Pending?.Kind=="defense" ? v.DefenseOptions.Select(o=>o.CardId).ToList() : v.Pending?.Kind=="forced_discard" ? v.ForcedDiscardCards : v.Pending?.Kind=="optional_discard" ? v.OptionalDiscardCards : v.Pending?.Kind=="minion_protection" ? v.MinionProtectionCards : new List<string>();
  private void ObserveWheel() {
   if(wheelState.Observe(renderedView,Time.realtimeSinceStartup)){wheelContext="";wheelSeat=null;wheelPreview="";wheelDecline=false;wheelFocusedBeat=-1;}
   string context=renderedView.MatchId+":"+seat+":"+renderedView.Round+":"+renderedView.Turn+":"+renderedView.Phase+":"+renderedView.Pending?.Id;
   if(context!=wheelContext){wheelOpened=Time.realtimeSinceStartup;wheelContext=context;wheelPreview="";wheelDecline=false;wheelSeat=renderedView.Phase==Phase.Planning ? seat : CameraFollowPolicy.ResponseSeat(renderedView);wheelNeedsFocus=CameraFollowPolicy.ResponseSeat(renderedView).HasValue;}
   if(wheelState.Discards.Count>0){var beat=wheelState.Discards.Peek();wheelSeat=beat.Seat;if(wheelFocusedBeat!=beat.Start){wheelFocusedBeat=beat.Start;wheelNeedsFocus=true;}}
  }
  private void ToggleHeroWheel(int target) {
   if(wheelState.Discards.Count>0)return;
   if(renderedView.Phase==Phase.Action && target==seat){actionRingClosed=false;actionChoice="";moveMode=null;chosenCell=null;}
   else if(renderedView.Phase==Phase.Action)actionRingClosed=true;
   HideHeroHover();HideSkillInfo();
   if(wheelSeat==target){CloseHeroWheel();return;}
   Sound("open");wheelOpened=Time.realtimeSinceStartup;wheelSeat=target;wheelPreview="";wheelDecline=false;Render();
   var unit=renderedView.Units.FirstOrDefault(u=>u.Seat==target);
   var currentBoard=board;int focusVersion=cameraFocusVersion;
   if(unit!=null)root.schedule.Execute(()=>{if(board==currentBoard && wheelSeat==target && focusVersion==cameraFocusVersion)board?.FollowAt(Board3DGeometry.World(unit.Position),board3DViewport.Enabled ? 3f : 2.6f);}).StartingIn(30);
  }
  private void CloseHeroWheel() {
   if(wheelState.Discards.Count>0)return;
   if(wheelSeat.HasValue || root.Q("action-wheel")!=null)Sound("close");
   actionRingClosed=true;root.Q("action-wheel")?.RemoveFromHierarchy();
   wheelSeat=null;wheelPreview="";wheelDecline=false;HideSkillInfo();
   if(confirmButton==skillWheel?.Confirm){confirmButton=null;confirmAction=null;root.Q("floating-confirm")?.RemoveFromHierarchy();}
   skillWheel?.Collapse();
  }
  private void WheelPick(string id) {
   if(!NetworkCanAct || wheelState.Discards.Count>0 || wheelSeat!=seat)return;
   if(renderedView.Phase==Phase.Planning && !renderedView.Players[seat].Confirmed){
    var c=renderedView.OwnCards.Single(x=>x.CardId==id);
    if(c.Zone==CardZone.Selected)Submit(CommandKind.CancelCardSelection);else if(c.Zone==CardZone.InHand)Submit(CommandKind.SelectCard,id);return;
   }
   if(WheelChoices(renderedView).Contains(id)){wheelDecline=false;wheelPreview=wheelPreview==id ? "" : id;Sound(wheelPreview==""?"cancel":"select");Render();}
  }
  private void WheelConfirm() {
   if(!NetworkCanAct || wheelState.Discards.Count>0 || wheelSeat!=seat)return;
   if(renderedView.Phase==Phase.Planning){if(!renderedView.QuickSelection && !renderedView.Players[seat].Confirmed && renderedView.OwnCards.Any(c=>c.Zone==CardZone.Selected))Submit(CommandKind.ConfirmCard);return;}
   if(wheelDecline && renderedView.Pending?.Kind=="defense"){wheelDecline=false;Submit(CommandKind.DeclineDefense);return;}
   if(wheelDecline && renderedView.CanDeclineRetaliationDiscard){wheelDecline=false;Submit(CommandKind.DeclineRetaliationDiscard);return;}
   if(!WheelChoices(renderedView).Contains(wheelPreview))return;
   var kind=renderedView.Pending!.Kind=="recover_discard" ? CommandKind.ChooseRecoveredCard : renderedView.Pending.Kind=="discard_attack" ? CommandKind.ChooseDiscardAttack : renderedView.Pending.Kind=="card_swap" ? CommandKind.ChooseCardSwap : renderedView.Pending.Kind=="defense" ? CommandKind.Defend : renderedView.Pending.Kind=="forced_discard" ? CommandKind.ForcedDiscard : renderedView.Pending.Kind=="optional_discard" ? CommandKind.ChooseOptionalDiscard : CommandKind.ChooseMinionProtection;
   string id=wheelPreview;wheelPreview="";Submit(kind,id);
  }
  private void WheelAlternative() {
   if(!NetworkCanAct || wheelSeat!=seat || wheelState.Discards.Count>0)return;
   if(renderedView.Pending?.Kind=="defense"){wheelPreview="";wheelDecline=!wheelDecline;Render();}
   else if(renderedView.Pending?.Kind=="recover_discard")Submit(CommandKind.ChooseRecoveredCard,"skip");
   else if(renderedView.Pending?.Kind=="card_swap")Submit(CommandKind.ChooseCardSwap,"skip");
   else if(renderedView.Pending?.Kind=="optional_discard")Submit(CommandKind.ChooseOptionalDiscard,"skip");
   else if(renderedView.Pending?.Kind=="minion_protection")Submit(CommandKind.ChooseMinionProtection,"skip");
   else if(renderedView.CanDeclineRetaliationDiscard){wheelPreview="";wheelDecline=!wheelDecline;Render();}
  }
  private void HideSkillInfo(){skillPopup?.RemoveFromHierarchy();skillPopup=null;skillPopupOwner=null;}
  private void PositionSkillInfo(Vector2 pointer) {
   skillPopupPointer=pointer;if(skillPopup==null)return;
   var at=root.WorldToLocal(pointer);float w=skillPopup.resolvedStyle.width,h=skillPopup.resolvedStyle.height;
   if(float.IsNaN(w)||float.IsNaN(h))return;
   float x=at.x+20;if(x+w>root.contentRect.width-12)x=at.x-w-20;
   skillPopup.style.left=Mathf.Clamp(x,12,Mathf.Max(12,root.contentRect.width-w-12));
   skillPopup.style.top=Mathf.Clamp(at.y+18,12,Mathf.Max(12,root.contentRect.height-h-12));
  }
  private void ShowSkillInfo(CardDefinition? card,VisualElement owner,Vector2 pointer) {
   if(card==null)return;HideHeroHover();HideCardPreview();HideSkillInfo();
   var popup=Box("hero-hover");popup.AddToClassList("skill-cursor-detail");popup.name="skill-description";skillPopup=popup;skillPopupOwner=owner;
   popup.style.width=Mathf.Min(540,root.contentRect.width-24);RenderCardDetail(popup,card,false);
   popup.pickingMode=PickingMode.Ignore;popup.Query<VisualElement>().ForEach(e=>e.pickingMode=PickingMode.Ignore);
   skillPopupPointer=pointer;popup.RegisterCallback<GeometryChangedEvent>(_=>PositionSkillInfo(skillPopupPointer));root.Add(popup);PositionSkillInfo(pointer);
  }
  private void BuildSkillWheel() {
   skillWheel=null;wheelFrame=null;if(board==null || !wheelSeat.HasValue)return;
   int target=wheelSeat.Value;if(renderedView.Phase==Phase.Action && target==seat && wheelState.Discards.Count==0)return;var player=renderedView.Players.Single(p=>p.Seat==target);
   var unit=renderedView.Units.FirstOrDefault(u=>u.Seat==target);Hex location;if(unit!=null)location=unit.Position;else if(!wheelState.LastPositions.TryGetValue(target,out location))return;
   bool beat=wheelState.Discards.Count>0;var eventBeat=beat ? wheelState.Discards.Peek() : null;
   bool own=target==seat && !beat,planning=renderedView.Phase==Phase.Planning;
   bool canConfirm=own && (planning ? !renderedView.QuickSelection && !player.Confirmed && renderedView.OwnCards.Any(c=>c.Zone==CardZone.Selected) : WheelChoices(renderedView).Contains(wheelPreview) || wheelDecline);
   string caption=beat ? "弃牌" : canConfirm ? wheelDecline ? renderedView.Pending?.Kind=="defense" ? "确认不防御？" : "确认被击败？" : "确认？" : own && planning ? player.Confirmed ? "已确认" : "选牌" : renderedView.Pending?.Kind=="defense" ? "防御" : WheelDiscard(renderedView) ? "弃牌" : "查看";
   var frame=new VisualElement{pickingMode=PickingMode.Ignore,name="skill-wheel-anchor"};frame.style.position=Position.Absolute;frame.style.width=520;frame.style.height=520;frame.style.transformOrigin=new TransformOrigin(0,0,0);board.Add(frame);wheelFrame=frame;
   var wheel=new SkillWheel(player.Team,caption,WheelConfirm,WheelAlternative);skillWheel=wheel;wheel.ResumeOpen(beat ? eventBeat!.Start : wheelOpened);frame.Add(wheel);wheel.Confirm.SetEnabled(canConfirm);wheel.Confirm.style.display=canConfirm?DisplayStyle.Flex:DisplayStyle.None;
   bool alt=own && (renderedView.Pending?.Kind=="defense" || renderedView.Pending?.Kind=="optional_discard" || renderedView.Pending?.Kind=="minion_protection" || renderedView.CanDeclineRetaliationDiscard || renderedView.Pending?.Kind=="recover_discard" || renderedView.Pending?.Kind=="card_swap");
   wheel.Alternative.style.display=alt ? DisplayStyle.Flex:DisplayStyle.None;wheel.Alternative.text=renderedView.Pending?.Kind=="recover_discard" ? "不取回，继续" : renderedView.Pending?.Kind=="card_swap" ? "不交换，继续" : renderedView.Pending?.Kind=="defense" ? "不防御" : renderedView.CanDeclineRetaliationDiscard ? "不弃牌，选择被击败" : "不弃牌，继续";
   if(canConfirm){confirmButton=wheel.Confirm;confirmAction=WheelConfirm;}
   for(int i=0;i<HeroPlate.Colors.Length;i++){
    string color=HeroPlate.Colors[i];var known=SkillWheelState.KnownCard(catalog,renderedView,seat,target,color);var card=known==null ? null : catalog.Card(known.CardId);
    var zone=known?.Zone ?? (player.DiscardColors.Contains(color)?CardZone.Discarded:CardZone.InHand);
    if(WheelRecovery(renderedView))zone=CardZone.InHand;
    bool allowed=own && card!=null && (planning ? !player.Confirmed && (zone==CardZone.InHand || zone==CardZone.Selected) : WheelChoices(renderedView).Contains(card.Id));
    var scheduled=wheelState.Discards.FirstOrDefault(b=>b.Seat==target && b.Color==color);float flipAt=scheduled!=null ? scheduled.Start+1.15f : 0;
    if(flipAt>0)zone=CardZone.Discarded;
    var disc=new SkillDisc(color,card,player,zone,card?.Id==wheelPreview,allowed,wheelState.Get(target,color),flipAt,()=>{if(card!=null)WheelPick(card.Id);},()=>{});
    disc.RegisterCallback<PointerEnterEvent>(e=>HoverSkillSound(e.position));
    disc.Inspect=at=>ShowSkillInfo(card,disc,at);
    disc.RegisterCallback<PointerMoveEvent>(e=>{if(skillPopupOwner==disc)PositionSkillInfo(e.position);});
    disc.RegisterCallback<PointerLeaveEvent>(_=>{if(skillPopupOwner==disc)HideSkillInfo();});
    if(renderedView.Pending?.Kind=="defense" && card!=null){var option=renderedView.DefenseOptions.FirstOrDefault(o=>o.CardId==card.Id);if(option!=null && CardDisplay.WarnDefense(card,option.Assessment)){disc.style.backgroundColor=new Color(.48f,.22f,.28f,.48f);disc.tooltip="防御数值偏低";}}
    wheel.AddSkill(disc,i);
   }
   if(wheelNeedsFocus && cameraFollow){wheelNeedsFocus=false;var currentBoard=board;int focusVersion=cameraFocusVersion;root.schedule.Execute(()=>{if(cameraFollow && board==currentBoard && focusVersion==cameraFocusVersion)board?.FollowAt(Board3DGeometry.World(location),board3DViewport.Enabled ? 3f : 2.6f);}).StartingIn(30);}
   frame.schedule.Execute(()=>{
    if(board==null || frame.panel==null)return;Vector2 size=board.contentRect.size;
    // World anchor may leave the viewport; never clamp it to a screen edge.
    float scale=Mathf.Clamp(Mathf.Min(size.x/1280f,size.y/800f),.65f,1);
    var point=board.ProjectHero(location);float radius=260*scale;
    frame.style.left=point.x-radius;frame.style.top=point.y-radius;frame.style.scale=new Scale(new Vector3(scale,scale,1));
    if(eventBeat!=null && Time.realtimeSinceStartup-eventBeat.Start>=2.1f)wheel.Collapse();
    if(eventBeat!=null && Time.realtimeSinceStartup-eventBeat.Start>=2.4f && wheelState.Discards.Count>0 && wheelState.Discards.Peek()==eventBeat){wheelState.Discards.Dequeue();wheelSeat=null;wheelContext="";Render();}
   }).Every(16);
  }
 }
}
