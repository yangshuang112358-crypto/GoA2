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
 public sealed class ElectricWaveTests
 {
  internal const string Wave="wasp-03-电能波";
  internal static GameSession Setup(ContentCatalog cat,Hex? extra=null)
  {
   var g=LocalGameFactory.Create(cat,"wave",new[]{"A","B","C","D"},42,true);
   Apply(g,0,CommandKind.DebugPrepare,"wasp,tigerclaw,brogan,sabina");Apply(g,0,CommandKind.DebugEquipCard,Wave,target:0);Apply(g,0,CommandKind.DebugEquipCard,ShockTests.Riposte,target:1);
   foreach(var p in new[]{(0,new Hex(6,-8)),(1,new Hex(7,-8)),(2,new Hex(5,-8)),(3,extra??new Hex(6,-6))})Apply(g,0,CommandKind.DebugTeleport,"hero:"+p.Item1,cell:p.Item2);
   string[] cards={Wave,"tigerclaw-07-伺机待发","brogan-06-铜墙铁壁","sabina-07-指挥"};for(int i=0;i<4;i++)Apply(g,i,CommandKind.SelectCard,cards[i]);Assert.That(g.View(0).ActiveSeat,Is.EqualTo(0));return g;
  }
  [Test] public void ExactContractAndEngineGate()
  {var c=BattlefieldTests.Catalog().Card(Wave);Assert.That(c.Subtype,Is.EqualTo("范围"));Assert.That(c.SubtypeValue,Is.EqualTo(2));Assert.That(c.PrimaryFamily,Is.EqualTo("attack"));Assert.That(c.PrimaryValue,Is.EqualTo(5));Assert.That(c.Initiative,Is.EqualTo(9));Assert.That(CombatRules.HasPrimaryProgram(c),Is.True);Assert.That(CombatRules.HasPrimaryProgram(c,35),Is.False);c.Text+="远程攻击";Assert.That(CombatRules.HasPrimaryProgram(c),Is.False);}
  [TestCase(1)] [TestCase(2)] [TestCase(3)] public void ExtraTargetRangeHasExactBoundary(int distance)
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat,new Hex(6,-8+distance));ShockTests.Target(g);if(distance<=2)Assert.That(g.View(0).EffectTargets,Is.EqualTo(new[]{"hero:3"}));else Assert.That(g.View(1).Pending!.Kind,Is.EqualTo("defense"));ChargeTests.Restore(cat,g);}
  [Test] public void SkillRangeBonusDoesNotTurnAttackIntoRangedOrExtendItsTargets()
  {
   var cat=BattlefieldTests.Catalog();var g=Setup(cat);Apply(g,0,CommandKind.BeginPrimary);
   var attackState=new JsonStateCodec().Read(g.ExportSave());attackState.Players[0].RangeBonus=10;attackState.Players[0].RangedBonus=10;Assert.That(CombatRules.AttackTargets(cat,attackState,0),Does.Not.Contain("hero:3").And.Contain("hero:1"));
   string before=g.ExportSave();Assert.That(g.Execute(0,Cmd(g,0,CommandKind.ChooseAttackTarget,"hero:3")).Accepted,Is.False);Assert.That(g.ExportSave(),Is.EqualTo(before));Apply(g,0,CommandKind.ChooseAttackTarget,"hero:1");
   var s=new JsonStateCodec().Read(g.ExportSave());s.Units.Single(u=>u.Id=="hero:3").Position=new Hex(6,-5);s.Players[0].RangedBonus=10;Assert.That(GameRules.LegalEffectTargets(cat,s,0),Is.Empty);s.Players[0].RangeBonus=1;Assert.That(GameRules.LegalEffectTargets(cat,s,0),Is.EqualTo(new[]{"hero:3"}));
   Apply(g,0,CommandKind.ChooseEffectTarget,"skip");Assert.That(g.View(1).Attack!.Ranged,Is.False);Assert.That(g.View(1).DefenseOptions.Select(d=>d.CardId),Does.Contain(ShockTests.Riposte));ChargeTests.Restore(cat,g);
  }
  [TestCase(false)] [TestCase(true)] public void ExtraDiscardThenOriginalDefenseRestores(bool empty)
  {
   var cat=BattlefieldTests.Catalog();var g=Setup(cat);if(empty)foreach(var c in g.View(3).OwnCards.Where(c=>c.Zone==CardZone.InHand).ToArray())Apply(g,0,CommandKind.DebugDiscard,c.CardId,target:3);
   ShockTests.Target(g);g=ChargeTests.Restore(cat,g);string before=g.ExportSave();Assert.That(g.Execute(0,Cmd(g,0,CommandKind.ChooseEffectTarget,"hero:1")).Accepted,Is.False);Assert.That(g.ExportSave(),Is.EqualTo(before));Apply(g,0,CommandKind.ChooseEffectTarget,"hero:3");
   if(!empty){g=ChargeTests.Restore(cat,g);Apply(g,3,CommandKind.ForcedDiscard,"sabina-01-拔枪");}
   Assert.That(g.View(1).Attack!.TargetUnitId,Is.EqualTo("hero:1"));Assert.That(g.View(3).Players[3].AwaitingRespawn,Is.False);ChargeTests.Restore(cat,g);
  }
  [Test] public void FrozenShockDiscardRemainsByteIdenticalAndResumesItsOriginalTarget()
  {var cat=BattlefieldTests.Catalog();string save=File.ReadAllText(Path.Combine(ContentTests.Root(),"tests/fixtures/engine35-shock-discard.json"));var g=LocalGameFactory.Restore(cat,save);Assert.That(g.ExportSave(),Is.EqualTo(save));Assert.That(g.View(0).SupportedPrimaryCards,Does.Not.Contain(Wave));Apply(g,3,CommandKind.ForcedDiscard,"sabina-01-拔枪");Assert.That(g.View(1).Attack!.TargetUnitId,Is.EqualTo("hero:1"));ChargeTests.Restore(cat,g);}
 }
}
