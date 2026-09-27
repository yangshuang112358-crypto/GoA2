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
 public sealed class TsunamiTests
 {
  internal const string Card="arien-05-巨浪滔天";
  internal static GameSession Ready(ContentCatalog cat)
  {var g=SurgeTests.Ready(cat,Card);Apply(g,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(0,-3));return g;}
  [Test] public void ExactTextAndOldVersionGate()
  {var c=BattlefieldTests.Catalog().Card(Card);Assert.That(CombatRules.HasPrimaryProgram(c),Is.True);Assert.That(CombatRules.HasPrimaryProgram(c,76),Is.False);c.Text+="任意";Assert.That(CombatRules.HasPrimaryProgram(c),Is.False);}
  [Test] public void MoveOriginalTargetIntoAdjacencyThenPushItFromItsNewPosition()
  {
   var cat=BattlefieldTests.Catalog();var g=Ready(cat);SurgeTests.Attack(g);Assert.That(g.View(0).Pending.ResumeAt,Is.EqualTo("target_unit_move"));Assert.That(g.View(0).Pending.UnitId,Is.EqualTo("hero:1"));
   Assert.That(g.View(1).EffectMoves,Is.Empty);g=ChargeTests.Restore(cat,g);Apply(g,0,CommandKind.ChooseEffectMove,cell:new Hex(0,-4));
   Assert.That(g.View(0).EffectTargets,Does.Contain("hero:1"));g=ChargeTests.Restore(cat,g);Apply(g,0,CommandKind.ChooseEffectTarget,"hero:1");
   Assert.That(g.View(0).Units.Single(u=>u.Seat==1).Position,Is.EqualTo(new Hex(0,-3)));Assert.That(g.View(0).Events.Count(e=>e.Kind=="UnitMoved"),Is.EqualTo(1));ChargeTests.Restore(cat,g);
  }
  [Test] public void SkippingTargetMoveStillOffersIndependentPush()
  {
   var cat=BattlefieldTests.Catalog();var g=Ready(cat);SurgeTests.Attack(g);Apply(g,0,CommandKind.ChooseEffectMove,"skip");Assert.That(g.View(0).EffectTargets,Is.EqualTo(new[]{"hero:3"}));
   Apply(g,0,CommandKind.ChooseEffectTarget,"hero:3");Assert.That(g.View(0).Units.Single(u=>u.Seat==3).Position,Is.EqualTo(new Hex(-2,-3)));ChargeTests.Restore(cat,g);
  }
  [Test] public void DefeatedOriginalTargetSkipsMoveButDoesNotCancelPush()
  {
   var cat=BattlefieldTests.Catalog();var g=Ready(cat);SurgeTests.Attack(g,false);Assert.That(g.View(0).Pending.ResumeAt,Is.EqualTo("single_push_target"));
   Apply(g,0,CommandKind.ChooseEffectTarget,"skip");Assert.That(g.View(0).Events.Any(e=>e.Kind=="UnitMoved"),Is.False);ChargeTests.Restore(cat,g);
  }
  [TestCase(EffectKind.AttackActionImmunity)] [TestCase(EffectKind.FriendlyDisplacementProtection)]
  public void AttackImmunityOrDisplacementProtectionPreventsPostAttackMovement(EffectKind kind)
  {
   var cat=BattlefieldTests.Catalog();var g=Ready(cat);Apply(g,0,CommandKind.BeginPrimary);Apply(g,0,CommandKind.ChooseAttackTarget,"hero:1");
   var s=new JsonStateCodec().Read(g.ExportSave());s.Effects.Add(new ActiveEffect{SourceCardId=kind==EffectKind.AttackActionImmunity?"shargatha-17-至死不渝":"brogan-06-铜墙铁壁",SourceUnitId="hero:1",ControllerSeat=1,Kind=kind,AreaKind=EffectAreaKind.Adjacent,Window=EffectTimeline.Create(s.Round,s.Turn,4,EffectDuration.ThisTurn)});
   new GameRules().Apply(cat,s,new Command{ActorSeat=1,Kind=CommandKind.Defend,Value=CounterattackTests.Slash});
   Assert.That(s.Pending?.ResumeAt,Is.Not.EqualTo("target_unit_move"));Assert.That(s.Units.Single(u=>u.Seat==1).Position,Is.EqualTo(new Hex(0,-3)));
  }
  [Test] public void InternalMoveHonorsStaticBoundaryAndIgnoresBothMovementBonuses()
  {
   var cat=BattlefieldTests.Catalog();var g=Ready(cat);SurgeTests.Attack(g);var s=new JsonStateCodec().Read(g.ExportSave());s.Players[0].MovementBonus=9;s.Players[1].MovementBonus=9;
   Assert.That(GameRules.LegalEffectMoves(cat,s,0).All(m=>m.Destination.Distance(new Hex(0,-3))==1),Is.True);
   s.Units.Single(u=>u.Seat==2).Position=new Hex(1,-3);s.Effects.Add(new ActiveEffect{SourceCardId="wasp-06-静电封锁",SourceUnitId="hero:2",ControllerSeat=2,Kind=EffectKind.MovementBoundary,AreaKind=EffectAreaKind.Adjacent,Window=EffectTimeline.Create(s.Round,s.Turn,4,EffectDuration.ThisTurn)});
   Assert.That(GameRules.LegalEffectMoves(cat,s,0).Select(m=>m.Destination),Has.No.Member(new Hex(0,-4)));
   Assert.That(GameRules.LegalEffectMoves(cat,s,0).All(m=>m.Destination.Distance(new Hex(1,-3))<=1),Is.True);
  }
  [Test] public void WrongSeatIllegalLandingAndRepeatedMoveAreAtomic()
  {
   var cat=BattlefieldTests.Catalog();var g=Ready(cat);SurgeTests.Attack(g);string save=g.ExportSave();
   foreach(var c in new[]{Cmd(g,1,CommandKind.ChooseEffectMove,destination:new Hex(0,-4)),Cmd(g,0,CommandKind.ChooseEffectMove,destination:new Hex(0,-5)),Cmd(g,0,CommandKind.ChooseEffectTarget,"hero:3")})
   {Assert.That(g.Execute(c.ActorSeat,c).Accepted,Is.False);Assert.That(g.ExportSave(),Is.EqualTo(save));}
   var move=Cmd(g,0,CommandKind.ChooseEffectMove,destination:new Hex(0,-4));Assert.That(g.Execute(0,move).Accepted,Is.True);save=g.ExportSave();Assert.That(g.Execute(0,move).Duplicate,Is.True);Assert.That(g.ExportSave(),Is.EqualTo(save));ChargeTests.Restore(cat,g);
  }
  [Test] public void ActualCounterattackWaitsForTargetMoveAndOptionalPush()
  {
   var cat=BattlefieldTests.Catalog();var g=SurgeTests.Ready(cat,Card,true);SurgeTests.Attack(g);Assert.That(g.View(0).Pending.ResumeAt,Is.EqualTo("target_unit_move"));g=ChargeTests.Restore(cat,g);
   Apply(g,0,CommandKind.ChooseEffectMove,cell:new Hex(1,-5));Assert.That(g.View(0).Pending.ResumeAt,Is.EqualTo("single_push_target"));g=ChargeTests.Restore(cat,g);
   Apply(g,0,CommandKind.ChooseEffectTarget,"hero:3");Assert.That(g.View(1).Pending.Kind,Is.EqualTo("discard_attack"));
   Apply(g,1,CommandKind.ChooseDiscardAttack,CounterattackTests.Slash);Apply(g,1,CommandKind.ChooseAttackTarget,"hero:0");Apply(g,0,CommandKind.DeclineDefense);ChargeTests.Restore(cat,g);
  }
  [Test] public void Old76PushChoiceRetainsExactSave()
  {
   var cat=BattlefieldTests.Catalog();string save=File.ReadAllText(Path.Combine(ContentTests.Root(),"tests/fixtures/engine76-rising-tide-choice.json"));var g=LocalGameFactory.Restore(cat,save);
   Assert.That(g.ExportSave(),Is.EqualTo(save));Assert.That(g.View(0).SupportedPrimaryCards,Does.Not.Contain(Card));
  }
 }
}
