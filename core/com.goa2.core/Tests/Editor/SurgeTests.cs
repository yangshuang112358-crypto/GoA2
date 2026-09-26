using System.IO;
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
 public sealed class SurgeTests
 {
  internal const string Card="arien-01-汹涌";
  internal static GameSession Ready(ContentCatalog cat,string card=Card,bool counter=false)
  {
   var g=LocalGameFactory.Create(cat,"surge",new[]{"A","B","C","D"},42,true);
   Apply(g,0,CommandKind.DebugPrepare,"arien,shargatha,brogan,wasp");Apply(g,0,CommandKind.DebugEquipCard,card,target:0);
   var pos=new[]{new Hex(0,-5),new Hex(0,-4),new Hex(5,-8),new Hex(-1,-4)};
   for(int i=0;i<4;i++)Apply(g,0,CommandKind.DebugTeleport,"hero:"+i,cell:pos[i]);
   if(counter)Apply(g,0,CommandKind.DebugTeleport,"minion:1,0",cell:new Hex(1,-4));
   string[] cards={card,counter?CounterattackTests.Card:"shargatha-13-石化","brogan-06-铜墙铁壁","wasp-07-抵挡屏障"};
   for(int i=0;i<4;i++)Apply(g,i,CommandKind.SelectCard,cards[i]);
   if(counter){OpportuneMomentTests.AdvanceTo(g,1);Apply(g,1,CommandKind.BeginPrimary);Apply(g,1,CommandKind.ChooseAttackTarget,"minion:1,0");}
   OpportuneMomentTests.AdvanceTo(g,0);return g;
  }
  internal static void Attack(GameSession g,bool defend=true)
  {Apply(g,0,CommandKind.BeginPrimary);Apply(g,0,CommandKind.ChooseAttackTarget,"hero:1");if(defend)Apply(g,1,CommandKind.Defend,CounterattackTests.Slash);else Apply(g,1,CommandKind.DeclineDefense);}
  [Test] public void ExactTextAndEngineGate()
  {var c=BattlefieldTests.Catalog().Card(Card);Assert.That(CombatRules.HasPrimaryProgram(c),Is.True);Assert.That(CombatRules.HasPrimaryProgram(c,74),Is.False);c.Text+="最多";Assert.That(CombatRules.HasPrimaryProgram(c),Is.False);}
  [Test] public void SuccessfulDefenseCanPushSameHeroAndFixedDistanceDoesNotOfferAnotherSkip()
  {
   var cat=BattlefieldTests.Catalog();var g=Ready(cat);Attack(g);Assert.That(g.View(0).Pending.ResumeAt,Is.EqualTo("single_push_target"));g=ChargeTests.Restore(cat,g);
   Assert.That(g.View(1).EffectTargets,Is.Empty);var choose=Cmd(g,0,CommandKind.ChooseEffectTarget,"hero:1");Assert.That(g.Execute(0,choose).Accepted,Is.True);string save=g.ExportSave();
   Assert.That(g.Execute(0,choose).Duplicate,Is.True);Assert.That(g.ExportSave(),Is.EqualTo(save));Assert.That(g.View(0).Pending?.ResumeAt,Is.Not.EqualTo("single_push_distance"));
   Assert.That(g.View(0).Units.Single(u=>u.Seat==1).Position,Is.EqualTo(new Hex(0,-3)));ChargeTests.Restore(cat,g);
  }
  [Test] public void OriginalHeroDefeatStillAllowsADifferentPushTarget()
  {
   var cat=BattlefieldTests.Catalog();var g=Ready(cat);Attack(g,false);Assert.That(g.View(0).EffectTargets,Is.EqualTo(new[]{"hero:3"}));
   Apply(g,0,CommandKind.ChooseEffectTarget,"hero:3");Assert.That(g.View(0).Units.Single(u=>u.Seat==3).Position,Is.EqualTo(new Hex(-2,-3)));ChargeTests.Restore(cat,g);
  }
  [Test] public void CanDeclineWholeOptionalPushAndNoTargetAlsoResolvesNormally()
  {
   var cat=BattlefieldTests.Catalog();var g=Ready(cat);Attack(g);Apply(g,0,CommandKind.ChooseEffectTarget,"skip");Assert.That(g.View(0).Events.Any(e=>e.Kind=="UnitPushed"),Is.False);ChargeTests.Restore(cat,g);
   g=Ready(cat);Apply(g,0,CommandKind.DebugTeleport,"hero:3",cell:new Hex(7,-8));Attack(g,false);
   Assert.That(g.View(0).Pending,Is.Null);Assert.That(g.View(0).Events.Any(e=>e.Kind=="CardEffectStopped"),Is.False);ChargeTests.Restore(cat,g);
  }
  [Test] public void FixedPushCanLegallyMoveZeroWhenBlocked()
  {
   var cat=BattlefieldTests.Catalog();var g=Ready(cat);Apply(g,0,CommandKind.DebugTeleport,"hero:2",cell:new Hex(0,-3));Attack(g);Apply(g,0,CommandKind.ChooseEffectTarget,"hero:1");
   Assert.That(g.View(0).Events.Last(e=>e.Kind=="UnitPushed").Path.Count,Is.EqualTo(1));Assert.That(g.View(0).Events.Last(e=>e.Kind=="PushStopped").Detail,Is.EqualTo("occupied"));ChargeTests.Restore(cat,g);
  }
  [Test] public void AttackImmunityExcludesPostAttackPushAndMovementBonusesDoNotExtendIt()
  {
   var cat=BattlefieldTests.Catalog();var g=Ready(cat);Attack(g);var s=new JsonStateCodec().Read(g.ExportSave());
   s.Effects.Add(new ActiveEffect{SourceCardId="shargatha-17-至死不渝",SourceUnitId="hero:1",ControllerSeat=1,Kind=EffectKind.AttackActionImmunity,Window=EffectTimeline.Create(s.Round,s.Turn,4,EffectDuration.ThisTurn)});
   Assert.That(GameRules.LegalEffectTargets(cat,s,0),Does.Not.Contain("hero:1"));s.Players[0].MovementBonus=8;
   new GameRules().Apply(cat,s,new Command{ActorSeat=0,Kind=CommandKind.ChooseEffectTarget,Value="hero:3"});Assert.That(s.Units.Single(u=>u.Seat==3).Position,Is.EqualTo(new Hex(-2,-3)));
  }
  [Test] public void ExtendedAttackRangeDoesNotExpandAdjacentPushTargets()
  {
   var cat=BattlefieldTests.Catalog();var g=Ready(cat);Apply(g,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(0,-3));var s=new JsonStateCodec().Read(g.ExportSave());s.Players[0].RangedBonus=1;
   Assert.That(CombatRules.AttackTargets(cat,s,0),Does.Contain("hero:1"));var r=new GameRules();r.Apply(cat,s,new Command{ActorSeat=0,Kind=CommandKind.BeginPrimary});r.Apply(cat,s,new Command{ActorSeat=0,Kind=CommandKind.ChooseAttackTarget,Value="hero:1"});
   r.Apply(cat,s,new Command{ActorSeat=1,Kind=CommandKind.Defend,Value=CounterattackTests.Slash});Assert.That(GameRules.LegalEffectTargets(cat,s,0),Is.EqualTo(new[]{"hero:3"}));
  }
  [Test] public void WrongChooserAndFriendlyTargetsLeaveSaveUnchanged()
  {
   var cat=BattlefieldTests.Catalog();var g=Ready(cat);Attack(g);string save=g.ExportSave();
   foreach(var c in new[]{Cmd(g,1,CommandKind.ChooseEffectTarget,"hero:1"),Cmd(g,0,CommandKind.ChooseEffectTarget,"hero:2"),Cmd(g,0,CommandKind.ChooseEffectMove,"skip")})
   {Assert.That(g.Execute(c.ActorSeat,c).Accepted,Is.False);Assert.That(g.ExportSave(),Is.EqualTo(save));}
  }
  [Test] public void ActualCounterattackWaitsUntilOptionalPushEnds()
  {
   var cat=BattlefieldTests.Catalog();var g=Ready(cat,counter:true);Attack(g);Assert.That(g.View(0).Pending.ResumeAt,Is.EqualTo("single_push_target"));g=ChargeTests.Restore(cat,g);
   Apply(g,0,CommandKind.ChooseEffectTarget,"hero:3");Assert.That(g.View(1).Pending.Kind,Is.EqualTo("discard_attack"));g=ChargeTests.Restore(cat,g);
   Apply(g,1,CommandKind.ChooseDiscardAttack,CounterattackTests.Slash);Apply(g,1,CommandKind.ChooseAttackTarget,"hero:0");Apply(g,0,CommandKind.DeclineDefense);
   Assert.That(g.View(0).Events.Count(e=>e.Kind=="CardResolved" && e.CardId==Card),Is.EqualTo(1));ChargeTests.Restore(cat,g);
  }
  [Test] public void VictoryDoesNotOfferPostAttackPush()
  {
   var cat=BattlefieldTests.Catalog();var g=Ready(cat);Apply(g,0,CommandKind.DebugSetCrystal,"1",target:1);Attack(g,false);
   Assert.That(g.View(0).Phase,Is.EqualTo(Phase.Finished));Assert.That(g.View(0).Pending,Is.Null);Assert.That(g.View(0).Events.Any(e=>e.Kind=="UnitPushed"),Is.False);ChargeTests.Restore(cat,g);
  }
  [Test] public void Previous74PhantasmPushRetainsBytes()
  {
   var cat=BattlefieldTests.Catalog();string save=File.ReadAllText(Path.Combine(ContentTests.Root(),"tests/fixtures/engine74-savage-kick-phantasm.json"));var g=LocalGameFactory.Restore(cat,save);
   Assert.That(g.ExportSave(),Is.EqualTo(save));Assert.That(g.View(0).SupportedPrimaryCards,Does.Not.Contain(Card));
  }
 }
}
