using System.Linq;
using Goa2.Application;
using Goa2.Domain;
using Goa2.Infrastructure;
using Goa2.Rules;
using NUnit.Framework;
using static Goa2.Tests.SessionTests;
using static Goa2.Tests.TurnFlowTests;
namespace Goa2.Tests
{
 public sealed class PowerBoostTests
 {
  internal const string Card="wasp-14-动力助推", Minion="minion:-1,-3";
  internal static GameSession Setup(ContentCatalog cat,string card=Card)
  {
   var g=LocalGameFactory.Create(cat,"power-boost",new[]{"A","B","C","D"},42,true);
   Apply(g,0,CommandKind.DebugPrepare,"wasp,sabina,brogan,arien");Apply(g,0,CommandKind.DebugEquipCard,card,target:0);
   Apply(g,0,CommandKind.DebugTeleport,"hero:0",cell:new Hex(-2,0));Apply(g,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(-2,-1));Apply(g,0,CommandKind.DebugTeleport,"hero:2",cell:new Hex(-2,1));Apply(g,0,CommandKind.DebugTeleport,"hero:3",cell:new Hex(-1,0));Apply(g,0,CommandKind.DebugTeleport,Minion,cell:new Hex(-1,-1));
   string[] cards={card,"sabina-07-指挥","brogan-06-铜墙铁壁","arien-07-潮水"};for(int i=0;i<4;i++)Apply(g,i,CommandKind.SelectCard,cards[i]);Assert.That(g.View(0).ActiveSeat,Is.EqualTo(0));return g;
  }
  private static GameState State(GameSession g)=>new JsonStateCodec().Read(g.ExportSave());
  private static void Push(GameSession g,string id)=>Apply(g,0,CommandKind.ChooseEffectTarget,id);
  [Test] public void ContractAndEngineGate(){var c=BattlefieldTests.Catalog().Card(Card);Assert.That(c.Initiative,Is.EqualTo(10));Assert.That(c.SecondaryMovement,Is.EqualTo(3));Assert.That(c.SecondaryDefense,Is.EqualTo(5));Assert.That(CombatRules.HasPrimaryProgram(c),Is.True);Assert.That(CombatRules.HasPrimaryProgram(c,47),Is.False);c.Text+="可选";Assert.That(CombatRules.HasPrimaryProgram(c),Is.False);}
  [TestCase("hero:3")] [TestCase("hero:1")] [TestCase(Minion)]
  public void AllPushesPrecedeDiscardAndEachWindowRestores(string first)
  {
   var cat=BattlefieldTests.Catalog();var g=Setup(cat);Apply(g,0,CommandKind.BeginPrimary);Assert.That(g.View(0).EffectTargets,Is.EquivalentTo(new[]{"hero:1","hero:3",Minion}));
   foreach(var id in new[]{first}.Concat(new[]{"hero:1","hero:3",Minion}.Where(x=>x!=first))){g=ChargeTests.Restore(cat,g);Push(g,id);Assert.That(g.View(0).Events.Any(e=>e.Kind=="ForcedDiscardRequired"),Is.False);}
   Assert.That(State(g).Units.Single(u=>u.Id=="hero:1").Position,Is.EqualTo(new Hex(-2,-3)));Assert.That(State(g).Units.Single(u=>u.Id=="hero:3").Position,Is.EqualTo(new Hex(-1,0)));Assert.That(State(g).Units.Single(u=>u.Id==Minion).Position,Is.EqualTo(new Hex(0,-2)));
   Assert.That(g.View(0).EffectTargets,Is.EqualTo(new[]{"hero:3"}));g=ChargeTests.Restore(cat,g);Push(g,"hero:3");Assert.That(g.View(3).Pending!.ChooserSeat,Is.EqualTo(3));g=ChargeTests.Restore(cat,g);Apply(g,3,CommandKind.ForcedDiscard,"arien-13-挑战者");Assert.That(State(g).Execution,Is.Null);Assert.That(g.View(0).Events.Count(e=>e.Kind=="UnitPushed"),Is.EqualTo(3));Assert.That(g.View(0).Events.Any(e=>e.Kind=="AttackStarted" || e.Kind=="HeroDefeated"),Is.False);
  }
  [Test] public void InvalidChoicesAreAtomicAndSuccessfulCommandIsIdempotent()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);Apply(g,0,CommandKind.BeginPrimary);foreach(var value in new[]{"skip","hero:0","hero:2","minion:-3,0","minion:-3,1"}){var save=g.ExportSave();Assert.That(g.Execute(0,Cmd(g,0,CommandKind.ChooseEffectTarget,value)).Accepted,Is.False);Assert.That(g.ExportSave(),Is.EqualTo(save));}var before=g.ExportSave();Assert.That(g.Execute(1,Cmd(g,1,CommandKind.ChooseEffectTarget,"hero:1")).Accepted,Is.False);Assert.That(g.ExportSave(),Is.EqualTo(before));var command=Cmd(g,0,CommandKind.ChooseEffectTarget,"hero:1");Assert.That(g.Execute(0,command).Accepted,Is.True);var after=g.ExportSave();Assert.That(g.Execute(0,command).Duplicate,Is.True);Assert.That(g.ExportSave(),Is.EqualTo(after));}
  [Test] public void NoAdjacentEnemyResolvesWithoutAttackOrDiscard()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);Apply(g,0,CommandKind.DebugTeleport,"hero:0",cell:new Hex(6,-8));Apply(g,0,CommandKind.BeginPrimary);Assert.That(State(g).Execution,Is.Null);Assert.That(g.View(0).Events.Any(e=>e.Kind=="UnitPushed" || e.Kind=="ForcedDiscardRequired"),Is.False);}
  [TestCase("hero:1")] [TestCase("hero:3")] public void MultipleBlockedHeroesChooseOrderThenEachOwnerDiscards(string first)
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);Apply(g,0,CommandKind.DebugTeleport,"minion:1,0",cell:new Hex(-2,-3));Apply(g,0,CommandKind.BeginPrimary);Push(g,"hero:1");Push(g,"hero:3");Push(g,Minion);Assert.That(g.View(0).EffectTargets,Is.EquivalentTo(new[]{"hero:1","hero:3"}));foreach(var id in new[]{first,first=="hero:1"?"hero:3":"hero:1"}){g=ChargeTests.Restore(cat,g);Push(g,id);int seat=id=="hero:1"?1:3;string save=g.ExportSave();Assert.That(g.Execute(0,Cmd(g,0,CommandKind.ForcedDiscard,g.View(seat).ForcedDiscardCards.First())).Accepted,Is.False);Assert.That(g.ExportSave(),Is.EqualTo(save));g=ChargeTests.Restore(cat,g);Apply(g,seat,CommandKind.ForcedDiscard,g.View(seat).ForcedDiscardCards.First());}Assert.That(State(g).Execution,Is.Null);Assert.That(g.View(0).Events.Count(e=>e.Kind=="ForcedDiscardCompleted"),Is.EqualTo(2));}
  [Test] public void EmptyHandIsSkippedWithoutDefeat()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);foreach(var card in g.View(3).OwnCards.Where(c=>c.Zone==CardZone.InHand).Select(c=>c.CardId).ToArray())Apply(g,0,CommandKind.DebugDiscard,card,target:3);Apply(g,0,CommandKind.BeginPrimary);Push(g,"hero:1");Push(g,"hero:3");Push(g,Minion);Push(g,"hero:3");Assert.That(State(g).Execution,Is.Null);Assert.That(g.View(0).Events.Count(e=>e.Kind=="ForcedDiscardSkipped"),Is.EqualTo(1));Assert.That(State(g).Units.Any(u=>u.Id=="hero:3"),Is.True);}
  [Test] public void HandChoiceCannotSkipOrDiscardRevealedCardAndIsPrivate()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);Apply(g,0,CommandKind.BeginPrimary);Push(g,"hero:1");Push(g,"hero:3");Push(g,Minion);Push(g,"hero:3");foreach(var card in new[]{"skip","arien-07-潮水"}){var save=g.ExportSave();Assert.That(g.Execute(3,Cmd(g,3,CommandKind.ForcedDiscard,card)).Accepted,Is.False);Assert.That(g.ExportSave(),Is.EqualTo(save));}Assert.That(g.View(0).ForcedDiscardCards,Is.Empty);Apply(g,3,CommandKind.ForcedDiscard,"arien-13-挑战者");Assert.That(g.View(3).Events.Last(e=>e.Kind=="CardDiscarded").CardId,Is.EqualTo("arien-13-挑战者"));Assert.That(g.View(0).Events.Any(e=>e.Kind=="CardDiscarded" && e.CardId=="arien-13-挑战者"),Is.False);}
  [Test] public void MapEdgeStopsPushWithoutObstacleDiscard()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);var state=State(g);var source=state.Units.Single(u=>u.Id=="hero:0");var target=state.Units.Single(u=>u.Id=="hero:1");bool found=false;for(int x=-15;x<=15&&!found;x++)for(int y=-15;y<=15&&!found;y++){var a=new Hex(x,y);if(cat.Cell(a)==null || cat.Cell(a)!.Obstacle)continue;foreach(var d in new[]{new Hex(1,0),new Hex(0,1),new Hex(1,-1),new Hex(-1,0),new Hex(0,-1),new Hex(-1,1)}){var b=new Hex(x+d.X,y+d.Y);if(cat.Cell(b)==null || cat.Cell(b)!.Obstacle || cat.Cell(new Hex(x+2*d.X,y+2*d.Y))!=null || state.Units.Any(u=>u.Id!=source.Id && u.Id!=target.Id && (u.Position==a || u.Position==b)))continue;source.Position=a;target.Position=b;found=true;break;}}Assert.That(found,Is.True);state.Units.RemoveAll(u=>u.Id!=source.Id && u.Id!=target.Id && u.Team!=source.Team);var command=new Command{ActorSeat=0,Kind=CommandKind.BeginPrimary};new GameRules().Apply(cat,state,command);new GameRules().Apply(cat,state,new Command{ActorSeat=0,Kind=CommandKind.ChooseEffectTarget,Value=target.Id});Assert.That(state.Execution,Is.Null);Assert.That(state.Events.Last(e=>e.Kind=="PushStopped").Detail,Is.EqualTo("map_edge"));Assert.That(state.Events.Any(e=>e.Kind=="ForcedDiscardRequired"),Is.False);}
  [Test] public void Old47PendingDefenseKeepsExactBytesAndAuraCancellation()
  {var cat=BattlefieldTests.Catalog();var save=System.IO.File.ReadAllText(System.IO.Path.Combine(ContentTests.Root(),"tests/fixtures/engine47-side-defense.json"));var g=LocalGameFactory.Restore(cat,save);Assert.That(g.ExportSave(),Is.EqualTo(save));Apply(g,2,CommandKind.Defend,"arien-13-挑战者");Assert.That(g.View(0).Effects.Any(e=>e.SourceCardId==SideBySideTests.Card),Is.False);}
  [Test] public void UnprotectedHeavyIsEligibleAndPassiveBonusesCannotExtendRadiusOrPush()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);var state=State(g);state.Units.RemoveAll(u=>u.Team==Team.Red && u.Kind!="hero" && u.Id!="minion:-3,0");state.Players[0].RangeBonus=9;state.Players[0].RangedBonus=9;state.Players[0].MovementBonus=9;new GameRules().Apply(cat,state,new Command{ActorSeat=0,Kind=CommandKind.BeginPrimary});Assert.That(GameRules.LegalEffectTargets(cat,state,0),Does.Contain("minion:-3,0"));new GameRules().Apply(cat,state,new Command{ActorSeat=0,Kind=CommandKind.ChooseEffectTarget,Value="hero:1"});Assert.That(state.Units.Single(u=>u.Id=="hero:1").Position,Is.EqualTo(new Hex(-2,-3)));}
  [Test] public void MinionReturnWaitsUntilPushGroupFinishes()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);Apply(g,0,CommandKind.DebugTeleport,"hero:0",cell:new Hex(-1,-2));Apply(g,0,CommandKind.DebugTeleport,Minion,cell:new Hex(-1,-3));Apply(g,0,CommandKind.BeginPrimary);Push(g,Minion);Assert.That(g.View(0).Pending!.Kind,Is.EqualTo("effect_target"));Assert.That(g.View(0).Events.Any(e=>e.Kind=="MinionReturnRequired"),Is.False);Push(g,"hero:1");Assert.That(g.View(0).Pending!.ResumeAt,Is.EqualTo("blocked_push_discard_target"));Push(g,"hero:1");Assert.That(g.View(1).Pending!.Kind,Is.EqualTo("forced_discard"));Apply(g,1,CommandKind.ForcedDiscard,g.View(1).ForcedDiscardCards.First());Assert.That(g.View(0).Pending!.Kind,Is.EqualTo("minion_return"));g=ChargeTests.Restore(cat,g);while(g.View(0).Pending?.Kind=="minion_return"){int seat=g.View(0).Pending!.ChooserSeat;var option=g.View(seat).MinionReturns.First();Apply(g,seat,CommandKind.ChooseMinionReturn,option.UnitId,cell:option.Destination);}Assert.That(State(g).Execution,Is.Null);Assert.That(g.View(0).Events.Count(e=>e.Kind=="UnitPushed"),Is.EqualTo(2));}
 }
}
