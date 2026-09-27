using System.Linq;
using Goa2.Domain;
using Goa2.Presentation.UI3D;
using NUnit.Framework;
using UnityEngine;
namespace Goa2.UI3D.Tests {
 public sealed class WorldHudTests {
  private static ContentCatalog Catalog() {var c=new ContentCatalog {Rules=new RuleSettings{StartingCrystalLife=7}};c.Cells.Add(new CellDefinition{Position=new Hex(0,0),Region="mid"});c.Cells.Add(new CellDefinition{Position=new Hex(6,0),Region="redNear"});c.Cells.Add(new CellDefinition{Position=new Hex(10,0),Region="redFountain",Spawn="redHeroSpawn"});return c;}
  private static GameView View()=>new GameView {MatchId="a",Revision=1,BlueCrystal=7,RedCrystal=7,CombatRegion="mid",Players={new PlayerView{Seat=1,Team=Team.Red}}};
  [Test] public void DeathAndCrownConsumeEachPublicEventOnlyOnce() {
   var c=Catalog();var v=View();var p=new BattlePresentationState();p.Observe(c,v,0);
   v.Events.Add(new GameEvent{Sequence=1,Kind="HeroDefeated",Seat=1});v.Events.Add(new GameEvent{Sequence=2,Kind="CrystalDamaged",Seat=1,Detail="2"});
   v.Events.Add(new GameEvent{Sequence=3,Kind="MinionDefeated",From=new Hex(2,0)});v.Events.Add(new GameEvent{Sequence=4,Kind="FrontlineMarkGained",Detail="Blue"});v.RedCrystal=5;p.Observe(c,v,1);p.Observe(c,v,1.1f);
   Assert.That(p.Shards.Count,Is.EqualTo(2));Assert.That(p.Shards.Select(s=>s.Index),Is.EqualTo(new[]{0,1}));Assert.That(p.Crowns.Count,Is.EqualTo(1));Assert.That(p.Crowns[0].From,Is.EqualTo(Board3DGeometry.World(new Hex(2,0),1)));Assert.That(p.DeathFocus,Is.EqualTo(Board3DGeometry.World(new Hex(10,0))));
  }
  [Test] public void LoadingHistoricalMatchDoesNotReplayDeathsOrToss() {var v=View();v.Events.Add(new GameEvent{Sequence=1,Kind="HeroDefeated",Seat=1});var p=new BattlePresentationState();p.Observe(Catalog(),v,10);Assert.That(p.DeathUntil,Is.LessThan(0));Assert.That(p.CoinStarted,Is.LessThan(0));}
  [Test] public void DebugCrystalCapacityAndCoinSideAreAuthoritative() {var v=View();var p=new BattlePresentationState();p.Observe(Catalog(),v,0);v.Events.Add(new GameEvent{Sequence=1,Kind="DebugCrystalSet",Detail="Red:12"});v.RedCrystal=12;v.DecisionCoin=Team.Red;p.Observe(Catalog(),v,3);Assert.That(p.RedCapacity,Is.EqualTo(12));Assert.That(p.CoinTo,Is.EqualTo(Team.Red));Assert.That(p.CoinStarted,Is.EqualTo(3));p.Observe(Catalog(),v,4);Assert.That(p.CoinStarted,Is.EqualTo(3));}
  [Test] public void RestoringOlderRevisionClearsFutureAnimations() {var c=Catalog();var v=View();var p=new BattlePresentationState();p.Observe(c,v,0);v.Revision=2;v.Events.Add(new GameEvent{Sequence=1,Kind="HeroDefeated",Seat=1});p.Observe(c,v,1);Assert.That(p.DeathUntil,Is.GreaterThan(1));v.Revision=1;v.Events.Clear();p.Observe(c,v,2);Assert.That(p.DeathUntil,Is.LessThan(0));Assert.That(p.CoinStarted,Is.LessThan(0));}
  [Test] public void RegionalCenterDoesNotUseWholeMapCenter() {var c=Catalog();Assert.That(BattlePresentationState.Center(c,"redNear"),Is.EqualTo(Board3DGeometry.World(new Hex(6,0))));Assert.That(BattlePresentationState.Center(c,"redNear"),Is.Not.EqualTo(BattlePresentationState.Center(c)));}
  [Test] public void LifeCrystalsHaveEqualRadiusAndSpacing() {var c=Catalog();var center=BattlePresentationState.Center(c);float? radius=null,spacing=null;Vector3? last=null;for(int i=0;i<7;i++){var p=Board3DScene.CrystalPosition(c,Team.Red,i,7);float d=Vector3.Distance(p,center);if(radius.HasValue)Assert.That(d,Is.EqualTo(radius.Value).Within(.001));radius=d;if(last.HasValue){float gap=Vector3.Distance(p,last.Value);if(spacing.HasValue)Assert.That(gap,Is.EqualTo(spacing.Value).Within(.001));spacing=gap;}last=p;}}
  [TestCase(CardZone.InHand,0)] [TestCase(CardZone.Selected,1)] [TestCase(CardZone.PlayedResolved,1)] [TestCase(CardZone.Discarded,2)]
  public void MeterUsesZonesRatherThanRevealingSelectedCard(CardZone zone,int expected) {var c=Catalog();c.Cards.Add(new CardDefinition{Id="red",Color="red"});var v=View();v.OwnCards.Add(new CardInstance{CardId="red",Zone=zone});Assert.That(HeroPlate.Status(v,v.Players[0],"red",c,1),Is.EqualTo(expected));}
  [Test] public void OpponentMeterDoesNotLeakSelectedColor() {var c=Catalog();c.Cards.Add(new CardDefinition{Id="red",Color="red"});var v=View();v.OwnCards.Add(new CardInstance{CardId="red",Zone=CardZone.Selected});Assert.That(HeroPlate.Status(v,v.Players[0],"red",c,0),Is.EqualTo(0));}
  [Test] public void SlowFollowStartsGentlyAndHasSpeedLimit() {var s=new Board3DViewport();s.Follow(new Vector3(100,0,0));s.Advance(.016f);float first=s.Focus.x;Assert.That(first,Is.LessThan(.05));float old=s.Focus.x;for(int i=0;i<200;i++){s.Advance(.016f);Assert.That(s.Focus.x-old,Is.LessThanOrEqualTo(18*.016f+.001f));old=s.Focus.x;}}
 }
}
