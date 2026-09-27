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
 public sealed class MindControlTests
 {
  internal const string Card="wasp-09-意念操控";
  internal static GameSession Ready(ContentCatalog cat,string card=Card)
  {
   var g=LocalGameFactory.Create(cat,"mind-control",new[]{"A","B","C","D"},42,true);Apply(g,0,CommandKind.DebugPrepare,"wasp,brogan,arien,shargatha");Apply(g,0,CommandKind.DebugEquipCard,card,target:0);
   var pos=new[]{new Hex(0,-5),new Hex(1,-4),new Hex(-2,-4),new Hex(6,-8)};for(int i=0;i<4;i++)Apply(g,0,CommandKind.DebugTeleport,"hero:"+i,cell:pos[i]);
   string[] cards={card,"brogan-06-铜墙铁壁","arien-01-汹涌","shargatha-00-反击"};for(int i=0;i<4;i++)Apply(g,i,CommandKind.SelectCard,cards[i]);OpportuneMomentTests.AdvanceTo(g,0);return g;
  }
  [Test] public void ExactContractAndGate()
  {var c=BattlefieldTests.Catalog().Card(Card);Assert.That(CombatRules.HasPrimaryProgram(c),Is.True);Assert.That(CombatRules.HasPrimaryProgram(c,77),Is.False);c.Text+="敌方";Assert.That(CombatRules.HasPrimaryProgram(c),Is.False);}
  [TestCase(1)] [TestCase(2)] public void CanPlaceEnemyOrFriendlyHeroOnEmptyAdjacentSpawn(int seat)
  {
   var cat=BattlefieldTests.Catalog();var g=Ready(cat);Apply(g,0,CommandKind.BeginPrimary);Assert.That(g.View(0).EffectTargets,Does.Contain("hero:"+seat));Apply(g,0,CommandKind.ChooseEffectTarget,"hero:"+seat);g=ChargeTests.Restore(cat,g);
   Assert.That(g.View(0).Placements,Does.Contain(new Hex(1,-6)));Assert.That(g.View(1).Placements,Is.Empty);Apply(g,0,CommandKind.ChoosePlacement,cell:new Hex(1,-6));
   Assert.That(g.View(0).Units.Single(u=>u.Seat==seat).Position,Is.EqualTo(new Hex(1,-6)));Assert.That(g.View(0).Events.Any(e=>e.Kind=="UnitMoved" || e.Kind=="UltimateTriggered"),Is.False);ChargeTests.Restore(cat,g);
  }
  [Test] public void StraightAxesAdjacentAndSelfAreExcluded()
  {
   var cat=BattlefieldTests.Catalog();var g=Ready(cat);Apply(g,0,CommandKind.BeginPrimary);var s=new JsonStateCodec().Read(g.ExportSave());var target=s.Units.Single(u=>u.Seat==1);
   foreach(var p in new[]{new Hex(0,-3),new Hex(2,-5),new Hex(2,-7),new Hex(0,-4)}){target.Position=p;Assert.That(GameRules.LegalEffectTargets(cat,s,0),Does.Not.Contain("hero:1"));}
   Assert.That(GameRules.LegalEffectTargets(cat,s,0),Does.Not.Contain("hero:0"));
  }
  [Test] public void RangeFourNeedsRangedBonusAndSkillRadiusDoesNotHelp()
  {
   var cat=BattlefieldTests.Catalog();var g=Ready(cat);Apply(g,0,CommandKind.BeginPrimary);var s=new JsonStateCodec().Read(g.ExportSave());s.Units.Single(u=>u.Seat==1).Position=new Hex(2,-3);s.Players[0].RangeBonus=9;
   Assert.That(GameRules.LegalEffectTargets(cat,s,0),Does.Not.Contain("hero:1"));s.Players[0].RangedBonus=1;Assert.That(GameRules.LegalEffectTargets(cat,s,0),Does.Contain("hero:1"));
  }
  [Test] public void AttackOnlyImmunityDoesNotBlockSkillButFullImmunityDoes()
  {
   var cat=BattlefieldTests.Catalog();var g=Ready(cat);Apply(g,0,CommandKind.BeginPrimary);var s=new JsonStateCodec().Read(g.ExportSave());var e=new ActiveEffect{SourceCardId="shargatha-17-至死不渝",SourceUnitId="hero:1",ControllerSeat=1,Kind=EffectKind.AttackActionImmunity,Window=EffectTimeline.Create(s.Round,s.Turn,4,EffectDuration.ThisTurn)};s.Effects.Add(e);
   Assert.That(GameRules.LegalEffectTargets(cat,s,0),Does.Contain("hero:1"));e.Kind=EffectKind.ImmunityAndUnitTraversal;Assert.That(GameRules.LegalEffectTargets(cat,s,0),Does.Not.Contain("hero:1"));e.Kind=EffectKind.FriendlyDisplacementProtection;e.AreaKind=EffectAreaKind.Adjacent;Assert.That(GameRules.LegalEffectTargets(cat,s,0),Does.Not.Contain("hero:1"));
  }
  [Test] public void PlacementIgnoresStaticMovementBoundary()
  {
   var cat=BattlefieldTests.Catalog();var g=Ready(cat);Apply(g,0,CommandKind.BeginPrimary);Apply(g,0,CommandKind.ChooseEffectTarget,"hero:1");var s=new JsonStateCodec().Read(g.ExportSave());s.Units.Single(u=>u.Seat==2).Position=new Hex(2,-4);
   s.Effects.Add(new ActiveEffect{SourceCardId="wasp-06-静电封锁",SourceUnitId="hero:2",ControllerSeat=2,Kind=EffectKind.MovementBoundary,AreaKind=EffectAreaKind.Adjacent,Window=EffectTimeline.Create(s.Round,s.Turn,4,EffectDuration.ThisTurn)});
   Assert.That(GameRules.LegalPlacements(cat,s,0),Does.Contain(new Hex(0,-6)));new GameRules().Apply(cat,s,new Command{ActorSeat=0,Kind=CommandKind.ChoosePlacement,Destination=new Hex(0,-6)});Assert.That(s.Units.Single(u=>u.Seat==1).Position,Is.EqualTo(new Hex(0,-6)));
  }
  [Test] public void RequiredChoicesRejectWrongSeatSkipOccupiedAndDistantCellsAtomically()
  {
   var cat=BattlefieldTests.Catalog();var g=Ready(cat);Apply(g,0,CommandKind.BeginPrimary);string save=g.ExportSave();
   Assert.That(g.Execute(0,Cmd(g,0,CommandKind.ChooseEffectTarget,"skip")).Accepted,Is.False);Assert.That(g.ExportSave(),Is.EqualTo(save));Apply(g,0,CommandKind.ChooseEffectTarget,"hero:1");save=g.ExportSave();
   foreach(var c in new[]{Cmd(g,1,CommandKind.ChoosePlacement,destination:new Hex(0,-6)),Cmd(g,0,CommandKind.ChoosePlacement,destination:new Hex(0,-5)),Cmd(g,0,CommandKind.ChoosePlacement,destination:new Hex(0,-3)),Cmd(g,0,CommandKind.ChoosePlacement,"skip")})
   {Assert.That(g.Execute(c.ActorSeat,c).Accepted,Is.False);Assert.That(g.ExportSave(),Is.EqualTo(save));}
   var place=Cmd(g,0,CommandKind.ChoosePlacement,destination:new Hex(0,-6));Assert.That(g.Execute(0,place).Accepted,Is.True);save=g.ExportSave();Assert.That(g.Execute(0,place).Duplicate,Is.True);Assert.That(g.ExportSave(),Is.EqualTo(save));ChargeTests.Restore(cat,g);
  }
  [Test] public void PlacedMinionReturnsBeforeCardResolves()
  {
   var cat=BattlefieldTests.Catalog();var g=Ready(cat);Apply(g,0,CommandKind.BeginPrimary);Apply(g,0,CommandKind.ChooseEffectTarget,"minion:-1,-3");Apply(g,0,CommandKind.ChoosePlacement,cell:new Hex(0,-4));
   Assert.That(g.View(1).Pending.Kind,Is.EqualTo("minion_return"));g=ChargeTests.Restore(cat,g);Apply(g,1,CommandKind.ChooseMinionReturn,"minion:-1,-3",cell:new Hex(0,-3));
   Assert.That(g.View(0).Events.Count(e=>e.Kind=="CardResolved" && e.CardId==Card),Is.EqualTo(1));ChargeTests.Restore(cat,g);
  }
  [Test] public void NoAdjacentEmptyDestinationMeansNoCompletableTarget()
  {
   var cat=BattlefieldTests.Catalog();var g=Ready(cat);Apply(g,0,CommandKind.BeginPrimary);var s=new JsonStateCodec().Read(g.ExportSave());
   var blockers=s.Units.Where(u=>u.Kind!="hero").ToArray();int i=0;foreach(var p in new Hex(0,-5).Neighbors().Where(p=>cat.Cell(p)?.Obstacle==false))blockers[i++].Position=p;
   Assert.That(GameRules.LegalEffectTargets(cat,s,0),Is.Empty);
  }
  [Test] public void Previous77TargetMoveSaveKeepsBytes()
  {
   var cat=BattlefieldTests.Catalog();string save=File.ReadAllText(Path.Combine(ContentTests.Root(),"tests/fixtures/engine77-tsunami-target-move.json"));var g=LocalGameFactory.Restore(cat,save);
   Assert.That(g.ExportSave(),Is.EqualTo(save));Assert.That(g.View(0).SupportedPrimaryCards,Does.Not.Contain(Card));
  }
 }
}
