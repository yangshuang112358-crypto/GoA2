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
 public sealed class ArcaneSwapTests
 {
  internal const string Card="arien-08-奥术换位";
  internal static GameSession Setup(ContentCatalog cat,bool boundary=false,bool suppression=false)
  {
   var g=LocalGameFactory.Create(cat,"arcane-swap",new[]{"A","B","C","D"},42,true);Apply(g,0,CommandKind.DebugPrepare,"arien,wasp,brogan,sabina");Apply(g,0,CommandKind.DebugEquipCard,Card,target:0);
   Apply(g,0,CommandKind.DebugTeleport,"hero:0",cell:new Hex(-2,0));Apply(g,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(-2,-1));Apply(g,0,CommandKind.DebugTeleport,"hero:2",cell:new Hex(-3,2));Apply(g,0,CommandKind.DebugTeleport,"hero:3",cell:new Hex(6,-9));
   string[] cards={Card,suppression?"wasp-00-闪耀之刃":"wasp-06-静电封锁","brogan-06-铜墙铁壁","sabina-07-指挥"};for(int i=0;i<4;i++)Apply(g,i,CommandKind.SelectCard,cards[i]);if(suppression){Apply(g,1,CommandKind.BeginPrimary);Apply(g,1,CommandKind.ChooseAttackTarget,"hero:0");Apply(g,0,CommandKind.Defend,"arien-13-挑战者");}else Apply(g,1,boundary?CommandKind.BeginPrimary:CommandKind.Pass);Apply(g,2,CommandKind.Pass);if(g.View(0).ActiveSeat==3)Apply(g,3,CommandKind.Pass);Assert.That(g.View(0).ActiveSeat,Is.EqualTo(0));return g;
  }
  [Test] public void ContractAndEngineGate()
  {var c=BattlefieldTests.Catalog().Card(Card);Assert.That(c.Initiative,Is.EqualTo(4));Assert.That(c.Subtype,Is.EqualTo("远程"));Assert.That(c.SubtypeValue,Is.EqualTo(3));Assert.That(c.SecondaryDefense,Is.EqualTo(3));Assert.That(CombatRules.HasPrimaryProgram(c),Is.True);Assert.That(CombatRules.HasPrimaryProgram(c,45),Is.False);c.Text+="全部";Assert.That(CombatRules.HasPrimaryProgram(c),Is.False);}
  [TestCase("hero:2")] [TestCase("minion:-3,1")] [TestCase("minion:-1,1")] public void FriendlyHeroOrEitherTeamMinionSwapsAtomicallyWithoutMovement(string id)
  {
   var cat=BattlefieldTests.Catalog();var g=Setup(cat);var from=g.View(0).Units.Single(u=>u.Seat==0).Position;var to=g.View(0).Units.Single(u=>u.Id==id).Position;Apply(g,0,CommandKind.BeginPrimary);g=ChargeTests.Restore(cat,g);Assert.That(g.View(0).EffectTargets,Does.Contain(id));var cmd=Cmd(g,0,CommandKind.ChooseEffectTarget,id);Assert.That(g.Execute(0,cmd).Accepted,Is.True);string save=g.ExportSave();Assert.That(g.Execute(0,cmd).Duplicate,Is.True);Assert.That(g.ExportSave(),Is.EqualTo(save));g=ChargeTests.Restore(cat,g);Assert.That(g.View(0).Units.Single(u=>u.Seat==0).Position,Is.EqualTo(to));Assert.That(g.View(0).Units.Single(u=>u.Id==id).Position,Is.EqualTo(from));Assert.That(g.View(0).Events.Any(e=>e.Kind=="UnitMoved"),Is.False);var swapped=g.View(0).Events.Single(e=>e.Kind=="UnitsSwapped");Assert.That(swapped.From,Is.EqualTo(from));Assert.That(swapped.To,Is.EqualTo(to));Assert.That(swapped.Path,Is.Empty);Assert.That(g.View(0).Players.All(p=>p.Gold==0),Is.True);
  }
  [Test] public void SelfEnemyHeroProtectedHeavyAndWrongSeatCannotSwap()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);Apply(g,0,CommandKind.DebugTeleport,CommandMinionTests.Heavy,cell:new Hex(-1,0));Apply(g,0,CommandKind.BeginPrimary);string before=g.ExportSave();foreach(var cmd in new[]{Cmd(g,0,CommandKind.ChooseEffectTarget,"hero:0"),Cmd(g,0,CommandKind.ChooseEffectTarget,"hero:1"),Cmd(g,0,CommandKind.ChooseEffectTarget,CommandMinionTests.Heavy),Cmd(g,2,CommandKind.ChooseEffectTarget,"hero:2"),Cmd(g,0,CommandKind.ChooseEffectTarget,"skip")}){Assert.That(g.Execute(cmd.ActorSeat,cmd).Accepted,Is.False);Assert.That(g.ExportSave(),Is.EqualTo(before));}Assert.That(g.View(2).EffectTargets,Is.Empty);Assert.That(g.View(null).EffectTargets,Is.Empty);}
  [Test] public void SwapCrossesActualStaticBoundaryBecauseItIsNotMovement()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat,true);Apply(g,0,CommandKind.BeginPrimary);Apply(g,0,CommandKind.ChooseEffectTarget,"hero:2");ChargeTests.Restore(cat,g);Assert.That(g.View(0).Units.Single(u=>u.Seat==0).Position,Is.EqualTo(new Hex(-3,2)));Assert.That(g.View(0).Events.Any(e=>e.Kind=="UnitMoved"),Is.False);}
  [Test] public void RangeUsesRangedBonusAndUnprotectedHeavyBecomesLegal()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);Apply(g,0,CommandKind.BeginPrimary);var state=new JsonStateCodec().Read(g.ExportSave());var source=state.Units.Single(u=>u.Seat==0);var target=state.Units.Single(u=>u.Id==CommandMinionTests.Heavy);target.Position=cat.Cells.First(c=>!c.Obstacle && c.Position.Distance(source.Position)==4 && state.Units.All(u=>u.Position!=c.Position)).Position;state.Units.RemoveAll(u=>u.Team==Team.Blue && u.Kind!="hero" && u.Id!=target.Id);state.Players[0].RangeBonus=10;Assert.That(GameRules.LegalEffectTargets(cat,state,0),Does.Not.Contain(target.Id));state.Players[0].RangedBonus=1;Assert.That(GameRules.LegalEffectTargets(cat,state,0),Does.Contain(target.Id));}
  [Test] public void EnemyMinionSwappedOutsideReturnsUnderItsOwnCaptainAfterSwap()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat,true);Apply(g,0,CommandKind.DebugTeleport,"hero:0",cell:new Hex(-1,-4));Apply(g,0,CommandKind.BeginPrimary);Apply(g,0,CommandKind.ChooseEffectTarget,FlashingBladeTests.Minion);g=ChargeTests.Restore(cat,g);Assert.That(g.View(1).Pending!.Kind,Is.EqualTo("minion_return"));Assert.That(g.View(1).Pending!.ChooserSeat,Is.EqualTo(1));Assert.That(g.View(0).Units.Single(u=>u.Seat==0).Position,Is.EqualTo(new Hex(-1,-3)));Assert.That(g.View(0).MinionReturns,Is.Empty);string before=g.ExportSave();Assert.That(g.Execute(0,Cmd(g,0,CommandKind.ChooseMinionReturn,FlashingBladeTests.Minion,destination:new Hex(-2,-3))).Accepted,Is.False);Assert.That(g.ExportSave(),Is.EqualTo(before));Apply(g,1,CommandKind.ChooseMinionReturn,FlashingBladeTests.Minion,cell:new Hex(-2,-3));g=ChargeTests.Restore(cat,g);Assert.That(g.View(0).Pending,Is.Null);Assert.That(g.View(0).Events.Count(e=>e.Kind=="UnitsSwapped"),Is.EqualTo(1));Assert.That(g.View(0).Events.Count(e=>e.Kind=="CardResolved" && e.CardId==Card),Is.EqualTo(1));Assert.That(g.View(0).Players.All(p=>p.Gold==0),Is.True);}
  [Test] public void NoLegalSwapTargetEndsWithoutMovingSource()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);var state=new JsonStateCodec().Read(g.ExportSave());var origin=state.Units.Single(u=>u.Seat==0).Position;state.Units.RemoveAll(u=>u.Kind!="hero" || u.Seat==2);new GameRules().Apply(cat,state,new Command{ActorSeat=0,Kind=CommandKind.BeginPrimary});Assert.That(state.Pending,Is.Null);Assert.That(state.Execution,Is.Null);Assert.That(state.Units.Single(u=>u.Seat==0).Position,Is.EqualTo(origin));Assert.That(state.Events.Any(e=>e.Kind=="UnitsSwapped"),Is.False);}
  [Test] public void SkillSuppressionStillBlocksSwapAndAttackOnlyImmunityDoesNot()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat,false,true);string before=g.ExportSave();Assert.That(g.Execute(0,Cmd(g,0,CommandKind.BeginPrimary)).Accepted,Is.False);Assert.That(g.ExportSave(),Is.EqualTo(before));ChargeTests.Restore(cat,g);g=Setup(cat);
   Apply(g,0,CommandKind.BeginPrimary);var state=new JsonStateCodec().Read(g.ExportSave());state.Effects.Add(new ActiveEffect{Id="swap-attack-immunity",Kind=EffectKind.NonAdjacentRangedImmunity,SourceCardId="wasp-10-反射屏障",SourceUnitId="hero:2",ProtectedUnitId="hero:2",ControllerSeat=2,AreaKind=EffectAreaKind.None,Window=EffectTimeline.Create(1,1,4,EffectDuration.ThisTurn)!});Assert.That(GameRules.LegalEffectTargets(cat,state,0),Does.Contain("hero:2"));}
  [Test] public void PreviousRepeatWindowRemainsByteStableAndSkipsNormally()
  {var cat=BattlefieldTests.Catalog();string save=File.ReadAllText(Path.Combine(ContentTests.Root(),"tests/fixtures/engine45-advantage-repeat.json"));var g=LocalGameFactory.Restore(cat,save);Assert.That(g.ExportSave(),Is.EqualTo(save));Assert.That(g.View(0).SupportedPrimaryCards,Does.Not.Contain(Card));Apply(g,0,CommandKind.ChooseEffectTarget,"skip");ChargeTests.Restore(cat,g);Assert.That(g.View(0).Pending,Is.Null);}
 }
}
