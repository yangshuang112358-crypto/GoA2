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
  private int hoverHeroSeat=-1;
  // Opened explicitly by right click. Moving the mouse never opens or dismisses it.
  private void ShowHeroHover(int? target,Vector2 at)
  {
   if(!target.HasValue){HideHeroHover();return;}
   if(target==hoverHeroSeat && heroPopup?.panel!=null){HideHeroHover();return;}
   HideHeroHover();hoverHeroSeat=target.Value;
   var player=renderedView.Players.Single(p=>p.Seat==target);
   var popup=Box("hero-hover");popup.name="hero-hover";heroPopup=popup;
   float width=Mathf.Min(1400,Screen.width-48);
   popup.style.position=Position.Absolute;popup.style.width=width;popup.style.maxHeight=Screen.height-48;
   popup.style.left=Mathf.Clamp(at.x-width*.5f,24,Screen.width-width-24);popup.style.top=24;
   var header=Box("panel-heading");popup.Add(header);
   header.Add(Text((catalog.Heroes.FirstOrDefault(h=>h.Id==player.HeroId)?.Name ?? HeroName(player.HeroId))+" · 等级 "+player.Level+" · 经验 "+player.Gold,"section-title"));
   header.Add(Button("×",HideHeroHover,"quiet-button","close-hero-inspection"));
   string state=(player.IsPoisoned ? "中毒  " : "")+(player.IsPetrified ? "石化  " : "");
   popup.Add(Text(state+"手牌 "+player.HandCount+" · 永久加成："+(player.PermanentBonuses.Count==0 ? "无" : string.Join(" / ",player.PermanentBonuses.Select(b=>b.Key+" +"+b.Value))),"body"));
   if(player.IsPoisoned)popup.Add(Text("当前有效加成："+string.Join(" / ",player.EffectiveBonuses.Select(b=>b.Key+" "+b.Value)),"body"));
   var scroll=new ScrollView();scroll.style.flexShrink=1;scroll.style.minHeight=0;popup.Add(scroll);
   var grid=new VisualElement{name="hero-card-grid"};grid.style.flexDirection=FlexDirection.Row;grid.style.flexWrap=Wrap.Wrap;scroll.Add(grid);
   foreach(string color in new[]{"gold","silver","red","green","blue","purple"}) {
    string? id=null;CardInstance? instance=null;
    if(color=="purple")id=player.PurpleCardId;
    else if(target==seat){instance=renderedView.OwnCards.FirstOrDefault(c=>catalog.Card(c.CardId).Color==color);id=instance?.CardId;}
    else {instance=player.PublicCards.FirstOrDefault(c=>catalog.Card(c.CardId).Color==color) ?? player.PublicDiscards.FirstOrDefault(c=>catalog.Card(c.CardId).Color==color) ?? player.Revealed.FirstOrDefault(c=>catalog.Card(c.CardId).Color==color);id=instance?.CardId ?? player.Plays.Where(p=>p.Round==renderedView.Round && catalog.Card(p.CardId).Color==color).OrderByDescending(p=>p.Turn).FirstOrDefault()?.CardId;}
    if(color=="purple" && id==null)continue;
    var box=Box("hero-hover-card");box.name="hero-inspect-"+color;box.style.width=Length.Percent(49);box.style.marginRight=Length.Percent(1);box.style.flexShrink=0;grid.Add(box);
    if(id==null){box.Add(Text("◆","section-title"));box.style.minHeight=130;continue;}
    bool discarded=instance?.Zone==CardZone.Discarded;
    var plays=player.Plays.Where(p=>p.Round==renderedView.Round && p.CardId==id).OrderBy(p=>p.Turn).ToList();
    box.style.opacity=discarded ? .5f : plays.Count>0 || instance?.Zone==CardZone.Selected ? .72f : 1;
    RenderCardDetail(box,catalog.Card(id),false);
    string footer=color=="purple" ? "终极能力" : discarded ? "弃牌堆" : plays.Count>0 ? "第 "+string.Join("、",plays.Select(p=>p.Turn))+" 回合出牌" : instance?.Zone==CardZone.Selected ? "已选牌" : "手牌";
    box.Add(Text(footer,"card-zone"));
   }
   root.Add(popup);
  }
  private void HideHeroHover() {heroPopup?.RemoveFromHierarchy();heroPopup=null;hoverHeroSeat=-1;}
 }
}
