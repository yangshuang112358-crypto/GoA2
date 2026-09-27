using System.IO;
using System.Linq;
using Goa2.Domain;
using Goa2.Infrastructure;
using Goa2.Rules;
using NUnit.Framework;
using static Goa2.Tests.SessionTests;
using static Goa2.Tests.TurnFlowTests;
namespace Goa2.Tests
{
 public sealed class PsychicVortexTests
 {
  internal const string Card="wasp-17-意念黑洞";
  [Test] public void ExactTextAndGate()
  {var c=BattlefieldTests.Catalog().Card(Card);Assert.That(c.SecondaryDefense,Is.EqualTo(6));Assert.That(CombatRules.HasPrimaryProgram(c),Is.True);Assert.That(CombatRules.HasPrimaryProgram(c,84),Is.False);c.Text+="任意次";Assert.That(CombatRules.HasPrimaryProgram(c),Is.False);}
  [Test] public void ThreeSeparateMovementsThenNoFourthOffer()
  {
   var cat=BattlefieldTests.Catalog();var g=TelekinesisTests.Ready(cat,Card);Apply(g,0,CommandKind.BeginPrimary);
   foreach(var pos in new[]{new Hex(2,-5),new Hex(2,-6),new Hex(2,-5)})
   {Apply(g,0,CommandKind.ChooseEffectTarget,"hero:1");g=ChargeTests.Restore(cat,g);Assert.That(g.View(0).EffectMoves.All(m=>m.Path.Count==2),Is.True);Apply(g,0,CommandKind.ChooseEffectMove,cell:pos);g=ChargeTests.Restore(cat,g);}
   Assert.That(g.View(0).Events.Count(e=>e.Kind=="UnitMoved" && e.CardId==Card),Is.EqualTo(3));Assert.That(g.View(0).Events.Count(e=>e.Kind=="ActionRepeated" && e.CardId==Card),Is.EqualTo(2));Assert.That(g.View(0).Events.Count(e=>e.Kind=="CardResolved" && e.CardId==Card),Is.EqualTo(1));Assert.That(g.View(0).Pending?.ResumeAt,Is.Not.EqualTo("orbital_repeat"));
  }
  [TestCase(1)] [TestCase(2)] public void SkipAfterEitherCompletedActionEndsAllRemainingRepeats(int count)
  {
   var cat=BattlefieldTests.Catalog();var g=TelekinesisTests.Ready(cat,Card);Apply(g,0,CommandKind.BeginPrimary);
   for(int i=0;i<count;i++){Apply(g,0,CommandKind.ChooseEffectTarget,i==0?"hero:1":"hero:2");Apply(g,0,CommandKind.ChooseEffectMove,"skip");}
   Apply(g,0,CommandKind.ChooseEffectTarget,"skip");Assert.That(g.View(0).Events.Count(e=>e.Kind=="ActionRepeated" && e.CardId==Card),Is.EqualTo(count-1));Assert.That(g.View(0).Events.Count(e=>e.Kind=="CardResolved" && e.CardId==Card),Is.EqualTo(1));ChargeTests.Restore(cat,g);
  }
  [Test] public void ThreeZeroStepsConsumeTheFullBudgetWithoutMovement()
  {
   var cat=BattlefieldTests.Catalog();var g=TelekinesisTests.Ready(cat,Card);Apply(g,0,CommandKind.BeginPrimary);
   for(int i=0;i<3;i++){Apply(g,0,CommandKind.ChooseEffectTarget,"hero:1");Apply(g,0,CommandKind.ChooseEffectMove,"skip");}
   Assert.That(g.View(0).Events.Any(e=>e.Kind=="UnitMoved"),Is.False);Assert.That(g.View(0).Events.Count(e=>e.Kind=="CardResolved" && e.CardId==Card),Is.EqualTo(1));ChargeTests.Restore(cat,g);
  }
  [Test] public void EachMinionReturnPrecedesTheNextRepeat()
  {
   var cat=BattlefieldTests.Catalog();var g=TelekinesisTests.Ready(cat,Card);Apply(g,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(2,-5));Apply(g,0,CommandKind.DebugTeleport,"minion:-1,-3",cell:new Hex(0,-3));Apply(g,0,CommandKind.BeginPrimary);
   for(int i=0;i<3;i++){Apply(g,0,CommandKind.ChooseEffectTarget,"minion:-1,-3");Apply(g,0,CommandKind.ChooseEffectMove,cell:new Hex(1,-4));Assert.That(g.View(1).Pending.Kind,Is.EqualTo("minion_return"));g=ChargeTests.Restore(cat,g);Apply(g,1,CommandKind.ChooseMinionReturn,"minion:-1,-3",cell:new Hex(0,-3));}
   Assert.That(g.View(0).Events.Count(e=>e.Kind=="CardResolved" && e.CardId==Card),Is.EqualTo(1));ChargeTests.Restore(cat,g);
  }
  [Test] public void LastRepeatRejectsSelfAndWrongSeatAndRetryIsIdempotent()
  {
   var cat=BattlefieldTests.Catalog();var g=TelekinesisTests.Ready(cat,Card);Apply(g,0,CommandKind.BeginPrimary);for(int i=0;i<2;i++){Apply(g,0,CommandKind.ChooseEffectTarget,"hero:1");Apply(g,0,CommandKind.ChooseEffectMove,"skip");}string before=g.ExportSave();
   foreach(var c in new[]{Cmd(g,0,CommandKind.ChooseEffectTarget,"hero:0"),Cmd(g,1,CommandKind.ChooseEffectTarget,"hero:1")}){Assert.That(g.Execute(c.ActorSeat,c).Accepted,Is.False);Assert.That(g.ExportSave(),Is.EqualTo(before));}
   var choose=Cmd(g,0,CommandKind.ChooseEffectTarget,"hero:1");Assert.That(g.Execute(0,choose).Accepted,Is.True);before=g.ExportSave();Assert.That(g.Execute(0,choose).Duplicate,Is.True);Assert.That(g.ExportSave(),Is.EqualTo(before));Apply(g,0,CommandKind.ChooseEffectMove,"skip");ChargeTests.Restore(cat,g);
  }
  [Test] public void Old84RepeatSaveRetainsBytes()
  {var cat=BattlefieldTests.Catalog();string save=File.ReadAllText(Path.Combine(ContentTests.Root(),"tests/fixtures/engine84-gravity-control-repeat.json"));var g=LocalGameFactory.Restore(cat,save);Assert.That(g.ExportSave(),Is.EqualTo(save));Assert.That(g.View(0).SupportedPrimaryCards,Does.Not.Contain(Card));}
 }
}
