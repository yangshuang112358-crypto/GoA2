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
 public sealed class PikeTests
 {
  internal const string Pike="shargatha-04-横枪跃马";
  internal static GameSession Setup(ContentCatalog cat,bool adjacent=true,bool sidestep=false)
  {
   var g=LocalGameFactory.Create(cat,"pike",new[]{"A","B","C","D"},42,true);
   Apply(g,0,CommandKind.DebugPrepare,"shargatha,tigerclaw,brogan,wasp");Apply(g,0,CommandKind.DebugEquipCard,Pike,target:0);Apply(g,0,CommandKind.DebugEquipCard,sidestep?SidestepTests.Step:ThunderBoomerangTests.Dodge,target:1);
   Apply(g,0,CommandKind.DebugTeleport,"hero:0",cell:new Hex(6,-8));Apply(g,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(8,-9));Apply(g,0,CommandKind.DebugTeleport,"hero:2",cell:new Hex(5,-8));Apply(g,0,CommandKind.DebugTeleport,"hero:3",cell:new Hex(6,adjacent?-9:-5));Apply(g,0,CommandKind.DebugTeleport,"minion:-1,-3",cell:new Hex(6,-6));
   var cards=new[]{Pike,"tigerclaw-07-伺机待发","brogan-06-铜墙铁壁","wasp-07-抵挡屏障"};for(int s=0;s<4;s++)Apply(g,s,CommandKind.SelectCard,cards[s]);Assert.That(g.View(0).ActiveSeat,Is.EqualTo(0));return g;
  }
  private static void Attack(GameSession g){Apply(g,0,CommandKind.BeginPrimary);Apply(g,0,CommandKind.ChooseAttackTarget,"hero:1");}
  [Test] public void ContractAndVersionGate()
  {var c=BattlefieldTests.Catalog().Card(Pike);Assert.That(c.Initiative,Is.EqualTo(8));Assert.That(c.PrimaryValue,Is.EqualTo(5));Assert.That(c.SubtypeValue,Is.EqualTo(2));Assert.That(c.SecondaryMovement,Is.EqualTo(4));Assert.That(c.SecondaryDefense,Is.EqualTo(4));Assert.That(CombatRules.HasPrimaryProgram(c),Is.True);Assert.That(CombatRules.HasPrimaryProgram(c,39),Is.False);c.Text+="重复两次";Assert.That(CombatRules.HasPrimaryProgram(c),Is.False);}
  [TestCase(false)] [TestCase(true)] public void FirstDefenseOrDefeatAllowsExactlyOneDifferentAttack(bool defend)
  {
   var cat=BattlefieldTests.Catalog();var g=Setup(cat);Attack(g);g=ChargeTests.Restore(cat,g);Apply(g,1,defend?CommandKind.Defend:CommandKind.DeclineDefense,defend?ThunderBoomerangTests.Dodge:"");g=ChargeTests.Restore(cat,g);
   Assert.That(g.View(0).Pending!.ResumeAt,Is.EqualTo("repeat_once_different"));Assert.That(g.View(0).Attack,Is.Null);Assert.That(g.View(0).AttackTargets,Does.Not.Contain("hero:1"));Assert.That(g.View(0).AttackTargets,Does.Not.Contain("hero:3"));Assert.That(g.View(1).AttackTargets,Is.Empty);
   var cmd=Cmd(g,0,CommandKind.ChooseAttackTarget,"minion:-1,-3");Assert.That(g.Execute(0,cmd).Accepted,Is.True);string after=g.ExportSave();Assert.That(g.Execute(0,cmd).Duplicate,Is.True);Assert.That(g.ExportSave(),Is.EqualTo(after));g=ChargeTests.Restore(cat,g);
   Assert.That(g.View(0).ActiveSeat,Is.EqualTo(2));Assert.That(g.View(0).Pending,Is.Null);Assert.That(g.View(0).Events.Count(e=>e.Kind=="AttackDeclared"),Is.EqualTo(2));Assert.That(g.View(0).Events.Count(e=>e.Kind=="AttackResolved"),Is.EqualTo(2));Assert.That(g.View(0).Events.Count(e=>e.Kind=="AttackRepeated"),Is.EqualTo(1));Assert.That(g.View(0).Events.Count(e=>e.Kind=="CardResolved" && e.CardId==Pike),Is.EqualTo(1));
  }
  [Test] public void NoAdjacentEnemyEndsEvenAfterSuccessfulAttack()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat,false);Attack(g);Apply(g,1,CommandKind.DeclineDefense);ChargeTests.Restore(cat,g);Assert.That(g.View(0).Pending,Is.Null);Assert.That(g.View(0).ActiveSeat,Is.EqualTo(2));}
  [Test] public void DefenseMovementCompletesBeforeAdjacencyIsRechecked()
  {
   var cat=BattlefieldTests.Catalog();var g=Setup(cat,false,true);Attack(g);Apply(g,1,CommandKind.Defend,SidestepTests.Step);g=ChargeTests.Restore(cat,g);Assert.That(g.View(1).Pending!.ResumeAt,Is.EqualTo("defense_response_move"));Assert.That(g.View(0).AttackTargets,Is.Empty);
   Apply(g,1,CommandKind.ChooseEffectMove,cell:new Hex(6,-7));g=ChargeTests.Restore(cat,g);Assert.That(g.View(0).Pending!.ResumeAt,Is.EqualTo("repeat_once_different"));Assert.That(g.View(0).AttackTargets,Does.Contain("minion:-1,-3"));Apply(g,0,CommandKind.ChooseAttackTarget,"skip");ChargeTests.Restore(cat,g);Assert.That(g.View(0).Pending,Is.Null);Assert.That(g.View(0).Events.Count(e=>e.Kind=="AttackDeclared"),Is.EqualTo(1));
  }
  [Test] public void InitialSkipAndWrongActorOrPreviousTargetAreAtomic()
  {
   var cat=BattlefieldTests.Catalog();var g=Setup(cat);Apply(g,0,CommandKind.BeginPrimary);string before=g.ExportSave();Assert.That(g.Execute(0,Cmd(g,0,CommandKind.ChooseAttackTarget,"skip")).Accepted,Is.False);Assert.That(g.ExportSave(),Is.EqualTo(before));Apply(g,0,CommandKind.ChooseAttackTarget,"hero:1");Apply(g,1,CommandKind.Defend,ThunderBoomerangTests.Dodge);before=g.ExportSave();
   foreach(var cmd in new[]{Cmd(g,1,CommandKind.ChooseAttackTarget,"skip"),Cmd(g,0,CommandKind.ChooseAttackTarget,"hero:1"),Cmd(g,0,CommandKind.ChooseAttackTarget,"hero:2"),Cmd(g,0,CommandKind.ChooseAttackTarget,"hero:3")}){Assert.That(g.Execute(cmd.ActorSeat,cmd).Accepted,Is.False);Assert.That(g.ExportSave(),Is.EqualTo(before));}Apply(g,0,CommandKind.ChooseAttackTarget,"skip");Assert.That(g.View(0).ActiveSeat,Is.EqualTo(2));
  }
  [TestCase(1,0,0,false)] [TestCase(2,0,0,true)] [TestCase(3,0,1,false)] [TestCase(3,1,0,true)] public void RangedBonusAndMinimumDistanceApply(int distance,int ranged,int range,bool legal)
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);var state=new JsonStateCodec().Read(g.ExportSave());state.Units.Single(u=>u.Seat==1).Position=new Hex(6,-8+distance);state.Players[0].RangedBonus=ranged;state.Players[0].RangeBonus=range;Assert.That(CombatRules.AttackTargets(cat,state,0).Contains("hero:1"),Is.EqualTo(legal));}
  [Test] public void PriorGoldFixtureRemainsByteStable()
  {var cat=BattlefieldTests.Catalog();string save=File.ReadAllText(Path.Combine(ContentTests.Root(),"tests/fixtures/engine39-master-gold.json"));var g=LocalGameFactory.Restore(cat,save);Assert.That(g.ExportSave(),Is.EqualTo(save));Assert.That(g.View(0).SupportedPrimaryCards,Does.Not.Contain(Pike));Apply(g,0,CommandKind.ChooseGoldTransfer,"3",target:1);ChargeTests.Restore(cat,g);}
  [Test] public void MinionFirstThenHeroDefenseFinishesWithoutThirdAttack()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);Apply(g,0,CommandKind.BeginPrimary);Apply(g,0,CommandKind.ChooseAttackTarget,"minion:-1,-3");g=ChargeTests.Restore(cat,g);Assert.That(g.View(0).AttackTargets,Does.Contain("hero:1"));Apply(g,0,CommandKind.ChooseAttackTarget,"hero:1");g=ChargeTests.Restore(cat,g);Apply(g,1,CommandKind.Defend,ThunderBoomerangTests.Dodge);ChargeTests.Restore(cat,g);Assert.That(g.View(0).Pending,Is.Null);Assert.That(g.View(0).Events.Count(e=>e.Kind=="AttackDeclared"),Is.EqualTo(2));}
  [Test] public void NoDifferentTargetEndsWithoutEmptyChoice()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);Apply(g,0,CommandKind.DebugTeleport,"minion:-1,-3",cell:new Hex(-1,-3));Attack(g);Apply(g,1,CommandKind.Defend,ThunderBoomerangTests.Dodge);ChargeTests.Restore(cat,g);Assert.That(g.View(0).Pending,Is.Null);Assert.That(g.View(0).ActiveSeat,Is.EqualTo(2));}
  [Test] public void VictoryStopsBeforeRepeat()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);Apply(g,0,CommandKind.DebugSetCrystal,"1",target:1);Attack(g);Apply(g,1,CommandKind.DeclineDefense);ChargeTests.Restore(cat,g);Assert.That(g.View(0).Phase,Is.EqualTo(Phase.Finished));Assert.That(g.View(0).Pending,Is.Null);Assert.That(g.View(0).Events.Any(e=>e.Kind=="AttackRepeatChoiceRequired"),Is.False);}
 }
}
