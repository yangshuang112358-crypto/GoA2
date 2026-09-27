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
 public sealed class ShiftingStepTests
 {
  internal const string Card="arien-10-移形换步";
  internal static GameSession Ready(ContentCatalog cat)=>SurgeTests.Ready(cat,Card);
  [Test] public void ExactContractAndEngineGate()
  {var c=BattlefieldTests.Catalog().Card(Card);Assert.That(c.SubtypeValue,Is.EqualTo(3));Assert.That(CombatRules.HasPrimaryProgram(c),Is.True);Assert.That(CombatRules.HasPrimaryProgram(c,79),Is.False);c.Text+="友方";Assert.That(CombatRules.HasPrimaryProgram(c),Is.False);}
  [TestCase(1)] [TestCase(2)] public void EnemyOrFriendlyHeroCanSwapWithoutMovement(int seat)
  {
   var cat=BattlefieldTests.Catalog();var g=Ready(cat);if(seat==2)Apply(g,0,CommandKind.DebugTeleport,"hero:2",cell:new Hex(1,-4));
   var from=g.View(0).Units.Single(u=>u.Seat==0).Position;var to=g.View(0).Units.Single(u=>u.Seat==seat).Position;Apply(g,0,CommandKind.BeginPrimary);g=ChargeTests.Restore(cat,g);
   Apply(g,0,CommandKind.ChooseEffectTarget,"hero:"+seat);Assert.That(g.View(0).Units.Single(u=>u.Seat==0).Position,Is.EqualTo(to));Assert.That(g.View(0).Units.Single(u=>u.Seat==seat).Position,Is.EqualTo(from));
   Assert.That(g.View(0).Events.Any(e=>e.Kind=="UnitMoved" || e.Kind=="AttackCalculated"),Is.False);ChargeTests.Restore(cat,g);
  }
  [Test] public void OpponentImmunityAndDisplacementProtectionAreRespectedButAttackImmunityIsNotRelevant()
  {
   var cat=BattlefieldTests.Catalog();var g=Ready(cat);Apply(g,0,CommandKind.BeginPrimary);var s=new JsonStateCodec().Read(g.ExportSave());var e=new ActiveEffect{SourceCardId="shargatha-17-至死不渝",SourceUnitId="hero:1",ControllerSeat=1,Kind=EffectKind.AttackActionImmunity,Window=EffectTimeline.Create(s.Round,s.Turn,4,EffectDuration.ThisTurn)};s.Effects.Add(e);
   Assert.That(GameRules.LegalEffectTargets(cat,s,0),Does.Contain("hero:1"));e.Kind=EffectKind.ImmunityAndUnitTraversal;Assert.That(GameRules.LegalEffectTargets(cat,s,0),Does.Not.Contain("hero:1"));e.Kind=EffectKind.FriendlyDisplacementProtection;e.AreaKind=EffectAreaKind.Adjacent;Assert.That(GameRules.LegalEffectTargets(cat,s,0),Does.Not.Contain("hero:1"));
  }
  [Test] public void RangeUsesRangedBonusAndSwapCanCrossStaticBoundary()
  {
   var cat=BattlefieldTests.Catalog();var g=Ready(cat);Apply(g,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(0,-1));Apply(g,0,CommandKind.BeginPrimary);var s=new JsonStateCodec().Read(g.ExportSave());s.Players[0].RangeBonus=8;
   Assert.That(GameRules.LegalEffectTargets(cat,s,0),Does.Not.Contain("hero:1"));s.Players[0].RangedBonus=1;Assert.That(GameRules.LegalEffectTargets(cat,s,0),Does.Contain("hero:1"));
   s.Effects.Add(new ActiveEffect{SourceCardId="wasp-06-静电封锁",SourceUnitId="hero:3",ControllerSeat=3,Kind=EffectKind.MovementBoundary,AreaKind=EffectAreaKind.Adjacent,Window=EffectTimeline.Create(s.Round,s.Turn,4,EffectDuration.ThisTurn)});
   new GameRules().Apply(cat,s,new Command{ActorSeat=0,Kind=CommandKind.ChooseEffectTarget,Value="hero:1"});Assert.That(s.Units.Single(u=>u.Seat==0).Position,Is.EqualTo(new Hex(0,-1)));
  }
  [Test] public void EnemyMinionReturnsBeforeTheSwapCardResolves()
  {
   var cat=BattlefieldTests.Catalog();var g=Ready(cat);Apply(g,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(2,-4));Apply(g,0,CommandKind.BeginPrimary);Apply(g,0,CommandKind.ChooseEffectTarget,"minion:-1,-3");
   Assert.That(g.View(1).Pending.Kind,Is.EqualTo("minion_return"));g=ChargeTests.Restore(cat,g);Apply(g,1,CommandKind.ChooseMinionReturn,"minion:-1,-3",cell:new Hex(0,-4));Apply(g,1,CommandKind.ChooseMinionReturn,"minion:-1,-3",cell:new Hex(0,-3));
   Assert.That(g.View(0).Units.Single(u=>u.Seat==0).Position,Is.EqualTo(new Hex(-1,-3)));Assert.That(g.View(0).Events.Count(e=>e.Kind=="CardResolved" && e.CardId==Card),Is.EqualTo(1));ChargeTests.Restore(cat,g);
  }
  [Test] public void IllegalSelfAndWrongSeatAreAtomicAndDuplicateDoesNotSwapBack()
  {
   var cat=BattlefieldTests.Catalog();var g=Ready(cat);Apply(g,0,CommandKind.BeginPrimary);string save=g.ExportSave();
   foreach(var c in new[]{Cmd(g,0,CommandKind.ChooseEffectTarget,"hero:0"),Cmd(g,1,CommandKind.ChooseEffectTarget,"hero:1"),Cmd(g,0,CommandKind.ChooseEffectTarget,"skip")}){Assert.That(g.Execute(c.ActorSeat,c).Accepted,Is.False);Assert.That(g.ExportSave(),Is.EqualTo(save));}
   var swap=Cmd(g,0,CommandKind.ChooseEffectTarget,"hero:1");Assert.That(g.Execute(0,swap).Accepted,Is.True);save=g.ExportSave();Assert.That(g.Execute(0,swap).Duplicate,Is.True);Assert.That(g.ExportSave(),Is.EqualTo(save));ChargeTests.Restore(cat,g);
  }
  [Test] public void Old79RepeatSaveKeepsExactBytes()
  {
   var cat=BattlefieldTests.Catalog();string save=File.ReadAllText(Path.Combine(ContentTests.Root(),"tests/fixtures/engine79-heart-control-repeat.json"));var g=LocalGameFactory.Restore(cat,save);
   Assert.That(g.ExportSave(),Is.EqualTo(save));Assert.That(g.View(0).SupportedPrimaryCards,Does.Not.Contain(Card));
  }
  [Test] public void FriendlyMinionAlsoSwapsAndReturnsUnderItsOwnCaptain()
  {
   var cat=BattlefieldTests.Catalog();var g=Ready(cat);Apply(g,0,CommandKind.DebugTeleport,"minion:1,0",cell:new Hex(1,-4));Apply(g,0,CommandKind.BeginPrimary);
   Assert.That(g.View(0).EffectTargets,Does.Contain("minion:1,0"));Apply(g,0,CommandKind.ChooseEffectTarget,"minion:1,0");
   Assert.That(g.View(0).Units.Single(u=>u.Seat==0).Position,Is.EqualTo(new Hex(1,-4)));Assert.That(g.View(0).Pending.Kind,Is.EqualTo("minion_return"));Assert.That(g.View(0).Pending.ChooserSeat,Is.EqualTo(0));ChargeTests.Restore(cat,g);
  }
  [Test] public void OrdinarySwapDoesNotTriggerLordOfTidesBasicSkillBattle()
  {
   var cat=BattlefieldTests.Catalog();var g=LordOfTidesTests.Ready(cat,played:Card);Apply(g,0,CommandKind.BeginPrimary);
   string target=g.View(0).EffectTargets.First();Apply(g,0,CommandKind.ChooseEffectTarget,target);
   Assert.That(g.View(0).Pending?.ResumeAt,Is.Not.EqualTo("action_minion_battle_offer"));Assert.That(g.View(0).Events.Any(e=>e.Kind=="CardResolved" && e.CardId==Card),Is.True);ChargeTests.Restore(cat,g);
  }
 }
}
