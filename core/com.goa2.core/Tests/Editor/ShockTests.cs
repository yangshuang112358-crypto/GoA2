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
 public sealed class ShockTests
 {
  internal const string Shock="wasp-01-电击",Riposte="tigerclaw-17-近身还击";
  internal static GameSession Setup(ContentCatalog cat,int engine=GameState.CurrentEngineVersion)
  {
   var g=LocalGameFactory.Create(cat,"shock",new[]{"A","B","C","D"},42,true,engine);Apply(g,0,CommandKind.DebugPrepare,"wasp,tigerclaw,brogan,sabina");Apply(g,0,CommandKind.DebugEquipCard,Riposte,target:1);
   foreach(var p in new[]{(0,new Hex(6,-8)),(1,new Hex(7,-8)),(2,new Hex(5,-8)),(3,new Hex(6,-9))})Apply(g,0,CommandKind.DebugTeleport,"hero:"+p.Item1,cell:p.Item2);
   string[] cards={Shock,"tigerclaw-07-伺机待发","brogan-06-铜墙铁壁","sabina-07-指挥"};for(int i=0;i<4;i++)Apply(g,i,CommandKind.SelectCard,cards[i]);Assert.That(g.View(0).ActiveSeat,Is.EqualTo(0));return g;
  }
  internal static void Target(GameSession g,string id="hero:1"){Apply(g,0,CommandKind.BeginPrimary);Apply(g,0,CommandKind.ChooseAttackTarget,id);}
  [Test] public void ExactContractAndPreviousEngineGate()
  {var c=BattlefieldTests.Catalog().Card(Shock);Assert.That(c.PrimaryFamily,Is.EqualTo("attack"));Assert.That(c.PrimaryValue,Is.EqualTo(5));Assert.That(c.Initiative,Is.EqualTo(8));Assert.That(c.SecondaryDefense,Is.EqualTo(6));Assert.That(c.SecondaryMovement,Is.EqualTo(4));Assert.That(CombatRules.HasPrimaryProgram(c),Is.True);Assert.That(CombatRules.HasPrimaryProgram(c,34),Is.False);c.Text+="并攻击额外英雄";Assert.That(CombatRules.HasPrimaryProgram(c),Is.False);}
  [Test] public void ExtraChoiceExcludesAttackTargetAlliesAndMinions()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);Target(g);Assert.That(g.View(0).EffectTargets,Is.EqualTo(new[]{"hero:3"}));Assert.That(g.View(0).Pending!.Optional,Is.True);Assert.That(g.View(0).Attack,Is.Null);ChargeTests.Restore(cat,g);}
  [TestCase(false)] [TestCase(true)] public void DiscardOrSkipReturnsToTheOriginalAttackTarget(bool skip)
  {
   var cat=BattlefieldTests.Catalog();var g=Setup(cat);Target(g);g=ChargeTests.Restore(cat,g);Apply(g,0,CommandKind.ChooseEffectTarget,skip?"skip":"hero:3");
   if(!skip){Assert.That(g.View(3).Pending!.ChooserSeat,Is.EqualTo(3));Assert.That(g.View(3).CanDeclineRetaliationDiscard,Is.False);Assert.That(new JsonStateCodec().Read(g.ExportSave()).Execution!.TargetUnitId,Is.EqualTo("hero:1"));g=ChargeTests.Restore(cat,g);Apply(g,3,CommandKind.ForcedDiscard,"sabina-01-拔枪");}
   Assert.That(g.View(1).Pending!.Kind,Is.EqualTo("defense"));Assert.That(g.View(1).Attack!.TargetUnitId,Is.EqualTo("hero:1"));Assert.That(g.View(1).Events.Count(e=>e.Kind=="AttackCalculated"),Is.EqualTo(1));ChargeTests.Restore(cat,g);
  }
  [TestCase(false)] [TestCase(true)] public void MissingExtraHeroOrEmptyHandDoesNotCancelAttack(bool empty)
  {
   var cat=BattlefieldTests.Catalog();var g=Setup(cat);if(empty)foreach(var c in g.View(3).OwnCards.Where(c=>c.Zone==CardZone.InHand).ToArray())Apply(g,0,CommandKind.DebugDiscard,c.CardId,target:3);else Apply(g,0,CommandKind.DebugTeleport,"hero:3",cell:new Hex(4,-9));Target(g);if(empty)Apply(g,0,CommandKind.ChooseEffectTarget,"hero:3");Assert.That(g.View(1).Pending!.Kind,Is.EqualTo("defense"));Assert.That(g.View(3).Players[3].AwaitingRespawn,Is.False);ChargeTests.Restore(cat,g);
  }
  [Test] public void MinionAttackTargetSurvivesTheExtraHeroSelection()
  {
   var cat=BattlefieldTests.Catalog();var g=Setup(cat);string id=g.View(0).Units.First(u=>u.Team==Team.Red && u.Kind=="melee").Id;Apply(g,0,CommandKind.DebugTeleport,id,cell:new Hex(6,-7));Target(g,id);Assert.That(g.View(0).EffectTargets,Is.EquivalentTo(new[]{"hero:1","hero:3"}));Apply(g,0,CommandKind.ChooseEffectTarget,"hero:3");Apply(g,3,CommandKind.ForcedDiscard,"sabina-01-拔枪");Assert.That(g.View(0).Units.Any(u=>u.Id==id),Is.False);Assert.That(g.View(1).Players[1].AwaitingRespawn,Is.False);Assert.That(g.View(3).Players[3].AwaitingRespawn,Is.False);ChargeTests.Restore(cat,g);
  }
  [Test] public void WrongChoicesCannotModifyEitherTargetAndExtraDiscardIsPrivate()
  {
   var cat=BattlefieldTests.Catalog();var g=Setup(cat);Target(g);string before=g.ExportSave();foreach(var c in new[]{Cmd(g,1,CommandKind.ChooseEffectTarget,"hero:3"),Cmd(g,0,CommandKind.ChooseEffectTarget,"hero:1"),Cmd(g,0,CommandKind.ChooseAttackTarget,"hero:3")}){Assert.That(g.Execute(c.ActorSeat,c).Accepted,Is.False);Assert.That(g.ExportSave(),Is.EqualTo(before));}
   Apply(g,0,CommandKind.ChooseEffectTarget,"hero:3");before=g.ExportSave();foreach(var c in new[]{Cmd(g,0,CommandKind.ForcedDiscard,"sabina-01-拔枪"),Cmd(g,3,CommandKind.ForcedDiscard,"skip"),Cmd(g,3,CommandKind.DeclineRetaliationDiscard)}){Assert.That(g.Execute(c.ActorSeat,c).Accepted,Is.False);Assert.That(g.ExportSave(),Is.EqualTo(before));}
   foreach(int? seat in new int?[]{null,0,1,2})Assert.That(g.View(seat).ForcedDiscardCards,Is.Empty);var valid=Cmd(g,3,CommandKind.ForcedDiscard,"sabina-01-拔枪");Assert.That(g.Execute(3,valid).Accepted,Is.True);before=g.ExportSave();Assert.That(g.Execute(3,valid).Duplicate,Is.True);Assert.That(g.ExportSave(),Is.EqualTo(before));Assert.That(g.View(null).Events.Any(e=>e.Kind=="CardDiscarded"),Is.False);ChargeTests.Restore(cat,g);
  }
  [Test] public void RiposteHasItsOwnLaterDiscardAfterExactlyOneAttack()
  {
   var cat=BattlefieldTests.Catalog();var g=Setup(cat);Target(g);Apply(g,0,CommandKind.ChooseEffectTarget,"hero:3");Apply(g,3,CommandKind.ForcedDiscard,"sabina-01-拔枪");Apply(g,1,CommandKind.Defend,Riposte);Assert.That(g.View(0).Pending!.ResumeAt,Is.EqualTo("defense_response"));Assert.That(g.View(0).CanDeclineRetaliationDiscard,Is.True);g=ChargeTests.Restore(cat,g);Apply(g,0,CommandKind.ForcedDiscard,"wasp-00-闪耀之刃");Assert.That(g.View(0).Events.Count(e=>e.Kind=="AttackResolved"),Is.EqualTo(1));Assert.That(g.View(0).Events.Count(e=>e.Kind=="ForcedDiscardCompleted"),Is.EqualTo(2));ChargeTests.Restore(cat,g);
  }
  [Test] public void FrozenRangeDiscardWindowIsUnchanged()
  {var cat=BattlefieldTests.Catalog();string save=File.ReadAllText(Path.Combine(ContentTests.Root(),"tests/fixtures/engine34-closesupport-discard.json"));var g=LocalGameFactory.Restore(cat,save);Assert.That(g.ExportSave(),Is.EqualTo(save));Assert.That(g.View(0).SupportedPrimaryCards,Does.Not.Contain(Shock));Apply(g,1,CommandKind.ForcedDiscard,g.View(1).ForcedDiscardCards.First());ChargeTests.Restore(cat,g);}
 }
}
