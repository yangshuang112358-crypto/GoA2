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
 public sealed class CharmTests
 {
  internal const string Card="shargatha-07-魅惑",Melee="minion:-1,-3",Ranged="minion:1,-1",Heavy="minion:-3,0";
  internal static GameSession Ready(ContentCatalog cat,string card=Card)
  {
   var g=LocalGameFactory.Create(cat,"charm",new[]{"A","B","C","D"},42,true);Apply(g,0,CommandKind.DebugPrepare,"shargatha,wasp,brogan,sabina");Apply(g,0,CommandKind.DebugEquipCard,card,target:0);Apply(g,0,CommandKind.DebugEquipCard,"brogan-10-吟游诗人",target:2);
   var pos=new[]{new Hex(3,-8),new Hex(3,-7),new Hex(5,-8),new Hex(6,-8)};for(int i=0;i<4;i++)Apply(g,0,CommandKind.DebugTeleport,"hero:"+i,cell:pos[i]);Apply(g,0,CommandKind.DebugTeleport,Melee,cell:new Hex(4,-8));
   var cards=new[]{card,"wasp-07-抵挡屏障","brogan-06-铜墙铁壁","sabina-07-指挥"};for(int i=0;i<4;i++)Apply(g,i,CommandKind.SelectCard,cards[i]);OpportuneMomentTests.AdvanceTo(g,0);return g;
  }
  internal static void Activate(GameSession g){Apply(g,0,CommandKind.BeginPrimary);Apply(g,0,CommandKind.ChooseEffectMove,"skip");}
  internal static GameSession Planning(ContentCatalog cat,string card=Card){var g=Ready(cat,card);Activate(g);Apply(g,0,CommandKind.DebugAdvance,"turn");return g;}
  internal static AttackBreakdown DebugMath(ContentCatalog cat,GameState state,int defender=0)
  {new GameRules().Apply(cat,state,new Command{ActorSeat=1,Kind=CommandKind.DebugAttack,Value="hero:"+defender+"|5"});return state.Execution.Attack;}
  [Test] public void ExactContractAndGate()
  {var c=BattlefieldTests.Catalog().Card(Card);Assert.That(c.PrimaryValue,Is.EqualTo(2));Assert.That(c.PrimaryFamily,Is.EqualTo("movement"));Assert.That(CombatRules.HasPrimaryProgram(c),Is.True);Assert.That(CombatRules.HasPrimaryProgram(c,85),Is.False);c.Text+="队友";Assert.That(CombatRules.HasPrimaryProgram(c),Is.False);}
  [TestCase(true)] [TestCase(false)] public void EffectBeginsOnlyAfterPrimaryMovement(bool stay)
  {
   var cat=BattlefieldTests.Catalog();var g=Ready(cat);Apply(g,0,CommandKind.BeginPrimary);Assert.That(g.View(0).Effects,Is.Empty);g=ChargeTests.Restore(cat,g);Apply(g,0,CommandKind.ChooseEffectMove,stay?"skip":"",cell:new Hex(3,-9));Assert.That(g.View(0).Effects.Single().SourceCardId,Is.EqualTo(Card));Assert.That(g.View(0).Effects.Single().Duration,Is.EqualTo(EffectDuration.ThisRound));ChargeTests.Restore(cat,g);
  }
  [TestCase(0)] [TestCase(2)] public void DefenseOverrideIsOnlyForCasterAndNeverChangesRealTeam(int defender)
  {
   var cat=BattlefieldTests.Catalog();var g=Planning(cat);var s=new JsonStateCodec().Read(g.ExportSave());var a=DebugMath(cat,s,defender);
   Assert.That(a.FriendlyGuardSources.Contains(Melee),Is.EqualTo(defender==0));Assert.That(a.EnemySupportSources.Contains(Melee),Is.EqualTo(defender!=0));Assert.That(s.Units.Single(u=>u.Id==Melee).Team,Is.EqualTo(Team.Red));Assert.That(a.FinalAttack,Is.EqualTo(defender==0?4:6));
  }
  [Test] public void ActualAttackUsesTheSameDefenseViewAsDebugAttack()
  {
   var cat=BattlefieldTests.Catalog();var g=Planning(cat);var cards=new[]{CounterattackTests.Card,"wasp-00-闪耀之刃","brogan-00-猛攻","sabina-00-近身射击"};for(int i=0;i<4;i++)Apply(g,i,CommandKind.SelectCard,cards[i]);OpportuneMomentTests.AdvanceTo(g,1);Apply(g,1,CommandKind.BeginPrimary);Apply(g,1,CommandKind.ChooseAttackTarget,"hero:0");Assert.That(g.View(0).Attack.FriendlyGuardSources,Does.Contain(Melee));Assert.That(g.View(0).Attack.EnemySupportSources,Does.Not.Contain(Melee));g=ChargeTests.Restore(cat,g);
   Apply(g,0,CommandKind.Defend,CounterattackTests.Slash);ChargeTests.Restore(cat,g);
  }
  [Test] public void RangedAndProtectedHeavyRemainEnemies()
  {
   var cat=BattlefieldTests.Catalog();var g=Planning(cat);Apply(g,0,CommandKind.DebugTeleport,Ranged,cell:new Hex(2,-8));Apply(g,0,CommandKind.DebugTeleport,Heavy,cell:new Hex(4,-9));Apply(g,1,CommandKind.DebugAttack,"hero:0|5");
   Assert.That(g.View(0).Attack.EnemySupportSources,Is.EquivalentTo(new[]{Ranged,Heavy}));Assert.That(g.View(0).Attack.FriendlyGuardSources,Is.EqualTo(new[]{Melee}));ChargeTests.Restore(cat,g);
  }
  [TestCase(false)] [TestCase(true)] public void CombatTypeConversionIsAppliedBeforeDefenseAllegiance(bool dual)
  {
   var cat=BattlefieldTests.Catalog();var g=Planning(cat);var s=new JsonStateCodec().Read(g.ExportSave());s.Units.Single(u=>u.Seat==3).Position=new Hex(5,-9);
   s.Effects.Add(new ActiveEffect{SourceUnitId="hero:3",SourceCardId=dual?"sabina-16-战斗武装":"sabina-14-战斗演练",ControllerSeat=3,Kind=dual?EffectKind.FriendlyAttackMinionsDual:EffectKind.FriendlyAttackMinionsRanged,Duration=EffectDuration.ThisRound,Window=EffectTimeline.Create(s.Round,s.Turn,4,EffectDuration.ThisRound)});
   var a=DebugMath(cat,s);Assert.That(a.FriendlyGuardSources.Contains(Melee),Is.EqualTo(dual));Assert.That(a.EnemySupportSources.Count(id=>id==Melee),Is.EqualTo(dual?0:1));Assert.That(s.Units.Single(u=>u.Id==Melee).Kind,Is.EqualTo("melee"));Assert.That(s.Units.Single(u=>u.Id==Melee).Team,Is.EqualTo(Team.Red));
  }
  [Test] public void IgnoringAllMinionModifiersIgnoresCharmedGuardAsWell()
  {
   var cat=BattlefieldTests.Catalog();var g=Planning(cat);var s=new JsonStateCodec().Read(g.ExportSave());var a=DebugMath(cat,s);var d=CombatMath.Defense(a,4,0,ignoreMinions:true);Assert.That(d.AttackCompared,Is.EqualTo(5));Assert.That(d.Successful,Is.False);
  }
  [Test] public void SourceDefeatAndRoundEndCancelTheAura()
  {
   var cat=BattlefieldTests.Catalog();var g=Ready(cat);Activate(g);Apply(g,0,CommandKind.DebugDefeatHero,"hero:0",target:1);Assert.That(g.View(0).Effects.Any(e=>e.SourceCardId==Card),Is.False);ChargeTests.Restore(cat,g);
   g=Ready(cat);Activate(g);for(int turn=1;turn<=4;turn++){if(turn>1)Apply(g,0,CommandKind.DebugSelectAll,"first");Apply(g,0,CommandKind.DebugAdvance,"turn");}Assert.That(g.View(0).Effects.Any(e=>e.SourceCardId==Card),Is.False);ChargeTests.Restore(cat,g);
  }
  [Test] public void RetrievingSourceCardCancelsDefenseOverride()
  {
   var cat=BattlefieldTests.Catalog();var g=Planning(cat);Apply(g,0,CommandKind.DebugTeleport,"hero:2",cell:new Hex(4,-9));var cards=new[]{CounterattackTests.Card,"wasp-00-闪耀之刃","brogan-10-吟游诗人","sabina-00-近身射击"};for(int i=0;i<4;i++)Apply(g,i,CommandKind.SelectCard,cards[i]);OpportuneMomentTests.AdvanceTo(g,2);Apply(g,2,CommandKind.BeginPrimary);Apply(g,2,CommandKind.ChooseEffectTarget,"hero:0");Apply(g,0,CommandKind.ChooseRecoveredCard,Card);Assert.That(g.View(0).Effects.Any(e=>e.SourceCardId==Card),Is.False);ChargeTests.Restore(cat,g);
  }
  [Test] public void FastReplacementDoesNotCreateAura()
  {
   var cat=BattlefieldTests.Catalog();var g=Ready(cat);Apply(g,0,CommandKind.DebugTeleport,"hero:0",cell:g.View(0).DebugTeleports["hero:0"].First(h=>cat.Cell(h).Region=="blueFountain"));Apply(g,0,CommandKind.Move,cell:g.View(0).FastMoves.First().Destination,mode:MoveMode.Fast);Assert.That(g.View(0).Effects.Any(e=>e.SourceCardId==Card),Is.False);ChargeTests.Restore(cat,g);
  }
  [Test] public void WrongChooserAndRetryDoNotCreateDuplicateEffects()
  {
   var cat=BattlefieldTests.Catalog();var g=Ready(cat);Apply(g,0,CommandKind.BeginPrimary);string before=g.ExportSave();Assert.That(g.Execute(1,Cmd(g,1,CommandKind.ChooseEffectMove,"skip")).Accepted,Is.False);Assert.That(g.ExportSave(),Is.EqualTo(before));var move=Cmd(g,0,CommandKind.ChooseEffectMove,"skip");Assert.That(g.Execute(0,move).Accepted,Is.True);before=g.ExportSave();Assert.That(g.Execute(0,move).Duplicate,Is.True);Assert.That(g.ExportSave(),Is.EqualTo(before));Assert.That(g.View(0).Effects.Count(e=>e.SourceCardId==Card),Is.EqualTo(1));
  }
  [Test] public void Old85RepeatRetainsExactBytes()
  {var cat=BattlefieldTests.Catalog();string save=File.ReadAllText(Path.Combine(ContentTests.Root(),"tests/fixtures/engine85-psychic-vortex-last-repeat.json"));var g=LocalGameFactory.Restore(cat,save);Assert.That(g.ExportSave(),Is.EqualTo(save));Assert.That(g.View(0).SupportedPrimaryCards,Does.Not.Contain(Card));}
 }
}
