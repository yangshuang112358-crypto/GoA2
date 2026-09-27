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
 public sealed class GravityControlTests
 {
  internal const string Card="wasp-15-引力控制";
  [Test] public void ExactTextAndGate()
  {var c=BattlefieldTests.Catalog().Card(Card);Assert.That(CombatRules.HasPrimaryProgram(c),Is.True);Assert.That(CombatRules.HasPrimaryProgram(c,83),Is.False);c.Text+="不同";Assert.That(CombatRules.HasPrimaryProgram(c),Is.False);}
  [Test] public void SameUnitCanMoveTwiceInSeparateOneStepActions()
  {
   var cat=BattlefieldTests.Catalog();var g=TelekinesisTests.Ready(cat,Card);Apply(g,0,CommandKind.BeginPrimary);Apply(g,0,CommandKind.ChooseEffectTarget,"hero:1");Apply(g,0,CommandKind.ChooseEffectMove,cell:new Hex(2,-5));
   Assert.That(g.View(0).Pending.ResumeAt,Is.EqualTo("orbital_repeat"));Assert.That(g.View(0).EffectTargets,Does.Contain("hero:1"));g=ChargeTests.Restore(cat,g);var first=new JsonStateCodec().Read(g.ExportSave()).Execution.ActionInstanceId;
   Apply(g,0,CommandKind.ChooseEffectTarget,"hero:1");Assert.That(new JsonStateCodec().Read(g.ExportSave()).Execution.ActionInstanceId,Is.Not.EqualTo(first));g=ChargeTests.Restore(cat,g);Apply(g,0,CommandKind.ChooseEffectMove,cell:new Hex(2,-6));
   Assert.That(g.View(0).Events.Count(e=>e.Kind=="UnitMoved" && e.CardId==Card),Is.EqualTo(2));Assert.That(g.View(0).Events.Count(e=>e.Kind=="ActionRepeated" && e.CardId==Card),Is.EqualTo(1));Assert.That(g.View(0).Pending?.ResumeAt,Is.Not.EqualTo("orbital_repeat"));ChargeTests.Restore(cat,g);
  }
  [Test] public void RepeatCanChooseAnotherUnitAndZeroStepsStillConsumeTheAction()
  {
   var cat=BattlefieldTests.Catalog();var g=TelekinesisTests.Ready(cat,Card);Apply(g,0,CommandKind.BeginPrimary);Apply(g,0,CommandKind.ChooseEffectTarget,"hero:1");Apply(g,0,CommandKind.ChooseEffectMove,"skip");Apply(g,0,CommandKind.ChooseEffectTarget,"hero:2");Apply(g,0,CommandKind.ChooseEffectMove,"skip");
   Assert.That(g.View(0).Events.Count(e=>e.Kind=="ActionRepeated" && e.CardId==Card),Is.EqualTo(1));Assert.That(g.View(0).Events.Count(e=>e.Kind=="CardResolved" && e.CardId==Card),Is.EqualTo(1));Assert.That(g.View(0).Events.Any(e=>e.Kind=="UnitMoved"),Is.False);ChargeTests.Restore(cat,g);
  }
  [Test] public void DecliningRepeatDoesNotCreateAnotherAction()
  {
   var cat=BattlefieldTests.Catalog();var g=TelekinesisTests.Ready(cat,Card);Apply(g,0,CommandKind.BeginPrimary);Apply(g,0,CommandKind.ChooseEffectTarget,"hero:1");Apply(g,0,CommandKind.ChooseEffectMove,"skip");Apply(g,0,CommandKind.ChooseEffectTarget,"skip");Assert.That(g.View(0).Events.Any(e=>e.Kind=="ActionRepeated"),Is.False);Assert.That(g.View(0).Events.Count(e=>e.Kind=="CardResolved" && e.CardId==Card),Is.EqualTo(1));ChargeTests.Restore(cat,g);
  }
  [Test] public void MinionReturnCompletesBeforeItCanBeChosenAgain()
  {
   var cat=BattlefieldTests.Catalog();var g=TelekinesisTests.Ready(cat,Card);Apply(g,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(2,-5));Apply(g,0,CommandKind.DebugTeleport,"minion:-1,-3",cell:new Hex(0,-3));Apply(g,0,CommandKind.BeginPrimary);
   for(int i=0;i<2;i++){Apply(g,0,CommandKind.ChooseEffectTarget,"minion:-1,-3");Apply(g,0,CommandKind.ChooseEffectMove,cell:new Hex(1,-4));Assert.That(g.View(1).Pending.Kind,Is.EqualTo("minion_return"));Assert.That(g.View(0).EffectTargets,Is.Empty);g=ChargeTests.Restore(cat,g);Apply(g,1,CommandKind.ChooseMinionReturn,"minion:-1,-3",cell:new Hex(0,-3));if(i==0)Assert.That(g.View(0).Pending.ResumeAt,Is.EqualTo("orbital_repeat"));}
   Assert.That(g.View(0).Events.Count(e=>e.Kind=="CardResolved" && e.CardId==Card),Is.EqualTo(1));ChargeTests.Restore(cat,g);
  }
  [Test] public void RepeatWrongSeatAndSelfRejectAtomicallyAndDuplicateDoesNotSpendBudget()
  {
   var cat=BattlefieldTests.Catalog();var g=TelekinesisTests.Ready(cat,Card);Apply(g,0,CommandKind.BeginPrimary);Apply(g,0,CommandKind.ChooseEffectTarget,"hero:1");Apply(g,0,CommandKind.ChooseEffectMove,"skip");string before=g.ExportSave();foreach(var c in new[]{Cmd(g,1,CommandKind.ChooseEffectTarget,"hero:1"),Cmd(g,0,CommandKind.ChooseEffectTarget,"hero:0")}){Assert.That(g.Execute(c.ActorSeat,c).Accepted,Is.False);Assert.That(g.ExportSave(),Is.EqualTo(before));}
   var repeat=Cmd(g,0,CommandKind.ChooseEffectTarget,"hero:1");Assert.That(g.Execute(0,repeat).Accepted,Is.True);before=g.ExportSave();Assert.That(g.Execute(0,repeat).Duplicate,Is.True);Assert.That(g.ExportSave(),Is.EqualTo(before));Apply(g,0,CommandKind.ChooseEffectMove,"skip");ChargeTests.Restore(cat,g);
  }
  [Test] public void Old83MoveKeepsOriginalBytesAndNoRepeatCapability()
  {var cat=BattlefieldTests.Catalog();string save=File.ReadAllText(Path.Combine(ContentTests.Root(),"tests/fixtures/engine83-telekinesis-move.json"));var g=LocalGameFactory.Restore(cat,save);Assert.That(g.ExportSave(),Is.EqualTo(save));Assert.That(g.View(0).SupportedPrimaryCards,Does.Not.Contain(Card));}
 }
}
