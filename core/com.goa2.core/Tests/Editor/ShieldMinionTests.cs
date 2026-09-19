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
 public sealed class ShieldMinionTests
 {
  internal const string Card="brogan-09-持盾", Ranged="minion:-1,2";
  internal static CommandKind Choice=>(CommandKind)Enum.Parse(typeof(CommandKind),"ChooseMinionProtection");
  internal static GameSession Setup(ContentCatalog cat)
  {var g=LocalGameFactory.Create(cat,"shield-minion",new[]{"A","B","C","D"},42,true);Apply(g,0,CommandKind.DebugPrepare,"brogan,wasp,sabina,arien");Apply(g,0,CommandKind.DebugEquipCard,Card,target:0);Apply(g,0,CommandKind.DebugTeleport,"hero:0",cell:new Hex(-2,0));Apply(g,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(-2,1));Apply(g,0,CommandKind.DebugTeleport,"hero:2",cell:new Hex(6,-9));Apply(g,0,CommandKind.DebugTeleport,"hero:3",cell:new Hex(7,-9));Apply(g,0,CommandKind.DebugTeleport,Ranged,cell:new Hex(-3,2));string[] cards={Card,"wasp-06-静电封锁","sabina-06-并肩作战","arien-07-潮水"};for(int i=0;i<4;i++)Apply(g,i,CommandKind.SelectCard,cards[i]);Apply(g,2,CommandKind.Pass);Apply(g,1,CommandKind.Pass);Assert.That(g.View(0).ActiveSeat,Is.EqualTo(0));return g;}
  internal static void Activate(GameSession g)=>GuardMinionTests.Activate(g);
  [Test] public void ContractAndVersionGate()
  {var c=BattlefieldTests.Catalog().Card(Card);Assert.That(c.SubtypeValue,Is.EqualTo(2));Assert.That(CombatRules.HasPrimaryProgram(c),Is.True);Assert.That(CombatRules.HasPrimaryProgram(c,53),Is.False);c.Text+="其他";Assert.That(CombatRules.HasPrimaryProgram(c),Is.False);}
  [TestCase(true)] [TestCase(false)] public void RangedMinionCanBeSavedOrDeliberatelyDefeated(bool save)
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);Activate(g);Apply(g,1,CommandKind.DebugAttack,Ranged+"|0");Assert.That(g.View(0).Pending.Kind,Is.EqualTo("minion_protection"));ChargeTests.Restore(cat,g);Apply(g,0,Choice,save?"brogan-13-冲拳":"skip");Assert.That(g.View(0).Units.Any(u=>u.Id==Ranged),Is.EqualTo(save));Assert.That(g.View(1).Players[1].Gold,Is.EqualTo(save?0:2));Assert.That(g.View(0).Effects.Any(e=>e.SourceCardId==Card),Is.True);ChargeTests.Restore(cat,g);}
  [TestCase("melee",Team.Blue,true)] [TestCase("ranged",Team.Blue,true)] [TestCase("heavy",Team.Blue,false)] [TestCase("ranged",Team.Red,false)] [TestCase("hero",Team.Blue,false)] [TestCase("marker",Team.Blue,false)]
  public void NonHeavyMeansOnlyFriendlyMeleeOrRangedMinion(string kind,Team team,bool allowed)
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);Activate(g);var s=GuardMinionTests.State(g);var u=s.Units.Single(x=>x.Id==Ranged);u.Kind=kind;u.Team=team;Assert.That(EffectRules.MinionDefeatProtectors(cat,s,u).Any(),Is.EqualTo(allowed));}
  [Test] public void SameRoundCanProtectMeleeAndRangedAtSeparateCosts()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);Activate(g);Apply(g,1,CommandKind.DebugAttack,Ranged+"|0");Apply(g,0,Choice,"brogan-13-冲拳");Apply(g,1,CommandKind.DebugAttack,GuardMinionTests.Minion+"|0");Apply(g,0,Choice,"brogan-01-冲撞");Assert.That(g.View(0).Events.Count(e=>e.Kind=="MinionDefeatPrevented"),Is.EqualTo(2));Assert.That(g.View(1).Players[1].Gold,Is.Zero);ChargeTests.Restore(cat,g);}
  [Test] public void ActualShockUsesTheSameSerializableProtectionWindowForRangedMinion()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);Activate(g);string[] cards={"brogan-00-猛攻","wasp-01-电击","sabina-01-拔枪","arien-00-华丽刀锋"};for(int i=0;i<4;i++)Apply(g,i,CommandKind.SelectCard,cards[i]);OpportuneMomentTests.AdvanceTo(g,1);Apply(g,1,CommandKind.BeginPrimary);Apply(g,1,CommandKind.ChooseAttackTarget,Ranged);Apply(g,1,CommandKind.ChooseEffectTarget,"skip");ChargeTests.Restore(cat,g);Apply(g,0,Choice,"brogan-13-冲拳");Assert.That(g.View(0).Events.Count(e=>e.Kind=="AttackResolved"),Is.EqualTo(1));Assert.That(g.View(0).Units.Any(u=>u.Id==Ranged),Is.True);ChargeTests.Restore(cat,g);}
  [Test] public void EmptyHandDoesNotOpenAnUnpayableRangedProtectionChoice()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);Activate(g);foreach(var c in g.View(0).OwnCards.Where(c=>c.Zone==CardZone.InHand).ToList())Apply(g,0,CommandKind.DebugDiscard,c.CardId,target:0);Apply(g,1,CommandKind.DebugAttack,Ranged+"|0");Assert.That(g.View(0).Pending,Is.Null);Assert.That(g.View(0).Units.Any(u=>u.Id==Ranged),Is.False);}
  [Test] public void ReequipmentRemovesShieldSourceRatherThanStackingWithGuard()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);Activate(g);Apply(g,0,CommandKind.DebugEquipCard,GuardMinionTests.Card,target:0);Assert.That(g.View(0).Effects.Any(e=>e.SourceCardId==Card),Is.False);Apply(g,1,CommandKind.DebugAttack,Ranged+"|0");Assert.That(g.View(0).Pending,Is.Null);}
  [Test] public void Old53PendingMeleeProtectionStillRestoresExactly()
  {var cat=BattlefieldTests.Catalog();var save=System.IO.File.ReadAllText(System.IO.Path.Combine(ContentTests.Root(),"tests/fixtures/engine53-guard-payment.json"));var g=LocalGameFactory.Restore(cat,save);Assert.That(g.ExportSave(),Is.EqualTo(save));Assert.That(g.View(0).SupportedPrimaryCards,Does.Not.Contain(Card));Apply(g,0,Choice,"brogan-13-冲拳");Assert.That(g.View(0).Units.Any(u=>u.Id==GuardMinionTests.Minion),Is.True);ChargeTests.Restore(cat,g);}

  [Test] public void SolitaryHeavyMinionIsNotProtectedAndStillAdvancesTheFrontline()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);Activate(g);string heavy=g.View(0).Units.Single(u=>u.Kind=="heavy" && u.Team==Team.Blue).Id;foreach(var u in g.View(0).Units.Where(u=>u.Team==Team.Blue && (u.Kind=="melee" || u.Kind=="ranged")).ToList())Apply(g,0,CommandKind.DebugRemoveMinion,u.Id);Apply(g,0,CommandKind.DebugTeleport,heavy,cell:new Hex(-3,1));Apply(g,1,CommandKind.DebugAttack,heavy+"|0");Assert.That(g.View(0).Pending?.Kind,Is.Not.EqualTo("minion_protection"));Assert.That(g.View(1).Players[1].Gold,Is.EqualTo(4));Assert.That(g.View(0).RedMarks,Is.EqualTo(1));Assert.That(g.View(0).Events.Count(e=>e.Kind=="FrontlineAdvanced"),Is.EqualTo(1));ChargeTests.Restore(cat,g);}
 }
}
