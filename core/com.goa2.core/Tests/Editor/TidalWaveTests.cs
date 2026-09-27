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
 public sealed class TidalWaveTests
 {
  internal const string Card="arien-04-惊涛骇浪";
  internal static GameSession Ready(ContentCatalog c,bool twoPayments=false,bool defended=false)
  {
   var g=TorrentTests.Ready(c,card:Card,defended:defended);
   Apply(g,0,CommandKind.DebugTeleport,"minion:-1,-3",cell:new Hex(3,-7));
   Apply(g,0,CommandKind.DebugTeleport,"minion:4,-1",cell:new Hex(2,-8));
   if(twoPayments){Apply(g,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(3,-6));Apply(g,0,CommandKind.DebugTeleport,"minion:4,-1",cell:new Hex(4,-8));}
   return g;
  }
  [Test] public void ExactTextAndEngineGate()
  {var c=BattlefieldTests.Catalog().Card(Card);Assert.That(c.PrimaryValue,Is.EqualTo(7));Assert.That(CombatRules.HasPrimaryProgram(c),Is.True);Assert.That(CombatRules.HasPrimaryProgram(c,81),Is.False);c.Text+="三次";Assert.That(CombatRules.HasPrimaryProgram(c),Is.False);}
  [Test] public void EachAttackRecomputesItsRearPaymentAndStopsAfterSecond()
  {
   var cat=BattlefieldTests.Catalog();var g=Ready(cat,true);Apply(g,0,CommandKind.BeginPrimary);Apply(g,0,CommandKind.ChooseAttackTarget,"minion:-1,-3");Apply(g,0,CommandKind.ChooseEffectTarget,"hero:1");Apply(g,1,CommandKind.ForcedDiscard,g.View(1).ForcedDiscardCards.First());
   Assert.That(g.View(0).Pending.ResumeAt,Is.EqualTo("repeat_once_different_full"));g=ChargeTests.Restore(cat,g);
   Apply(g,0,CommandKind.ChooseAttackTarget,"minion:4,-1");Assert.That(g.View(0).EffectTargets,Is.EqualTo(new[]{"hero:3"}));Apply(g,0,CommandKind.ChooseEffectTarget,"hero:3");g=ChargeTests.Restore(cat,g);Apply(g,3,CommandKind.ForcedDiscard,g.View(3).ForcedDiscardCards.First());
   Assert.That(g.View(0).Events.Count(e=>e.Kind=="AttackResolved" && e.CardId==Card),Is.EqualTo(2));Assert.That(g.View(0).Events.Count(e=>e.Kind=="CardResolved" && e.CardId==Card),Is.EqualTo(1));Assert.That(g.View(0).Pending?.ResumeAt,Is.Not.EqualTo("repeat_once_different_full"));ChargeTests.Restore(cat,g);
  }
  [Test] public void CanRepeatWithoutAdjacentEnemyHeroAndWithoutRearHero()
  {
   var cat=BattlefieldTests.Catalog();var g=Ready(cat);Apply(g,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(4,-6));Apply(g,0,CommandKind.BeginPrimary);Apply(g,0,CommandKind.ChooseAttackTarget,"minion:-1,-3");
   Assert.That(g.View(0).AttackTargets,Does.Contain("minion:4,-1"));Apply(g,0,CommandKind.ChooseAttackTarget,"minion:4,-1");Assert.That(g.View(0).Events.Count(e=>e.Kind=="AttackResolved" && e.CardId==Card),Is.EqualTo(2));ChargeTests.Restore(cat,g);
  }
  [Test] public void SkipRepeatEndsWithoutPaymentOrAttack()
  {
   var cat=BattlefieldTests.Catalog();var g=Ready(cat);Apply(g,0,CommandKind.BeginPrimary);Apply(g,0,CommandKind.ChooseAttackTarget,"minion:-1,-3");Apply(g,0,CommandKind.ChooseAttackTarget,"skip");
   Assert.That(g.View(0).Events.Count(e=>e.Kind=="AttackResolved" && e.CardId==Card),Is.EqualTo(1));Assert.That(g.View(0).Events.Any(e=>e.Kind=="ForcedDiscardRequired"),Is.False);ChargeTests.Restore(cat,g);
  }
  [Test] public void SurvivingFirstTargetIsExcludedAndInvalidRepeatIsAtomic()
  {
   var cat=BattlefieldTests.Catalog();var g=Ready(cat,defended:true);Apply(g,0,CommandKind.DebugTeleport,"minion:1,-1",cell:new Hex(5,-9));
   TorrentTests.MainTarget(g);Apply(g,0,CommandKind.ChooseEffectTarget,"hero:3");Apply(g,3,CommandKind.ForcedDiscard,CounterattackTests.Slash);Apply(g,1,CommandKind.Defend,"sabina-08-带头冲锋");
   Assert.That(g.View(0).AttackTargets,Does.Not.Contain("hero:1"));string before=g.ExportSave();foreach(var cmd in new[]{Cmd(g,0,CommandKind.ChooseAttackTarget,"hero:1"),Cmd(g,1,CommandKind.ChooseAttackTarget,"minion:-1,-3")}){Assert.That(g.Execute(cmd.ActorSeat,cmd).Accepted,Is.False);Assert.That(g.ExportSave(),Is.EqualTo(before));}
   var repeat=Cmd(g,0,CommandKind.ChooseAttackTarget,"minion:-1,-3");Assert.That(g.Execute(0,repeat).Accepted,Is.True);before=g.ExportSave();Assert.That(g.Execute(0,repeat).Duplicate,Is.True);Assert.That(g.ExportSave(),Is.EqualTo(before));ChargeTests.Restore(cat,g);
  }
  [Test] public void FiveCellRearLimitIgnoresRangeBonuses()
  {
   var cat=BattlefieldTests.Catalog();var g=Ready(cat);TorrentTests.MainTarget(g);var s=new JsonStateCodec().Read(g.ExportSave());s.Players[0].RangeBonus=9;s.Players[0].RangedBonus=9;
   s.Units.Single(u=>u.Seat==3).Position=new Hex(9,-8);Assert.That(GameRules.LegalEffectTargets(cat,s,0),Is.EqualTo(new[]{"hero:3"}));s.Units.Single(u=>u.Seat==3).Position=new Hex(10,-8);Assert.That(GameRules.LegalEffectTargets(cat,s,0),Is.Empty);
  }
  [Test] public void CounterattackResolvesBeforeOfferingTheNextCompleteAttack()
  {
   var cat=BattlefieldTests.Catalog();var g=TorrentTests.Ready(cat,true,Card);Apply(g,0,CommandKind.DebugTeleport,"minion:-1,-3",cell:new Hex(3,-7));TorrentTests.MainTarget(g);Apply(g,0,CommandKind.ChooseEffectTarget,"hero:3");Apply(g,3,CommandKind.ForcedDiscard,CounterattackTests.Slash);
   Assert.That(g.View(1).Pending.Kind,Is.EqualTo("defense"));Apply(g,1,CommandKind.DeclineDefense);Assert.That(g.View(3).Pending.Kind,Is.EqualTo("discard_attack"));g=ChargeTests.Restore(cat,g);
   Apply(g,3,CommandKind.ChooseDiscardAttack,CounterattackTests.Slash);Apply(g,3,CommandKind.ChooseAttackTarget,"hero:2");Apply(g,2,CommandKind.DeclineDefense);
   Assert.That(g.View(0).Pending.ResumeAt,Is.EqualTo("repeat_once_different_full"));g=ChargeTests.Restore(cat,g);Apply(g,0,CommandKind.ChooseAttackTarget,"minion:-1,-3");Assert.That(g.View(0).Events.Count(e=>e.Kind=="CardResolved" && e.CardId==Card),Is.EqualTo(1));ChargeTests.Restore(cat,g);
  }
  [Test] public void VictoryDuringSecondPaymentPreventsItsMainAttack()
  {
   var cat=BattlefieldTests.Catalog();var g=Ready(cat,true);Apply(g,0,CommandKind.DebugSetCrystal,"1",target:3);Apply(g,0,CommandKind.BeginPrimary);Apply(g,0,CommandKind.ChooseAttackTarget,"minion:-1,-3");Apply(g,0,CommandKind.ChooseEffectTarget,"hero:1");Apply(g,1,CommandKind.ForcedDiscard,g.View(1).ForcedDiscardCards.First());
   Apply(g,0,CommandKind.ChooseAttackTarget,"minion:4,-1");Apply(g,0,CommandKind.ChooseEffectTarget,"hero:3");Apply(g,3,CommandKind.DeclineRetaliationDiscard);
   Assert.That(g.View(0).Phase,Is.EqualTo(Phase.Finished));Assert.That(g.View(0).Units.Any(u=>u.Id=="minion:4,-1"),Is.True);Assert.That(g.View(0).Events.Count(e=>e.Kind=="AttackResolved" && e.CardId==Card),Is.EqualTo(1));ChargeTests.Restore(cat,g);
  }
  [Test] public void NoSecondTargetFinishesWithoutAPendingRepeat()
  {
   var cat=BattlefieldTests.Catalog();var g=TorrentTests.Ready(cat,card:Card);TorrentTests.MainTarget(g);Apply(g,0,CommandKind.ChooseEffectTarget,"hero:3");Apply(g,3,CommandKind.ForcedDiscard,CounterattackTests.Slash);Apply(g,1,CommandKind.DeclineDefense);
   Assert.That(g.View(0).Pending?.ResumeAt,Is.Not.EqualTo("repeat_once_different_full"));Assert.That(g.View(0).Events.Count(e=>e.Kind=="CardResolved" && e.CardId==Card),Is.EqualTo(1));ChargeTests.Restore(cat,g);
  }
  [Test] public void Old81PaymentRestoresExactBytes()
  {var cat=BattlefieldTests.Catalog();string save=File.ReadAllText(Path.Combine(ContentTests.Root(),"tests/fixtures/engine81-torrent-payment.json"));var g=LocalGameFactory.Restore(cat,save);Assert.That(g.ExportSave(),Is.EqualTo(save));Assert.That(g.View(0).SupportedPrimaryCards,Does.Not.Contain(Card));}
 }
}
