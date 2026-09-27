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
 public sealed class BindingTests
 {
  internal const string Card="shargatha-09-致命束缚";
  internal static GameSession Cast(ContentCatalog cat,string card=Card)
  {
   var g=LocalGameFactory.Create(cat,"binding",new[]{"A","B","C","D"},42,true);Apply(g,0,CommandKind.DebugPrepare,"shargatha,tigerclaw,arien,wasp");Apply(g,0,CommandKind.DebugEquipCard,card,target:0);var pos=new[]{new Hex(3,-8),new Hex(4,-8),new Hex(6,-8),new Hex(6,-9)};for(int i=0;i<4;i++)Apply(g,0,CommandKind.DebugTeleport,"hero:"+i,cell:pos[i]);var cards=new[]{card,"tigerclaw-07-伺机待发","arien-13-挑战者","wasp-07-抵挡屏障"};for(int i=0;i<4;i++)Apply(g,i,CommandKind.SelectCard,cards[i]);OpportuneMomentTests.AdvanceTo(g,0);Apply(g,0,CommandKind.BeginPrimary);Apply(g,0,CommandKind.ChooseEffectMove,"skip");return g;
  }
  internal static GameSession Next(ContentCatalog cat,string played="tigerclaw-08-偷天妙手",string card=Card)
  {var g=Cast(cat,card);Apply(g,0,CommandKind.DebugAdvance,"turn");Apply(g,0,CommandKind.DebugEquipCard,played,target:1);var cards=new[]{"shargatha-00-反击",played,"arien-07-潮水","wasp-13-控物"};for(int i=0;i<4;i++)Apply(g,i,CommandKind.SelectCard,cards[i]);OpportuneMomentTests.AdvanceTo(g,1);return g;}
  [Test] public void ExactTextAndGate(){var c=BattlefieldTests.Catalog().Card(Card);Assert.That(CombatRules.HasPrimaryProgram(c),Is.True);Assert.That(CombatRules.HasPrimaryProgram(c,92),Is.False);c.Text+="蓝色";Assert.That(CombatRules.HasPrimaryProgram(c),Is.False);}
  [TestCase("tigerclaw-00-瞬闪打击",true)] [TestCase("tigerclaw-02-偷袭",true)] [TestCase("tigerclaw-08-偷天妙手",false)] [TestCase("tigerclaw-18-躲闪",false)] public void OnlyGoldOrRedSecondaryMovementIsLegal(string played,bool allowed)
  {var cat=BattlefieldTests.Catalog();var g=Next(cat,played);Assert.That(g.View(1).SecondaryMoves.Count>0,Is.EqualTo(allowed));if(!allowed){var before=g.ExportSave();Assert.That(g.Execute(1,Cmd(g,1,CommandKind.Move,destination:new Hex(4,-9))).Accepted,Is.False);Assert.That(g.ExportSave(),Is.EqualTo(before));}ChargeTests.Restore(cat,g);}
  [Test] public void MovingSourceChangesAdjacencyAtEachNewMovement()
  {var cat=BattlefieldTests.Catalog();var g=Next(cat);Assert.That(g.View(1).SecondaryMoves,Is.Empty);Apply(g,0,CommandKind.DebugTeleport,"hero:0",cell:new Hex(2,-8));Assert.That(g.View(1).SecondaryMoves,Is.Not.Empty);Apply(g,0,CommandKind.DebugTeleport,"hero:0",cell:new Hex(3,-8));Assert.That(g.View(1).SecondaryMoves,Is.Empty);}
  [Test] public void InternalGreenMovementIsPreventedButGoldRequiredMovementStillWorks()
  {var cat=BattlefieldTests.Catalog();var g=Next(cat);Apply(g,1,CommandKind.BeginPrimary);Assert.That(g.View(1).EffectMoves,Is.Empty);Assert.That(g.View(1).Events.Any(e=>e.Kind=="UnitMoved" && e.Seat==1),Is.False);ChargeTests.Restore(cat,g);g=Next(cat,"tigerclaw-00-瞬闪打击");Apply(g,1,CommandKind.BeginPrimary);Assert.That(g.View(1).EffectMoves.Any(m=>m.Destination==new Hex(2,-8)),Is.True);}
  [Test] public void ImmunityExemptsTargetAndSourceDefeatCancelsRestriction()
  {var cat=BattlefieldTests.Catalog();var g=Next(cat);var s=new JsonStateCodec().Read(g.ExportSave());s.Effects.Add(new ActiveEffect{SourceUnitId="hero:1",Kind=EffectKind.ImmunityAndUnitTraversal,Window=EffectTimeline.Create(s.Round,s.Turn,4,EffectDuration.ThisTurn)});Assert.That(MovementRules.LegalMoves(cat,s,1,MoveMode.Secondary),Is.Not.Empty);Apply(g,0,CommandKind.DebugDefeatHero,"hero:0",target:1);Assert.That(g.View(1).SecondaryMoves,Is.Not.Empty);ChargeTests.Restore(cat,g);}
  [Test] public void PushIgnoresCannotMoveAndDoesNotRemoveRestriction()
  {var cat=BattlefieldTests.Catalog();var g=Next(cat);var s=new JsonStateCodec().Read(g.ExportSave());var source=s.Units.Single(u=>u.Seat==0);var target=s.Units.Single(u=>u.Seat==1);var pushed=PushRules.AwayFromAdjacent(cat,s,source,target,1);Assert.That(pushed.Path.Last(),Is.EqualTo(new Hex(5,-8)));Assert.That(g.View(1).SecondaryMoves,Is.Empty);}
  [Test] public void NextTurnWindowExpiresBeforeThirdTurn()
  {var cat=BattlefieldTests.Catalog();var g=Cast(cat);var e=g.View(0).Effects.Single();Assert.That(e.Window.StartTurn,Is.EqualTo(2));Assert.That(e.Window.EndTurn,Is.EqualTo(2));Apply(g,0,CommandKind.DebugAdvance,"turn");Apply(g,0,CommandKind.DebugSelectAll,"first");Apply(g,0,CommandKind.DebugAdvance,"turn");Assert.That(g.View(0).Effects.Any(eff=>eff.SourceCardId==Card),Is.False);ChargeTests.Restore(cat,g);}
  [Test] public void OtherControllerUsesItsOwnBlueCardNotTargetsGoldCard()
  {var cat=BattlefieldTests.Catalog();var g=Cast(cat);Apply(g,0,CommandKind.DebugAdvance,"turn");var cards=new[]{"shargatha-00-反击","tigerclaw-00-瞬闪打击","arien-07-潮水","wasp-13-控物"};for(int i=0;i<4;i++)Apply(g,i,CommandKind.SelectCard,cards[i]);OpportuneMomentTests.AdvanceTo(g,3);Apply(g,3,CommandKind.BeginPrimary);Assert.That(g.View(3).EffectTargets,Does.Contain("hero:1"));Apply(g,3,CommandKind.ChooseEffectTarget,"hero:1");Assert.That(g.View(3).Pending?.ResumeAt,Is.Not.EqualTo("target_unit_move"));Assert.That(g.View(0).Units.Single(u=>u.Seat==1).Position,Is.EqualTo(new Hex(4,-8)));ChargeTests.Restore(cat,g);}
  [Test] public void DefenseMovementUsesDefenseCardRatherThanParentAttackColor()
  {var cat=BattlefieldTests.Catalog();var g=Cast(cat);Apply(g,0,CommandKind.DebugAdvance,"turn");Apply(g,0,CommandKind.DebugEquipCard,"tigerclaw-15-侧步",target:1);var s=new JsonStateCodec().Read(g.ExportSave());new GameRules().Apply(cat,s,new Command{ActorSeat=0,Kind=CommandKind.DebugAttack,Value="hero:1|5"});s.Execution.Attack.Ranged=true;new GameRules().Apply(cat,s,new Command{ActorSeat=1,Kind=CommandKind.Defend,Value="tigerclaw-15-侧步"});Assert.That(s.Pending?.ResumeAt,Is.Not.EqualTo("defense_response_move"));Assert.That(s.Units.Single(u=>u.Seat==1).Position,Is.EqualTo(new Hex(4,-8)));Assert.That(s.Events.Any(e=>e.Kind=="EffectMoveSkipped" && e.CardId=="tigerclaw-15-侧步"),Is.True);}
  [Test] public void FastMovementIsAlsoDeniedEvenWhenItsRegionIsSafe()
  {var cat=BattlefieldTests.Catalog();var g=Next(cat);var s=new JsonStateCodec().Read(g.ExportSave());s.Units.RemoveAll(u=>u.Seat!=0 && u.Seat!=1);s.Units.Single(u=>u.Seat==0).Position=new Hex(0,-3);s.Units.Single(u=>u.Seat==1).Position=new Hex(1,-4);Assert.That(MovementRules.LegalMoves(cat,s,1,MoveMode.Fast),Is.Empty);s.Players[1].Cards.Single(c=>c.Zone==CardZone.PlayedUnresolved).Zone=CardZone.PlayedResolved;s.Players[1].Cards.Single(c=>c.CardId=="tigerclaw-02-偷袭").Zone=CardZone.PlayedUnresolved;Assert.That(MovementRules.LegalMoves(cat,s,1,MoveMode.Fast),Is.Not.Empty);}
  [Test] public void PlacementCanRelocateRestrictedHeroWithoutBeingOrdinaryMovement()
  {var cat=BattlefieldTests.Catalog();var g=Cast(cat);Apply(g,0,CommandKind.DebugAdvance,"turn");Apply(g,0,CommandKind.DebugEquipCard,"wasp-09-意念操控",target:3);var cards=new[]{"shargatha-00-反击","tigerclaw-00-瞬闪打击","arien-07-潮水","wasp-09-意念操控"};for(int i=0;i<4;i++)Apply(g,i,CommandKind.SelectCard,cards[i]);OpportuneMomentTests.AdvanceTo(g,3);Apply(g,3,CommandKind.BeginPrimary);Assert.That(g.View(3).EffectTargets,Does.Contain("hero:1"));Apply(g,3,CommandKind.ChooseEffectTarget,"hero:1");Apply(g,3,CommandKind.ChoosePlacement,cell:new Hex(5,-9));Assert.That(g.View(0).Units.Single(u=>u.Seat==1).Position,Is.EqualTo(new Hex(5,-9)));ChargeTests.Restore(cat,g);}
  [Test] public void Old92PoisonPendingBytesUnchanged(){var cat=BattlefieldTests.Catalog();var save=File.ReadAllText(Path.Combine(ContentTests.Root(),"tests/fixtures/engine92-poison-dart-defense.json"));var g=LocalGameFactory.Restore(cat,save);Assert.That(g.ExportSave(),Is.EqualTo(save));Assert.That(g.View(0).SupportedPrimaryCards,Does.Not.Contain(Card));}
 }
}
