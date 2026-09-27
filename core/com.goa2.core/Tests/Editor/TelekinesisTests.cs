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
 public sealed class TelekinesisTests
 {
  internal const string Card="wasp-13-控物";
  internal static GameSession Ready(ContentCatalog c,string card=Card)=>MindControlTests.Ready(c,card);
  [Test] public void ExactContractAndGate()
  {var c=BattlefieldTests.Catalog().Card(Card);Assert.That(c.SubtypeValue,Is.EqualTo(2));Assert.That(CombatRules.HasPrimaryProgram(c),Is.True);Assert.That(CombatRules.HasPrimaryProgram(c,82),Is.False);c.Text+="自己";Assert.That(CombatRules.HasPrimaryProgram(c),Is.False);}
  [TestCase(1,2,-5)] [TestCase(2,-2,-3)] public void BothTeamsCanMoveExactlyOneCellAroundSource(int seat,int x,int y)
  {
   var cat=BattlefieldTests.Catalog();var g=Ready(cat);Apply(g,0,CommandKind.BeginPrimary);Apply(g,0,CommandKind.ChooseEffectTarget,"hero:"+seat);g=ChargeTests.Restore(cat,g);
   var origin=g.View(0).Units.Single(u=>u.Seat==seat).Position;Assert.That(g.View(0).EffectMoves.All(m=>m.Path.Count==2 && m.Destination.Distance(new Hex(0,-5))==2),Is.True);
   Apply(g,0,CommandKind.ChooseEffectMove,cell:new Hex(x,y));Assert.That(g.View(0).Units.Single(u=>u.Seat==seat).Position,Is.EqualTo(new Hex(x,y)));Assert.That(g.View(0).Events.Count(e=>e.Kind=="UnitMoved" && e.CardId==Card),Is.EqualTo(1));ChargeTests.Restore(cat,g);
  }
  [Test] public void SelfAndAdjacentTargetsExcludedButStraightAxisAllowed()
  {
   var cat=BattlefieldTests.Catalog();var g=Ready(cat);Apply(g,0,CommandKind.BeginPrimary);var s=new JsonStateCodec().Read(g.ExportSave());Assert.That(GameRules.LegalEffectTargets(cat,s,0),Does.Not.Contain("hero:0"));
   s.Units.Single(u=>u.Seat==1).Position=new Hex(0,-4);Assert.That(GameRules.LegalEffectTargets(cat,s,0),Does.Not.Contain("hero:1"));s.Units.Single(u=>u.Seat==1).Position=new Hex(0,-3);Assert.That(GameRules.LegalEffectTargets(cat,s,0),Does.Contain("hero:1"));
  }
  [Test] public void WrongSeatInwardOutwardAndSelfAreAtomicAndDuplicateDoesNotMoveTwice()
  {
   var cat=BattlefieldTests.Catalog();var g=Ready(cat);Apply(g,0,CommandKind.BeginPrimary);string before=g.ExportSave();foreach(var c in new[]{Cmd(g,1,CommandKind.ChooseEffectTarget,"hero:1"),Cmd(g,0,CommandKind.ChooseEffectTarget,"hero:0"),Cmd(g,0,CommandKind.ChooseEffectTarget,"skip")}){Assert.That(g.Execute(c.ActorSeat,c).Accepted,Is.False);Assert.That(g.ExportSave(),Is.EqualTo(before));}
   Apply(g,0,CommandKind.ChooseEffectTarget,"hero:1");Assert.That(g.View(1).EffectMoves,Is.Empty);before=g.ExportSave();foreach(var p in new[]{new Hex(0,-4),new Hex(2,-4)}){Assert.That(g.Execute(0,Cmd(g,0,CommandKind.ChooseEffectMove,destination:p)).Accepted,Is.False);Assert.That(g.ExportSave(),Is.EqualTo(before));}
   var move=Cmd(g,0,CommandKind.ChooseEffectMove,destination:new Hex(2,-5));Assert.That(g.Execute(0,move).Accepted,Is.True);before=g.ExportSave();Assert.That(g.Execute(0,move).Duplicate,Is.True);Assert.That(g.ExportSave(),Is.EqualTo(before));ChargeTests.Restore(cat,g);
  }
  [Test] public void ZeroStepsStillCompletesSelectedAction()
  {
   var cat=BattlefieldTests.Catalog();var g=Ready(cat);Apply(g,0,CommandKind.BeginPrimary);Apply(g,0,CommandKind.ChooseEffectTarget,"hero:1");Apply(g,0,CommandKind.ChooseEffectMove,"skip");Assert.That(g.View(0).Units.Single(u=>u.Seat==1).Position,Is.EqualTo(new Hex(1,-4)));Assert.That(g.View(0).Events.Any(e=>e.Kind=="UnitMoved"),Is.False);Assert.That(g.View(0).Events.Count(e=>e.Kind=="CardResolved" && e.CardId==Card),Is.EqualTo(1));ChargeTests.Restore(cat,g);
  }
  [Test] public void SkillRangeBonusOnlyAndTextMoveDoesNotGainMovementBonus()
  {
   var cat=BattlefieldTests.Catalog();var g=Ready(cat);Apply(g,0,CommandKind.BeginPrimary);var s=new JsonStateCodec().Read(g.ExportSave());s.Units.Single(u=>u.Seat==1).Position=new Hex(2,-4);s.Players[0].RangedBonus=9;Assert.That(GameRules.LegalEffectTargets(cat,s,0),Does.Not.Contain("hero:1"));s.Players[0].RangeBonus=1;Assert.That(GameRules.LegalEffectTargets(cat,s,0),Does.Contain("hero:1"));
   g=new GameSession(cat,new JsonStateCodec(),s);Apply(g,0,CommandKind.ChooseEffectTarget,"hero:1");s=new JsonStateCodec().Read(g.ExportSave());s.Players[0].MovementBonus=9;s.Players[1].MovementBonus=9;Assert.That(GameRules.LegalEffectMoves(cat,s,0).All(m=>m.Path.Count==2),Is.True);
  }
  [Test] public void FullImmunityProtectionAndHeavyImmunityExcludeTargets()
  {
   var cat=BattlefieldTests.Catalog();var g=Ready(cat);Apply(g,0,CommandKind.BeginPrimary);var s=new JsonStateCodec().Read(g.ExportSave());s.Units.Single(u=>u.Id=="minion:-3,0").Position=new Hex(2,-5);Assert.That(GameRules.LegalEffectTargets(cat,s,0),Does.Not.Contain("minion:-3,0"));
   var e=new ActiveEffect{SourceUnitId="hero:1",SourceCardId="brogan-06-铜墙铁壁",ControllerSeat=1,Kind=EffectKind.FriendlyDisplacementProtection,Window=EffectTimeline.Create(s.Round,s.Turn,4,EffectDuration.ThisRound)};s.Effects.Add(e);Assert.That(GameRules.LegalEffectTargets(cat,s,0),Does.Not.Contain("hero:1"));e.Kind=EffectKind.ImmunityAndUnitTraversal;Assert.That(GameRules.LegalEffectTargets(cat,s,0),Does.Not.Contain("hero:1"));e.Kind=EffectKind.AttackActionImmunity;Assert.That(GameRules.LegalEffectTargets(cat,s,0),Does.Contain("hero:1"));
  }
  [Test] public void StaticBoundaryAndOccupiedCellsStillRestrictTheOrbit()
  {
   var cat=BattlefieldTests.Catalog();var g=Ready(cat);Apply(g,0,CommandKind.DebugTeleport,"hero:2",cell:new Hex(0,-3));Apply(g,0,CommandKind.BeginPrimary);Apply(g,0,CommandKind.ChooseEffectTarget,"hero:1");var s=new JsonStateCodec().Read(g.ExportSave());Assert.That(GameRules.LegalEffectMoves(cat,s,0).Any(m=>m.Destination==new Hex(2,-5)),Is.True);
   s.Effects.Add(new ActiveEffect{SourceUnitId="hero:2",SourceCardId="wasp-06-静电封锁",ControllerSeat=2,Kind=EffectKind.MovementBoundary,AreaKind=EffectAreaKind.Adjacent,Window=EffectTimeline.Create(s.Round,s.Turn,4,EffectDuration.ThisTurn)});Assert.That(GameRules.LegalEffectMoves(cat,s,0).Any(m=>m.Destination==new Hex(2,-5)),Is.False);
  }
  [Test] public void BlockedOrbitCompletesWithZeroMovement()
  {
   var cat=BattlefieldTests.Catalog();var g=Ready(cat);Apply(g,0,CommandKind.BeginPrimary);var s=new JsonStateCodec().Read(g.ExportSave());var pos=s.Units.Single(u=>u.Seat==1).Position;foreach(var p in pos.Neighbors().Where(p=>cat.Cell(p)?.Obstacle==false && !s.Units.Any(u=>u.Position==p)))s.Units.Add(new UnitState{Id="block:"+p,Kind="melee",Team=Team.Blue,Position=p});g=new GameSession(cat,new JsonStateCodec(),s);Apply(g,0,CommandKind.ChooseEffectTarget,"hero:1");Assert.That(g.View(0).Events.Count(e=>e.Kind=="CardResolved" && e.CardId==Card),Is.EqualTo(1));Assert.That(g.View(0).Pending?.Kind,Is.Not.EqualTo("effect_move"));
  }
  [Test] public void EnemyMinionReturnsBeforeCardResolves()
  {
   var cat=BattlefieldTests.Catalog();var g=Ready(cat);Apply(g,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(2,-5));Apply(g,0,CommandKind.DebugTeleport,"minion:-1,-3",cell:new Hex(0,-3));Apply(g,0,CommandKind.BeginPrimary);Apply(g,0,CommandKind.ChooseEffectTarget,"minion:-1,-3");Apply(g,0,CommandKind.ChooseEffectMove,cell:new Hex(1,-4));
   Assert.That(g.View(1).Pending.Kind,Is.EqualTo("minion_return"));Assert.That(g.View(0).Events.Any(e=>e.Kind=="CardResolved" && e.CardId==Card),Is.False);g=ChargeTests.Restore(cat,g);Apply(g,1,CommandKind.ChooseMinionReturn,"minion:-1,-3",cell:new Hex(0,-3));
   Assert.That(g.View(0).Events.Count(e=>e.Kind=="CardResolved" && e.CardId==Card),Is.EqualTo(1));ChargeTests.Restore(cat,g);
  }
  [Test] public void Old82RepeatSaveKeepsExactBytes()
  {var cat=BattlefieldTests.Catalog();string save=File.ReadAllText(Path.Combine(ContentTests.Root(),"tests/fixtures/engine82-tidal-wave-repeat.json"));var g=LocalGameFactory.Restore(cat,save);Assert.That(g.ExportSave(),Is.EqualTo(save));Assert.That(g.View(0).SupportedPrimaryCards,Does.Not.Contain(Card));}
 }
}
