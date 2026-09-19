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
 public sealed class SideBySideTests
 {
  internal const string Card="sabina-06-并肩作战";
  internal static GameSession Setup(ContentCatalog cat,bool bard=false)
  {
   var g=LocalGameFactory.Create(cat,"side-by-side",new[]{"A","B","C","D"},42,true);Apply(g,0,CommandKind.DebugPrepare,bard?"sabina,wasp,brogan,arien":"sabina,wasp,arien,brogan");if(bard)Apply(g,0,CommandKind.DebugEquipCard,"brogan-10-吟游诗人",target:2);
   Apply(g,0,CommandKind.DebugTeleport,"hero:0",cell:new Hex(-2,0));Apply(g,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(-2,1));Apply(g,0,CommandKind.DebugTeleport,"hero:2",cell:new Hex(-3,2));Apply(g,0,CommandKind.DebugTeleport,"hero:3",cell:new Hex(6,-9));
   string[] cards={Card,"wasp-00-闪耀之刃",bard?"brogan-10-吟游诗人":"arien-07-潮水",bard?"arien-07-潮水":"brogan-06-铜墙铁壁"};for(int i=0;i<4;i++)Apply(g,i,CommandKind.SelectCard,cards[i]);Assert.That(g.View(0).ActiveSeat,Is.EqualTo(0));return g;
  }
  internal static void Activate(GameSession g,string target="skip"){Apply(g,0,CommandKind.BeginPrimary);Apply(g,0,CommandKind.ChooseEffectTarget,target);}
  private static GameState State(GameSession g)=>new JsonStateCodec().Read(g.ExportSave());
  // Isolated defense-query matrix; the effect itself is created by a real skill action.
  private static int Bonus(ContentCatalog cat,GameState original,int seat)
  {
   var codec=new JsonStateCodec();var state=codec.Read(codec.Write(original));int attacker=seat%2==0?1:0;string card=attacker==1?"wasp-00-闪耀之刃":"sabina-00-近身射击";
   state.Execution=new CardExecution{CardId=card,ControllerSeat=attacker,Attack=CombatMath.Attack(state,cat.Card(card),attacker,"hero:"+seat)};state.Pending=new PendingChoice{Kind="defense",ChooserSeat=seat};state.Phase=Phase.EffectChoice;
   return CombatRules.DefenseOptions(cat,state,seat).First(d=>!d.Block).Assessment.DefenseBonus-state.Players[seat].DefenseBonus;
  }
  [Test] public void ContractAndEngineGate()
  {var c=BattlefieldTests.Catalog().Card(Card);Assert.That(c.PrimaryCategory,Is.EqualTo("基础技能"));Assert.That(c.Initiative,Is.EqualTo(13));Assert.That(c.SubtypeValue,Is.EqualTo(3));Assert.That(c.SecondaryMovement,Is.Null);Assert.That(c.SecondaryDefense,Is.EqualTo(2));Assert.That(CombatRules.HasPrimaryProgram(c),Is.True);Assert.That(CombatRules.HasPrimaryProgram(c,46),Is.False);c.Text+="此轮";Assert.That(CombatRules.HasPrimaryProgram(c),Is.False);}
  [TestCase("skip")] [TestCase(CommandMinionTests.Melee)] public void OptionalSwapOrSkipBothCreateAuraAndPreserveJournal(string target)
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);Apply(g,0,CommandKind.BeginPrimary);g=ChargeTests.Restore(cat,g);Assert.That(g.View(0).Pending!.Optional,Is.True);var command=Cmd(g,0,CommandKind.ChooseEffectTarget,target);Assert.That(g.Execute(0,command).Accepted,Is.True);string save=g.ExportSave();Assert.That(g.Execute(0,command).Duplicate,Is.True);Assert.That(g.ExportSave(),Is.EqualTo(save));g=ChargeTests.Restore(cat,g);Assert.That(g.View(0).Effects.Count(e=>e.SourceCardId==Card),Is.EqualTo(1));Assert.That(g.View(0).Events.Count(e=>e.Kind=="UnitsSwapped"),Is.EqualTo(target=="skip"?0:1));Assert.That(g.View(0).Events.Any(e=>e.Kind=="UnitMoved"),Is.False);Assert.That(Bonus(cat,State(g),0),Is.EqualTo(1));}
  [Test] public void NoMinionTargetStillCreatesConditionalAura()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);Apply(g,0,CommandKind.DebugTeleport,"hero:0",cell:new Hex(6,-8));Apply(g,0,CommandKind.BeginPrimary);g=ChargeTests.Restore(cat,g);var state=State(g);Assert.That(state.Execution,Is.Null);Assert.That(state.Pending,Is.Null);Assert.That(state.Effects.Count(e=>e.SourceCardId==Card),Is.EqualTo(1));Assert.That(Bonus(cat,state,0),Is.Zero);}
  [Test] public void SwapTargetsStayAdjacentFriendlyUnprotectedMinionsRegardlessOfRangeBonus()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);Apply(g,0,CommandKind.DebugTeleport,CommandMinionTests.Heavy,cell:new Hex(-1,0));Apply(g,0,CommandKind.BeginPrimary);string before=g.ExportSave();foreach(var c in new[]{Cmd(g,0,CommandKind.ChooseEffectTarget,"hero:0"),Cmd(g,0,CommandKind.ChooseEffectTarget,"hero:1"),Cmd(g,0,CommandKind.ChooseEffectTarget,CommandMinionTests.Heavy),Cmd(g,0,CommandKind.ChooseEffectTarget,CommandMinionTests.Ranged),Cmd(g,2,CommandKind.ChooseEffectTarget,CommandMinionTests.Melee)}){Assert.That(g.Execute(c.ActorSeat,c).Accepted,Is.False);Assert.That(g.ExportSave(),Is.EqualTo(before));}var state=State(g);state.Players[0].RangeBonus=10;state.Players[0].RangedBonus=10;Assert.That(GameRules.LegalEffectTargets(cat,state,0),Does.Not.Contain(CommandMinionTests.Ranged));Assert.That(g.View(2).EffectTargets,Is.Empty);Apply(g,0,CommandKind.ChooseEffectTarget,"skip");ChargeTests.Restore(cat,g);}
  [Test] public void SelfAndAllyGainOnlyOneBonusWhileEnemyAndUnaccompaniedHeroDoNot()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);Activate(g);var state=State(g);Assert.That(Bonus(cat,state,0),Is.EqualTo(1));Assert.That(Bonus(cat,state,2),Is.EqualTo(1));Assert.That(Bonus(cat,state,1),Is.Zero);state.Units.RemoveAll(u=>u.Team==Team.Blue && u.Kind!="hero");Assert.That(Bonus(cat,state,0),Is.Zero);Assert.That(Bonus(cat,state,2),Is.Zero);}
  [TestCase("melee")] [TestCase("ranged")] [TestCase("heavy")] public void AnyFriendlyMinionKindSatisfiesPresenceCondition(string kind)
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);Activate(g);var state=State(g);state.Units.RemoveAll(u=>u.Team==Team.Blue && u.Kind!="hero" && u.Id!=CommandMinionTests.Melee);state.Units.Single(u=>u.Id==CommandMinionTests.Melee).Kind=kind;if(kind=="heavy"){state.Units.Add(new UnitState{Id="protecting-minion",Kind="melee",Team=Team.Blue,Position=new Hex(6,-8)});Assert.That(GameRules.LegalMinionRemovals(state),Does.Not.Contain(CommandMinionTests.Melee));}Assert.That(Bonus(cat,state,0),Is.EqualTo(1));}
  [Test] public void AuraUsesSkillRangeAndCurrentPositionsRatherThanRangedBonus()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);Activate(g);var state=State(g);var source=state.Units.Single(u=>u.Seat==0);var ally=state.Units.Single(u=>u.Seat==2);ally.Position=cat.Cells.First(c=>!c.Obstacle && c.Position.Distance(source.Position)==4 && state.Units.All(u=>u.Position!=c.Position)).Position;var minion=state.Units.Single(u=>u.Id==CommandMinionTests.Melee);minion.Position=ally.Position.Neighbors().First(p=>cat.Cell(p)?.Obstacle==false && state.Units.All(u=>u.Position!=p));state.Players[0].RangedBonus=5;Assert.That(Bonus(cat,state,2),Is.Zero);state.Players[0].RangeBonus=1;Assert.That(Bonus(cat,state,2),Is.EqualTo(1));source.Position=cat.Cells.First(c=>!c.Obstacle && c.Position.Distance(ally.Position)>4 && state.Units.All(u=>u.Position!=c.Position)).Position;Assert.That(Bonus(cat,state,2),Is.Zero);}
  [Test] public void ChallengerGetsAuraBonusBeforeActualShiningBladeCancelsIt()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);Activate(g);Apply(g,1,CommandKind.BeginPrimary);Apply(g,1,CommandKind.ChooseAttackTarget,"hero:2");g=ChargeTests.Restore(cat,g);var defense=g.View(2).DefenseOptions.Single(o=>o.CardId=="arien-13-挑战者");Assert.That(defense.IgnoresMinions,Is.True);Assert.That(defense.Assessment.DefenseBonus,Is.EqualTo(1));Apply(g,2,CommandKind.Defend,"arien-13-挑战者");g=ChargeTests.Restore(cat,g);Assert.That(g.View(0).Effects.Any(e=>e.SourceCardId==Card),Is.False);Assert.That(g.View(0).Events.Any(e=>e.Kind=="EffectCancelled" && e.CardId==Card),Is.True);}
  [TestCase(false)] [TestCase(true)] public void TurnEndOrSourceDefeatRemovesAura(bool defeat)
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);Activate(g);if(defeat)Apply(g,0,CommandKind.DebugDefeatHero,"hero:0",target:1);else{Apply(g,1,CommandKind.Pass);Apply(g,3,CommandKind.Pass);Apply(g,2,CommandKind.Pass);}ChargeTests.Restore(cat,g);Assert.That(g.View(0).Effects.Any(e=>e.SourceCardId==Card),Is.False);}
  [Test] public void SwapOutOfBattleZonePreservesAuraWhileCaptainReturnsMinion()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);Apply(g,0,CommandKind.DebugTeleport,FlashingBladeTests.Minion,cell:new Hex(8,-9));Apply(g,0,CommandKind.DebugTeleport,"hero:0",cell:new Hex(-1,-4));Apply(g,0,CommandKind.DebugTeleport,CommandMinionTests.Melee,cell:new Hex(-1,-3));Activate(g,CommandMinionTests.Melee);g=ChargeTests.Restore(cat,g);Assert.That(g.View(0).Pending!.Kind,Is.EqualTo("minion_return"));Assert.That(g.View(0).Effects.Any(e=>e.SourceCardId==Card),Is.True);Apply(g,0,CommandKind.ChooseMinionReturn,CommandMinionTests.Melee,cell:new Hex(-2,-3));g=ChargeTests.Restore(cat,g);Assert.That(Bonus(cat,State(g),0),Is.EqualTo(1));}
  [Test] public void Engine46EnemyReturnRemainsByteStableAndCompletes()
  {var cat=BattlefieldTests.Catalog();string save=File.ReadAllText(Path.Combine(ContentTests.Root(),"tests/fixtures/engine46-swap-return.json"));var g=LocalGameFactory.Restore(cat,save);Assert.That(g.ExportSave(),Is.EqualTo(save));Assert.That(g.View(0).SupportedPrimaryCards,Does.Not.Contain(Card));Apply(g,1,CommandKind.ChooseMinionReturn,FlashingBladeTests.Minion,cell:new Hex(-2,-3));ChargeTests.Restore(cat,g);}
  [Test] public void ActualBardRetrievalCancelsAuraBeforeCardCanBeReused()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat,true);Activate(g);Apply(g,1,CommandKind.Pass);if(g.View(2).ActiveSeat==3)Apply(g,3,CommandKind.Pass);Assert.That(g.View(2).ActiveSeat,Is.EqualTo(2));Apply(g,2,CommandKind.BeginPrimary);Apply(g,2,CommandKind.ChooseEffectTarget,"hero:0");g=ChargeTests.Restore(cat,g);Assert.That(g.View(0).RecoverableCards,Does.Contain(Card));Apply(g,0,CommandKind.ChooseRecoveredCard,Card);g=ChargeTests.Restore(cat,g);Assert.That(g.View(0).Effects.Any(e=>e.SourceCardId==Card),Is.False);Assert.That(g.View(0).OwnCards.Single(c=>c.CardId==Card).Zone,Is.EqualTo(CardZone.InHand));}
 }
}
