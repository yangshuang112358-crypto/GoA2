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
 public sealed class SlipperyTests
 {
  internal const string Card="arien-14-滑溜溜";
  internal static GameSession Ready(ContentCatalog cat,string enemy="shargatha-13-石化",string card=Card)
  {
   var g=LocalGameFactory.Create(cat,"slippery",new[]{"A","B","C","D"},42,true);Apply(g,0,CommandKind.DebugPrepare,"arien,shargatha,brogan,tigerclaw");Apply(g,0,CommandKind.DebugEquipCard,card,target:0);Apply(g,0,CommandKind.DebugEquipCard,enemy,target:1);
   var pos=new[]{new Hex(3,-8),new Hex(4,-8),new Hex(6,-8),new Hex(3,-9)};for(int i=0;i<4;i++)Apply(g,0,CommandKind.DebugTeleport,"hero:"+i,cell:pos[i]);
   var cards=new[]{card,enemy,"brogan-06-铜墙铁壁","tigerclaw-08-偷天妙手"};for(int i=0;i<4;i++)Apply(g,i,CommandKind.SelectCard,cards[i]);OpportuneMomentTests.AdvanceTo(g,0);return g;
  }
  internal static GameSession Active(ContentCatalog cat,string enemy="shargatha-13-石化",string card=Card){var g=Ready(cat,enemy,card);Apply(g,0,CommandKind.BeginPrimary);OpportuneMomentTests.AdvanceTo(g,1);return g;}
  [Test] public void ExactTextAndGate(){var c=BattlefieldTests.Catalog().Card(Card);Assert.That(CombatRules.HasPrimaryProgram(c),Is.True);Assert.That(CombatRules.HasPrimaryProgram(c,88),Is.False);c.Text+="推动";Assert.That(CombatRules.HasPrimaryProgram(c),Is.False);}
  [Test] public void SkillCreatesTurnEffectAndRestores(){var cat=BattlefieldTests.Catalog();var g=Active(cat);Assert.That(g.View(0).Effects.Single().SourceCardId,Is.EqualTo(Card));Assert.That(g.View(0).Effects.Single().Window.EndTurn,Is.EqualTo(g.View(0).Turn));ChargeTests.Restore(cat,g);}
  [Test] public void SecondaryMovementIsOneStepAndInvalidLongMoveIsAtomic()
  {var cat=BattlefieldTests.Catalog();var g=Active(cat);var moves=g.View(1).SecondaryMoves;Assert.That(moves,Is.Not.Empty);Assert.That(moves.All(m=>m.Path.Count<=2),Is.True);string before=g.ExportSave();Assert.That(g.Execute(1,Cmd(g,1,CommandKind.Move,destination:new Hex(6,-7))).Accepted,Is.False);Assert.That(g.ExportSave(),Is.EqualTo(before));Apply(g,1,CommandKind.Move,cell:moves.First().Destination);ChargeTests.Restore(cat,g);}
  [Test] public void PrimaryMovementIsAlsoLimited()
  {var cat=BattlefieldTests.Catalog();var g=Active(cat,CharmTests.Card);Apply(g,1,CommandKind.BeginPrimary);Assert.That(g.View(1).EffectMoves,Is.Not.Empty);Assert.That(g.View(1).EffectMoves.All(m=>m.Path.Count<=2),Is.True);g=ChargeTests.Restore(cat,g);Apply(g,1,CommandKind.ChooseEffectMove,"skip");}
  [Test] public void OutsideStartMayEnterWithoutOneStepLimit()
  {var cat=BattlefieldTests.Catalog();var g=Active(cat);Apply(g,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(7,-8));Assert.That(g.View(1).SecondaryMoves.Any(m=>m.Path.Count>2 && m.Destination.Distance(new Hex(3,-8))<=3),Is.True);}
  [Test] public void RangeAndMovementPassivesDoNotBypassLimit()
  {var cat=BattlefieldTests.Catalog();var g=Active(cat);var s=new JsonStateCodec().Read(g.ExportSave());s.Units.Single(u=>u.Seat==1).Position=new Hex(7,-8);s.Players[0].RangeBonus=1;s.Players[1].MovementBonus=5;var moves=MovementRules.LegalMoves(cat,s,1,MoveMode.Secondary);Assert.That(moves,Is.Not.Empty);Assert.That(moves.All(m=>m.Path.Count<=2),Is.True);}
  [Test] public void ExistingImmunityAndFriendlyTeamExempt()
  {var cat=BattlefieldTests.Catalog();var g=Active(cat);var s=new JsonStateCodec().Read(g.ExportSave());s.Effects.Add(new ActiveEffect{Kind=EffectKind.ImmunityAndUnitTraversal,SourceUnitId="hero:1",Window=EffectTimeline.Create(s.Round,s.Turn,4,EffectDuration.ThisTurn)});Assert.That(MovementRules.LegalMoves(cat,s,1,MoveMode.Secondary).Any(m=>m.Path.Count>2),Is.True);s.Effects.RemoveAt(s.Effects.Count-1);s.Units.Single(u=>u.Seat==1).Team=Team.Blue;Assert.That(MovementRules.LegalMoves(cat,s,1,MoveMode.Secondary).Any(m=>m.Path.Count>2),Is.True);}
  [Test] public void InternalCardMovementRemainsTwoSteps()
  {var cat=BattlefieldTests.Catalog();var g=Active(cat);OpportuneMomentTests.AdvanceTo(g,3);Apply(g,3,CommandKind.BeginPrimary);Assert.That(g.View(3).EffectMoves.Any(m=>m.Path.Count==3),Is.True);ChargeTests.Restore(cat,g);}
  [Test] public void FastMovementStillRequiresSafeRegionsAndOneHex()
  {var cat=BattlefieldTests.Catalog();var g=Active(cat);var s=new JsonStateCodec().Read(g.ExportSave());Assert.That(MovementRules.LegalMoves(cat,s,1,MoveMode.Fast),Is.Empty);s.Units.RemoveAll(u=>u.Kind!="hero");var actor=s.Units.Single(u=>u.Seat==1);var source=s.Units.Single(u=>u.Seat==0);bool tested=false;
   foreach(var a in cat.Cells.Where(c=>!c.Obstacle))foreach(var b in cat.Cells.Where(c=>!c.Obstacle && c.Region!=a.Region && c.Position.Distance(a.Position)<=3))
   {actor.Position=a.Position;source.Position=b.Position;var effects=s.Effects.ToList();s.Effects.Clear();var free=MovementRules.LegalMoves(cat,s,1,MoveMode.Fast);s.Effects.AddRange(effects);if(!free.Any(m=>m.Destination.Distance(actor.Position)>1))continue;var capped=MovementRules.LegalMoves(cat,s,1,MoveMode.Fast);Assert.That(capped.All(m=>m.Destination.Distance(actor.Position)<=1),Is.True);tested=true;break;}Assert.That(tested,Is.True);
  }
  [Test] public void SourceDefeatAndTurnEndRemoveLimit()
  {var cat=BattlefieldTests.Catalog();var g=Active(cat);Apply(g,0,CommandKind.DebugDefeatHero,"hero:0",target:1);Assert.That(g.View(1).SecondaryMoves.Any(m=>m.Path.Count>2),Is.True);g=Active(cat);Apply(g,0,CommandKind.DebugAdvance,"turn");Assert.That(g.View(0).Effects.Any(e=>e.SourceCardId==Card),Is.False);ChargeTests.Restore(cat,g);}
  [Test] public void Old88PendingBytesUnchanged(){var cat=BattlefieldTests.Catalog();var save=File.ReadAllText(Path.Combine(ContentTests.Root(),"tests/fixtures/engine88-dominance-defense.json"));var g=LocalGameFactory.Restore(cat,save);Assert.That(g.ExportSave(),Is.EqualTo(save));Assert.That(g.View(0).SupportedPrimaryCards,Does.Not.Contain(Card));}
 }
}
