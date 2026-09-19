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
 public sealed class ArmedConspiracyTests
 {
  internal const string Card="sabina-10-武装密谋";
  internal static GameState State(GameSession g)=>new JsonStateCodec().Read(g.ExportSave());
  internal static GameSession Setup(ContentCatalog cat,bool support=true,bool bardTeam=false,bool originalBarrier=false)
  {
   var g=LocalGameFactory.Create(cat,"armed-conspiracy",new[]{"A","B","C","D"},42,true);
   Apply(g,0,CommandKind.DebugPrepare,bardTeam?"wasp,sabina,arien,brogan":"wasp,sabina,brogan,arien");Apply(g,0,CommandKind.DebugEquipCard,Card,target:1);
   Apply(g,0,CommandKind.DebugTeleport,"hero:0",cell:new Hex(7,-10));Apply(g,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(8,-10));Apply(g,0,CommandKind.DebugTeleport,"hero:2",cell:new Hex(7,-9));Apply(g,0,CommandKind.DebugTeleport,"hero:3",cell:bardTeam?new Hex(8,-8):new Hex(6,-9));
   if(originalBarrier)Apply(g,0,CommandKind.DebugEquipCard,"wasp-08-偏转屏障",target:0);
   if(support)Apply(g,0,CommandKind.DebugTeleport,g.View(0).Units.First(u=>u.Team==Team.Red && u.Kind=="melee").Id,cell:new Hex(8,-9));
   if(bardTeam)Apply(g,0,CommandKind.DebugEquipCard,"brogan-10-吟游诗人",target:3);
   string[] cards={"wasp-00-闪耀之刃","sabina-01-拔枪",bardTeam?"arien-00-华丽刀锋":"brogan-00-猛攻",bardTeam?"brogan-10-吟游诗人":"arien-07-潮水"};for(int i=0;i<4;i++)Apply(g,i,CommandKind.SelectCard,cards[i]);Assert.That(g.View(0).ActiveSeat,Is.EqualTo(0));return g;
  }
  internal static void Block(GameSession g){BarrierResponseTests.Attack(g);Apply(g,1,CommandKind.Defend,Card);}
  [Test] public void ContractIsVersionedAndBlocksWithFriendlyMinion()
  {var cat=BattlefieldTests.Catalog();Assert.That(CombatRules.HasDefenseProgram(cat.Card(Card)),Is.True);Assert.That(CombatRules.HasDefenseProgram(cat.Card(Card),51),Is.False);var g=Setup(cat);BarrierResponseTests.Attack(g);Assert.That(g.View(1).DefenseOptions.Any(o=>o.CardId==Card && o.Block && o.Assessment.Successful),Is.True);}
  [Test] public void SuccessfulBlockProtectsOnlyFromOtherEnemyAndIsPrivate()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);Block(g);var s=State(g);var u=s.Units.Single(x=>x.Seat==1);Assert.That(EffectRules.CanAffect(s,0,u),Is.True);Assert.That(EffectRules.CanAffect(s,1,u),Is.True);Assert.That(EffectRules.CanAffect(s,3,u),Is.True);Assert.That(EffectRules.CanAffect(s,2,u),Is.False);Assert.That(EffectRules.CanTraverseUnits(s,u),Is.False);Assert.That(g.View(1).Effects.Any(e=>e.SourceCardId==Card),Is.True);Assert.That(g.View(0).Effects.Any(e=>e.SourceCardId==Card),Is.False);Assert.That(g.View(null).Events.Any(e=>e.CardId==Card),Is.False);ChargeTests.Restore(cat,g);}
  [Test] public void OtherEnemyAttackCannotSelectProtectedHeroButMinionStillCounts()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);Block(g);Apply(g,2,CommandKind.BeginPrimary);Assert.That(g.View(2).AttackTargets,Does.Not.Contain("hero:1"));Assert.That(g.View(2).AttackTargets,Does.Contain(g.View(0).Units.Single(u=>u.Position==new Hex(8,-9)).Id));var before=g.ExportSave();Assert.That(g.Execute(2,Cmd(g,2,CommandKind.ChooseAttackTarget,"hero:1")).Accepted,Is.False);Assert.That(g.ExportSave(),Is.EqualTo(before));ChargeTests.Restore(cat,g);}
  [Test] public void MissingSupportRejectsWithoutDiscardOrEffect()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat,false);BarrierResponseTests.Attack(g);var before=g.ExportSave();Assert.That(g.Execute(1,Cmd(g,1,CommandKind.Defend,Card)).Accepted,Is.False);Assert.That(g.ExportSave(),Is.EqualTo(before));Assert.That(g.View(1).DefenseRestrictions[Card],Is.EqualTo("requires_adjacent_friendly_minion"));}
  [TestCase("melee",true)] [TestCase("ranged",true)] [TestCase("heavy",true)] [TestCase("marker",false)]
  public void SupportChecksPresenceAndActualMinionKinds(string kind,bool allowed)
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);BarrierResponseTests.Attack(g);var s=State(g);s.Units.RemoveAll(u=>u.Kind!="hero");s.Units.Add(new UnitState{Id="support",Kind=kind,Team=Team.Red,Position=new Hex(8,-9)});Assert.That(CombatRules.DefenseOptions(cat,s,1).Any(o=>o.CardId==Card),Is.EqualTo(allowed));}
  [Test] public void UnblockableCannotUseCardEvenWithSupport()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);BarrierResponseTests.Attack(g);var s=State(g);s.Execution.Attack.Unblockable=true;Assert.That(CombatRules.DefenseOptions(cat,s,1).Any(o=>o.CardId==Card),Is.False);}
  [Test] public void OriginalAttackSuppressionStillAffectsDefender()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);Block(g);var s=State(g);Assert.That(EffectRules.SkillRestriction(cat,s,1,cat.Card("sabina-07-指挥")),Is.EqualTo("wasp-00-闪耀之刃"));}
  [Test] public void TurnEndExpiresProtection()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);Block(g);IronWallTests.FinishTurn(g);Assert.That(g.View(1).Turn,Is.EqualTo(2));Assert.That(g.View(1).Effects.Any(e=>e.SourceCardId==Card),Is.False);ChargeTests.Restore(cat,g);}
  [Test] public void Old51PendingMovementRetainsFullImmunityAndExactSaveBytes()
  {var cat=BattlefieldTests.Catalog();var save=System.IO.File.ReadAllText(System.IO.Path.Combine(ContentTests.Root(),"tests/fixtures/engine51-moment-move.json"));var g=LocalGameFactory.Restore(cat,save);Assert.That(g.ExportSave(),Is.EqualTo(save));Apply(g,0,CommandKind.ChooseEffectMove,"skip");Apply(g,0,CommandKind.ChooseEffectMove,cell:new Hex(-1,-1));ChargeTests.Restore(cat,g);}

  [Test] public void FriendlyBardCanRecoverAndCancelProtection()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat,bardTeam:true);Block(g);Apply(g,2,CommandKind.Pass);Apply(g,1,CommandKind.Pass);Apply(g,3,CommandKind.BeginPrimary);Assert.That(g.View(3).EffectTargets,Does.Contain("hero:1"));Apply(g,3,CommandKind.ChooseEffectTarget,"hero:1");ChargeTests.Restore(cat,g);Apply(g,1,CommandKind.ChooseRecoveredCard,Card);Assert.That(g.View(1).Effects.Any(e=>e.SourceCardId==Card),Is.False);Assert.That(g.View(1).OwnCards.Single(c=>c.CardId==Card).Zone,Is.EqualTo(CardZone.InHand));ChargeTests.Restore(cat,g);}
  [Test] public void NonOwnerAndDuplicateDefenseDoNotCreateExtraProtection()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);BarrierResponseTests.Attack(g);var before=g.ExportSave();Assert.That(g.Execute(0,Cmd(g,0,CommandKind.Defend,Card)).Accepted,Is.False);Assert.That(g.ExportSave(),Is.EqualTo(before));var cmd=Cmd(g,1,CommandKind.Defend,Card);Assert.That(g.Execute(1,cmd).Accepted,Is.True);Assert.That(g.Execute(1,cmd).Duplicate,Is.True);Assert.That(State(g).Effects.Count(e=>e.SourceCardId==Card),Is.EqualTo(1));var effect=State(g).Effects.Single(e=>e.SourceCardId==Card);Assert.That(effect.ExemptControllerSeat,Is.EqualTo(0));Assert.That(g.View(null).Effects.Single(e=>e.Kind==EffectKind.OtherEnemyActionImmunity).ExemptControllerSeat,Is.EqualTo(0));ChargeTests.Restore(cat,g);}
  [Test] public void ImmunityBeginsBeforeOriginalAttackAftereffects()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);Block(g);var events=State(g).Events;Assert.That(events.FindIndex(e=>e.Kind=="EffectActivated" && e.CardId==Card),Is.LessThan(events.FindIndex(e=>e.Kind=="DefenseResolved")));Assert.That(events.FindIndex(e=>e.Kind=="EffectActivated" && e.CardId==Card),Is.LessThan(events.FindIndex(e=>e.Kind=="EffectActivated" && e.CardId=="wasp-00-闪耀之刃")));}
  [Test] public void SourceDefeatCancelsItsProtection()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);Block(g);Apply(g,0,CommandKind.DebugDefeatHero,"hero:1",target:2);Assert.That(State(g).Effects.Any(e=>e.SourceCardId==Card),Is.False);ChargeTests.Restore(cat,g);}
  [Test] public void OriginalAttackerDefeatDoesNotCancelDefendersSource()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);Block(g);Apply(g,0,CommandKind.DebugDefeatHero,"hero:0",target:1);Assert.That(State(g).Effects.Single(e=>e.SourceCardId==Card).ExemptControllerSeat,Is.EqualTo(0));Assert.That(EffectRules.CanAffect(State(g),2,State(g).Units.Single(u=>u.Seat==1)),Is.False);ChargeTests.Restore(cat,g);}
  // Isolated continuous-effect query: changes controller of an existing aura to contrast the exempt attacker.
  [Test] public void OtherEnemyAuraIsIgnoredButOriginalAttackerAuraStillApplies()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);Block(g);var s=State(g);var aura=s.Effects.Single(e=>e.SourceCardId=="wasp-00-闪耀之刃");aura.ControllerSeat=2;aura.SourceUnitId="hero:2";Assert.That(EffectRules.SkillRestriction(cat,s,1,cat.Card("sabina-06-并肩作战")),Is.Empty);aura.Kind=EffectKind.MovementBoundary;aura.AreaKind=EffectAreaKind.Adjacent;var u=s.Units.Single(x=>x.Seat==1);Assert.That(EffectRules.CanMoveAcross(cat,s,u,u.Position,new Hex(9,-10)),Is.True);aura.ControllerSeat=0;aura.SourceUnitId="hero:0";Assert.That(EffectRules.CanMoveAcross(cat,s,u,u.Position,new Hex(9,-10)),Is.False);}
  [TestCase(true)] [TestCase(false)] public void NumericStrengthAndAttackTypeDoNotWeakenConditionalBlock(bool ranged)
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);BarrierResponseTests.Attack(g);var s=State(g);s.Execution.Attack.Ranged=ranged;s.Execution.Attack.FinalAttack=99;Assert.That(CombatRules.DefenseOptions(cat,s,1).Single(o=>o.CardId==Card).Assessment.Successful,Is.True);}

  [Test] public void OtherEnemyRetaliationCannotForceProtectedAttackerToDiscard()
  {var cat=BattlefieldTests.Catalog();var g=LocalGameFactory.Create(cat,"conspiracy-retaliation",new[]{"A","B","C","D"},42,true);Apply(g,0,CommandKind.DebugPrepare,"brogan,sabina,wasp,arien");Apply(g,0,CommandKind.DebugEquipCard,Card,target:1);Apply(g,0,CommandKind.DebugEquipCard,"wasp-08-偏转屏障",target:2);Apply(g,0,CommandKind.DebugTeleport,"hero:0",cell:new Hex(7,-10));Apply(g,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(8,-10));Apply(g,0,CommandKind.DebugTeleport,"hero:2",cell:new Hex(6,-8));Apply(g,0,CommandKind.DebugTeleport,"hero:3",cell:new Hex(6,-9));Apply(g,0,CommandKind.DebugTeleport,"minion:-1,-3",cell:new Hex(8,-9));string[] cards={"brogan-00-猛攻","sabina-01-拔枪","wasp-13-控物","arien-07-潮水"};for(int i=0;i<4;i++)Apply(g,i,CommandKind.SelectCard,cards[i]);Block(g);Apply(g,2,CommandKind.Pass);Apply(g,1,CommandKind.BeginPrimary);Apply(g,1,CommandKind.ChooseAttackTarget,"hero:2");int count=g.View(1).OwnCards.Count(c=>c.Zone==CardZone.InHand);Apply(g,2,CommandKind.Defend,"wasp-08-偏转屏障");Assert.That(g.View(1).OwnCards.Count(c=>c.Zone==CardZone.InHand),Is.EqualTo(count));Assert.That(g.View(1).Pending?.Kind,Is.Not.EqualTo("forced_discard"));Assert.That(g.View(1).Events.Single(e=>e.Kind=="ForcedDiscardSkipped").Detail,Is.EqualTo("immune"));Assert.That(g.View(1).Effects.Any(e=>e.SourceCardId==Card),Is.True);ChargeTests.Restore(cat,g);}
  [Test] public void OriginalEnemyBarrierStillForcesProtectedAttackerToDiscard()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat,originalBarrier:true);Block(g);Apply(g,0,CommandKind.DebugTeleport,"hero:0",cell:new Hex(6,-8));Apply(g,2,CommandKind.Pass);Apply(g,1,CommandKind.BeginPrimary);Apply(g,1,CommandKind.ChooseAttackTarget,"hero:0");Apply(g,0,CommandKind.Defend,"wasp-08-偏转屏障");Assert.That(g.View(1).Pending.Kind,Is.EqualTo("forced_discard"));Assert.That(g.View(1).Pending.ChooserSeat,Is.EqualTo(1));ChargeTests.Restore(cat,g);Apply(g,1,CommandKind.ForcedDiscard,"sabina-17-演练");Assert.That(g.View(1).OwnCards.Single(c=>c.CardId=="sabina-17-演练").Zone,Is.EqualTo(CardZone.Discarded));ChargeTests.Restore(cat,g);}
 }
}
