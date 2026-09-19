using System;
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
 public sealed class GuardMinionTests
 {
  internal const string Card="brogan-07-保卫", Minion="minion:-3,1";
  internal static CommandKind Choice=>(CommandKind)Enum.Parse(typeof(CommandKind),"ChooseMinionProtection");
  internal static GameState State(GameSession g)=>new JsonStateCodec().Read(g.ExportSave());
  internal static GameSession Setup(ContentCatalog cat,bool crossFire=false)
  {
   var g=LocalGameFactory.Create(cat,"guard-minion",new[]{"A","B","C","D"},42,true);Apply(g,0,CommandKind.DebugPrepare,crossFire?"brogan,sabina,wasp,arien":"brogan,wasp,sabina,arien");
   Apply(g,0,CommandKind.DebugTeleport,"hero:0",cell:crossFire?new Hex(-5,2):new Hex(-2,0));Apply(g,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(-2,1));Apply(g,0,CommandKind.DebugTeleport,"hero:2",cell:new Hex(6,-9));Apply(g,0,CommandKind.DebugTeleport,"hero:3",cell:new Hex(7,-9));
   if(crossFire){Apply(g,0,CommandKind.DebugEquipCard,"sabina-02-交叉火力",target:1);Apply(g,0,CommandKind.DebugTeleport,"minion:-1,2",cell:new Hex(-2,2));}
   string[] cards={Card,crossFire?"sabina-06-并肩作战":"wasp-06-静电封锁",crossFire?"wasp-06-静电封锁":"sabina-06-并肩作战","arien-07-潮水"};for(int i=0;i<4;i++)Apply(g,i,CommandKind.SelectCard,cards[i]);Apply(g,crossFire?1:2,CommandKind.Pass);Apply(g,crossFire?2:1,CommandKind.Pass);Assert.That(g.View(0).ActiveSeat,Is.EqualTo(0));return g;
  }
  internal static void Activate(GameSession g){Apply(g,0,CommandKind.BeginPrimary);Apply(g,3,CommandKind.Pass);Assert.That(g.View(0).Phase,Is.EqualTo(Phase.Planning));}
  internal static void Attack(GameSession g,bool spendLast=false)
  {string[] cards={"brogan-00-猛攻","wasp-01-电击","sabina-01-拔枪","arien-00-华丽刀锋"};for(int i=0;i<4;i++)Apply(g,i,CommandKind.SelectCard,cards[i]);OpportuneMomentTests.AdvanceTo(g,1);Apply(g,1,CommandKind.BeginPrimary);Apply(g,1,CommandKind.ChooseAttackTarget,Minion);if(g.View(1).Pending?.Kind=="effect_target"){Apply(g,1,CommandKind.ChooseEffectTarget,spendLast?"hero:0":"skip");if(spendLast)Apply(g,0,CommandKind.ForcedDiscard,"brogan-13-冲拳");}}
  [Test] public void ContractAndVersionGate()
  {var c=BattlefieldTests.Catalog().Card(Card);Assert.That(c.SubtypeValue,Is.EqualTo(2));Assert.That(CombatRules.HasPrimaryProgram(c),Is.True);Assert.That(CombatRules.HasPrimaryProgram(c,52),Is.False);}
  [Test] public void SkillCreatesRoundAuraWithoutDiscardingImmediately()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);int count=g.View(0).OwnCards.Count(c=>c.Zone==CardZone.InHand);Activate(g);Assert.That(g.View(0).OwnCards.Count(c=>c.Zone==CardZone.InHand),Is.EqualTo(count));var effect=g.View(0).Effects.Single(e=>e.SourceCardId==Card);Assert.That(effect.Window.EndTurn,Is.EqualTo(4));ChargeTests.Restore(cat,g);}
  [Test] public void PayBeforeMinionDefeatStopsRewardAndResumesOriginalAttackOnce()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);Activate(g);Attack(g);Assert.That(g.View(0).Pending.Kind,Is.EqualTo("minion_protection"));Assert.That(g.View(0).Pending.ChooserSeat,Is.EqualTo(0));Assert.That(g.View(0).Units.Any(u=>u.Id==Minion),Is.True);Assert.That(g.View(1).Players[1].Gold,Is.Zero);ChargeTests.Restore(cat,g);Apply(g,0,Choice,"brogan-13-冲拳");Assert.That(g.View(0).Units.Any(u=>u.Id==Minion),Is.True);Assert.That(g.View(1).Players[1].Gold,Is.Zero);Assert.That(g.View(0).Events.Count(e=>e.Kind=="MinionDefeatPrevented"),Is.EqualTo(1));Assert.That(g.View(0).Events.Any(e=>e.Kind=="MinionDefeated"),Is.False);Assert.That(g.View(0).Effects.Any(e=>e.SourceCardId==Card),Is.True);Assert.That(g.View(0).Events.Count(e=>e.Kind=="AttackResolved"),Is.EqualTo(1));ChargeTests.Restore(cat,g);}
  [Test] public void DeclineWithCardsStillDefeatsAndAwardsExactlyOnce()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);Activate(g);Attack(g);var cmd=Cmd(g,0,Choice,"skip");Assert.That(g.Execute(0,cmd).Accepted,Is.True);var save=g.ExportSave();Assert.That(g.Execute(0,cmd).Duplicate,Is.True);Assert.That(g.ExportSave(),Is.EqualTo(save));Assert.That(g.View(0).Units.Any(u=>u.Id==Minion),Is.False);Assert.That(g.View(1).Players[1].Gold,Is.EqualTo(2));Assert.That(g.View(0).Events.Count(e=>e.Kind=="MinionDefeated"),Is.EqualTo(1));ChargeTests.Restore(cat,g);}
  [Test] public void EmptyHandDoesNotOpenChoice()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);Activate(g);foreach(var c in g.View(0).OwnCards.Where(c=>c.Zone==CardZone.InHand).ToList())Apply(g,0,CommandKind.DebugDiscard,c.CardId,target:0);Apply(g,1,CommandKind.DebugAttack,Minion+"|0");Assert.That(g.View(0).Pending,Is.Null);Assert.That(g.View(0).Units.Any(u=>u.Id==Minion),Is.False);Assert.That(g.View(1).Players[1].Gold,Is.EqualTo(2));}
  [Test] public void DirectRemovalDoesNotTriggerDefeatPrevention()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);Activate(g);Apply(g,0,CommandKind.DebugRemoveMinion,Minion);Assert.That(g.View(0).Pending,Is.Null);Assert.That(g.View(0).Units.Any(u=>u.Id==Minion),Is.False);Assert.That(g.View(0).Events.Any(e=>e.Kind=="MinionProtectionChoiceRequired"),Is.False);}
  [TestCase(CommandKind.DebugAttack)] [TestCase(CommandKind.DebugDefeatMinion)]
  public void DebugDefeatOrAttackOffersProtectionAndReturnsToPlanning(CommandKind trigger)
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);Activate(g);Apply(g,1,trigger,trigger==CommandKind.DebugAttack?Minion+"|0":Minion,target:1);Assert.That(g.View(0).Pending.Kind,Is.EqualTo("minion_protection"));Apply(g,0,Choice,"brogan-13-冲拳");Assert.That(g.View(0).Phase,Is.EqualTo(Phase.Planning));Assert.That(g.View(0).Units.Any(u=>u.Id==Minion),Is.True);Assert.That(g.View(0).Effects.Any(e=>e.SourceCardId==Card),Is.True);ChargeTests.Restore(cat,g);}
  [Test] public void OtherOwnerInvalidCardsAndStructuralDebugAreAtomic()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);Activate(g);Attack(g);var save=g.ExportSave();foreach(var cmd in new[]{Cmd(g,1,Choice,"skip"),Cmd(g,0,Choice,Card),Cmd(g,0,Choice,"wasp-13-控物"),Cmd(g,0,CommandKind.DebugRemoveMinion,Minion)}){Assert.That(g.Execute(cmd.ActorSeat,cmd).Accepted,Is.False);Assert.That(g.ExportSave(),Is.EqualTo(save));}}
  [Test] public void CostIsPrivateAndDuplicateDoesNotPayTwice()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);Activate(g);Attack(g);var cmd=Cmd(g,0,Choice,"brogan-13-冲拳");Assert.That(g.Execute(0,cmd).Accepted,Is.True);var save=g.ExportSave();Assert.That(g.Execute(0,cmd).Duplicate,Is.True);Assert.That(g.ExportSave(),Is.EqualTo(save));Assert.That(g.View(null).Events.Any(e=>e.CardId=="brogan-13-冲拳"),Is.False);Assert.That(g.View(0).Events.Count(e=>e.Kind=="CardDiscarded" && e.CardId=="brogan-13-冲拳"),Is.EqualTo(1));}
  [Test] public void SourceDefeatCancelsProtection()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);Activate(g);Apply(g,0,CommandKind.DebugDefeatHero,"hero:0",target:1);Apply(g,1,CommandKind.DebugAttack,Minion+"|0");Assert.That(g.View(0).Pending,Is.Null);Assert.That(g.View(0).Units.Any(u=>u.Id==Minion),Is.False);}
  [Test] public void RoundEndExpiresProtectionAfterThreeMoreTurns()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);Activate(g);for(int turn=2;turn<=4;turn++){Assert.That(g.View(0).Turn,Is.EqualTo(turn));for(int i=0;i<4;i++)Apply(g,i,CommandKind.SelectCard,g.View(i).OwnCards.First(c=>c.Zone==CardZone.InHand).CardId);IronWallTests.FinishTurn(g);}Assert.That(g.View(0).Effects.Any(e=>e.SourceCardId==Card),Is.False);ChargeTests.Restore(cat,g);}

  [Test] public void DebugReequipmentCannotLeaveGhostProtectionFromPlayedSource()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);Activate(g);Apply(g,0,CommandKind.DebugEquipCard,Card,target:0);Assert.That(g.View(0).OwnCards.Single(c=>c.CardId==Card).Zone,Is.EqualTo(CardZone.InHand));Assert.That(g.View(0).Effects.Any(e=>e.SourceCardId==Card),Is.False);}

  [TestCase("melee",Team.Blue,true)] [TestCase("ranged",Team.Blue,false)] [TestCase("heavy",Team.Blue,false)] [TestCase("marker",Team.Blue,false)] [TestCase("melee",Team.Red,false)]
  public void ProtectionOnlyCoversActualFriendlyMeleeMinions(string kind,Team team,bool covered)
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);Activate(g);var s=State(g);var u=s.Units.Single(x=>x.Id==Minion);u.Kind=kind;u.Team=team;Assert.That(EffectRules.MinionDefeatProtectors(cat,s,u).Any(),Is.EqualTo(covered));}
  [Test] public void CurrentDistanceAndSkillBonusDetermineCoverage()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);Activate(g);var s=State(g);var u=s.Units.Single(x=>x.Id==Minion);u.Position=new Hex(-4,0);Assert.That(EffectRules.MinionDefeatProtectors(cat,s,u).Count,Is.EqualTo(1));u.Position=new Hex(-5,0);s.Players[0].RangedBonus=8;Assert.That(EffectRules.MinionDefeatProtectors(cat,s,u),Is.Empty);s.Players[0].RangeBonus=1;Assert.That(EffectRules.MinionDefeatProtectors(cat,s,u).Count,Is.EqualTo(1));Apply(g,0,CommandKind.DebugTeleport,"hero:0",cell:new Hex(5,-8));Apply(g,1,CommandKind.DebugAttack,Minion+"|0");Assert.That(g.View(0).Pending,Is.Null);}
  [Test] public void OneRoundAuraCanPreventMoreThanOneDefeatAtSeparateCosts()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);Activate(g);foreach(string cost in new[]{"brogan-13-冲拳","brogan-01-冲撞"}){Apply(g,1,CommandKind.DebugAttack,Minion+"|0");Apply(g,0,Choice,cost);}Assert.That(g.View(0).Events.Count(e=>e.Kind=="MinionDefeatPrevented"),Is.EqualTo(2));Assert.That(g.View(0).Effects.Count(e=>e.SourceCardId==Card),Is.EqualTo(1));Assert.That(g.View(1).Players[1].Gold,Is.Zero);ChargeTests.Restore(cat,g);}
  [TestCase(true)] [TestCase(false)] public void CrossfireAdditionalRemovalRequiresActualDefeat(bool protect)
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat,crossFire:true);Activate(g);string[] cards={"brogan-00-猛攻","sabina-02-交叉火力","wasp-00-闪耀之刃","arien-00-华丽刀锋"};for(int i=0;i<4;i++)Apply(g,i,CommandKind.SelectCard,cards[i]);OpportuneMomentTests.AdvanceTo(g,1);Apply(g,1,CommandKind.BeginPrimary);Apply(g,1,CommandKind.ChooseAttackTarget,Minion);Apply(g,0,Choice,protect?"brogan-13-冲拳":"skip");if(protect){Assert.That(g.View(0).Pending?.Kind,Is.Not.EqualTo("effect_minion"));Assert.That(g.View(0).Units.Any(u=>u.Id==Minion),Is.True);}else{Assert.That(g.View(1).Pending.Kind,Is.EqualTo("effect_minion"));Apply(g,1,CommandKind.ChooseEffectTarget,"minion:-1,2");}Assert.That(g.View(1).Players[1].Gold,Is.EqualTo(protect?0:2));Assert.That(g.View(0).Units.Any(u=>u.Id=="minion:-1,2"),Is.EqualTo(protect));ChargeTests.Restore(cat,g);}
  [Test] public void ActualShiningBladeHeroAttackCancelsGuardForLaterMinionAttack()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);Activate(g);string[] cards={"brogan-00-猛攻","wasp-00-闪耀之刃","sabina-01-拔枪","arien-00-华丽刀锋"};for(int i=0;i<4;i++)Apply(g,i,CommandKind.SelectCard,cards[i]);Apply(g,1,CommandKind.BeginPrimary);Apply(g,1,CommandKind.ChooseAttackTarget,"hero:0");string defense=g.View(0).DefenseOptions.First(o=>o.Assessment.Successful).CardId;Apply(g,0,CommandKind.Defend,defense);Assert.That(g.View(0).Units.Any(u=>u.Seat==0),Is.True);Assert.That(g.View(0).Effects.Any(e=>e.SourceCardId==Card),Is.False);IronWallTests.FinishTurn(g);Apply(g,1,CommandKind.DebugAttack,Minion+"|0");Assert.That(g.View(0).Pending,Is.Null);Assert.That(g.View(0).Units.Any(u=>u.Id==Minion),Is.False);ChargeTests.Restore(cat,g);}
  [Test] public void OnlyProtectionOwnerReceivesPrivatePaymentCandidates()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);Activate(g);Attack(g);Assert.That(g.View(0).MinionProtectionCards,Is.EquivalentTo(g.View(0).OwnCards.Where(c=>c.Zone==CardZone.InHand).Select(c=>c.CardId)));foreach(int? seat in new int?[]{null,1,2,3})Assert.That(g.View(seat).MinionProtectionCards,Is.Empty);ChargeTests.Restore(cat,g);}
  [Test] public void Old52SelectiveImmunityRecoveryRestoresByteForByte()
  {var cat=BattlefieldTests.Catalog();var save=System.IO.File.ReadAllText(System.IO.Path.Combine(ContentTests.Root(),"tests/fixtures/engine52-conspiracy-recovery.json"));var g=LocalGameFactory.Restore(cat,save);Assert.That(g.ExportSave(),Is.EqualTo(save));Assert.That(g.View(0).SupportedPrimaryCards,Does.Not.Contain(Card));Apply(g,1,CommandKind.ChooseRecoveredCard,ArmedConspiracyTests.Card);ChargeTests.Restore(cat,g);}

  [Test] public void SurvivingMinionStillReceivesPointBlankShotsFollowingPush()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat,crossFire:true);Activate(g);string[] cards={"brogan-00-猛攻","sabina-00-近身射击","wasp-00-闪耀之刃","arien-00-华丽刀锋"};for(int i=0;i<4;i++)Apply(g,i,CommandKind.SelectCard,cards[i]);OpportuneMomentTests.AdvanceTo(g,1);Apply(g,1,CommandKind.BeginPrimary);Apply(g,1,CommandKind.ChooseAttackTarget,Minion);Apply(g,0,Choice,"brogan-13-冲拳");Assert.That(g.View(0).Units.Single(u=>u.Id==Minion).Position,Is.EqualTo(new Hex(-4,1)));Assert.That(g.View(0).Events.Count(e=>e.Kind=="UnitPushed"),Is.EqualTo(1));Assert.That(g.View(0).Events.Count(e=>e.Kind=="AttackResolved"),Is.EqualTo(1));Assert.That(g.View(1).Players[1].Gold,Is.Zero);ChargeTests.Restore(cat,g);}
  [Test] public void ShockSpendingLastHandCardPreventsLaterProtectionOffer()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);Activate(g);foreach(string c in new[]{"brogan-01-冲撞","brogan-06-铜墙铁壁"})Apply(g,0,CommandKind.DebugDiscard,c,target:0);Attack(g,spendLast:true);Assert.That(g.View(0).Pending?.Kind,Is.Not.EqualTo("minion_protection"));Assert.That(g.View(0).Units.Any(u=>u.Id==Minion),Is.False);Assert.That(g.View(1).Players[1].Gold,Is.EqualTo(2));ChargeTests.Restore(cat,g);}

  [Test] public void DebugProtectionSpendingLastCardCompletesNonQuickPlanningWithoutDeadlock()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);Activate(g);Apply(g,0,CommandKind.SetQuickSelection,"off");foreach(string c in new[]{"brogan-00-猛攻","brogan-01-冲撞","brogan-06-铜墙铁壁"})Apply(g,0,CommandKind.DebugDiscard,c,target:0);for(int i=1;i<4;i++){Apply(g,i,CommandKind.SelectCard,g.View(i).OwnCards.First(c=>c.Zone==CardZone.InHand).CardId);Apply(g,i,CommandKind.ConfirmCard);}Assert.That(g.View(0).Phase,Is.EqualTo(Phase.Planning));Apply(g,1,CommandKind.DebugAttack,Minion+"|0");Apply(g,0,Choice,"brogan-13-冲拳");Assert.That(State(g).Players[0].Confirmed,Is.True);Assert.That(g.View(0).Phase,Is.Not.EqualTo(Phase.Planning));ChargeTests.Restore(cat,g);}
 }
}
