#nullable enable
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using Goa2.Domain;
namespace Goa2.Presentation
{
 public sealed partial class GameScreen
 {
  private VisualElement? heroPopup;
  private int hoverHeroSeat=-1,hoverRequest;
  private bool overHeroPopup;
  private void ShowHeroHover(int? target,Vector2 at)
  {
   int request=++hoverRequest;
   if(!target.HasValue) {root.schedule.Execute(()=>{if(request==hoverRequest && !overHeroPopup)HideHeroHover();}).StartingIn(220);return;}
   if(target==hoverHeroSeat && heroPopup?.panel!=null)return;
   HideHeroHover();hoverHeroSeat=target.Value;
   var player=renderedView.Players.Single(p=>p.Seat==target);
   var popup=Box("hero-hover");popup.name="hero-hover";heroPopup=popup;
   popup.style.position=Position.Absolute;popup.style.width=Mathf.Min(620,Screen.width-40);popup.style.maxHeight=Screen.height-80;
   popup.style.left=Mathf.Clamp(at.x+24,16,Screen.width-640);popup.style.top=40;
   popup.RegisterCallback<PointerEnterEvent>(_=>overHeroPopup=true);
   popup.RegisterCallback<PointerLeaveEvent>(_=>{overHeroPopup=false;ShowHeroHover(null,Vector2.zero);});
   popup.Add(Text((catalog.Heroes.FirstOrDefault(h=>h.Id==player.HeroId)?.Name ?? HeroName(player.HeroId))+" · 等级 "+player.Level+" · 经验 "+player.Gold,"section-title"));
   popup.Add(Text((player.Team==Team.Blue ? "蓝队" : "红队")+" · 席位 "+(player.Seat+1)+(player.Seat==renderedView.BlueCaptain || player.Seat==renderedView.RedCaptain ? " · 队长" : ""),"muted"));
   popup.Add(Text("手牌 "+player.HandCount+" · "+(player.IsPoisoned ? "中毒 · " : "")+(player.IsPetrified ? "石化 · " : "")+string.Join(" / ",(player.EffectiveBonuses??player.PermanentBonuses).Select(b=>b.Key+" "+b.Value)),"body"));
   int next=target.Value;popup.Add(Button("操控角色 "+(next+1),()=>{HideHeroHover();SwitchSeat(next);},"quiet-button"));
   var scroll=new ScrollView();scroll.style.flexShrink=1;scroll.style.minHeight=0;popup.Add(scroll);
   if(next==seat) foreach(var c in renderedView.OwnCards.Where(c=>catalog.Card(c.CardId).Color!="purple")) {
    var box=Box("hero-hover-card");box.style.opacity=c.Zone==CardZone.Discarded ? .4f : c.Zone==CardZone.PlayedResolved || c.Zone==CardZone.PlayedUnresolved ? .62f : 1;
    var play=player.Plays.LastOrDefault(p=>p.Round==renderedView.Round && p.CardId==c.CardId);
    box.Add(Text(ZoneName(c)+(play==null ? "" : " · 本轮第 "+play.Turn+" 回合出牌"),"muted"));RenderCardDetail(box,catalog.Card(c.CardId));scroll.Add(box);
   } else {
    scroll.Add(Text("未公开手牌隐藏","muted"));
    foreach(var play in player.Plays.Where(p=>p.Round==renderedView.Round).OrderBy(p=>p.Turn)) {
     scroll.Add(Text("第 "+play.Round+" 轮 · 第 "+play.Turn+" 回合出牌","muted"));RenderCardDetail(scroll,catalog.Card(play.CardId));
    }
    scroll.Add(Text("当前弃牌堆","section-title"));
    foreach(var c in player.PublicDiscards)RenderCardDetail(scroll,catalog.Card(c.CardId));
    if(player.PublicDiscards.Count==0)scroll.Add(Text("无弃牌","muted"));
   }
   if(player.PurpleCardId!=null) {scroll.Add(Text("终极能力","section-title"));RenderCardDetail(scroll,catalog.Card(player.PurpleCardId));}
   root.Add(popup);
  }
  private void HideHeroHover() {heroPopup?.RemoveFromHierarchy();heroPopup=null;hoverHeroSeat=-1;overHeroPopup=false;}
 }
}
