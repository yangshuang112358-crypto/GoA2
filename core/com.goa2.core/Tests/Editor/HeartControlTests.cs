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
 public sealed class HeartControlTests
 {
  internal const string Card="wasp-11-心灵控制";
  [Test] public void ExactTextAndEngineGate()
  {var c=BattlefieldTests.Catalog().Card(Card);Assert.That(CombatRules.HasPrimaryProgram(c),Is.True);Assert.That(CombatRules.HasPrimaryProgram(c,78),Is.False);c.Text+="不同";Assert.That(CombatRules.HasPrimaryProgram(c),Is.False);}
  [Test] public void TwoPlacementsAreSeparateActionsAndThereIsNoThirdRepeat()
  {
   var cat=BattlefieldTests.Catalog();var g=MindControlTests.Ready(cat,Card);Apply(g,0,CommandKind.BeginPrimary);Apply(g,0,CommandKind.ChooseEffectTarget,"hero:1");Apply(g,0,CommandKind.ChoosePlacement,cell:new Hex(0,-4));
   Assert.That(g.View(0).Pending.ResumeAt,Is.EqualTo("unit_placement_repeat"));Assert.That(g.View(0).EffectTargets,Does.Not.Contain("hero:1"));Assert.That(g.View(0).EffectTargets,Does.Contain("hero:2"));g=ChargeTests.Restore(cat,g);
   var first=new JsonStateCodec().Read(g.ExportSave()).Execution.ActionInstanceId;Apply(g,0,CommandKind.ChooseEffectTarget,"hero:2");Assert.That(new JsonStateCodec().Read(g.ExportSave()).Execution.ActionInstanceId,Is.Not.EqualTo(first));g=ChargeTests.Restore(cat,g);
   Apply(g,0,CommandKind.ChoosePlacement,cell:new Hex(0,-6));Assert.That(g.View(0).Pending?.ResumeAt,Is.Not.EqualTo("unit_placement_repeat"));Assert.That(g.View(0).Events.Count(e=>e.Kind=="UnitPlaced"),Is.EqualTo(2));
   Assert.That(g.View(0).Events.Count(e=>e.Kind=="ActionRepeated"),Is.EqualTo(1));Assert.That(g.View(0).Events.Count(e=>e.Kind=="CardResolved" && e.CardId==Card),Is.EqualTo(1));ChargeTests.Restore(cat,g);
  }
  [Test] public void FirstPlacementIsRequiredButSecondCanBeDeclined()
  {
   var cat=BattlefieldTests.Catalog();var g=MindControlTests.Ready(cat,Card);Apply(g,0,CommandKind.BeginPrimary);string before=g.ExportSave();Assert.That(g.Execute(0,Cmd(g,0,CommandKind.ChooseEffectTarget,"skip")).Accepted,Is.False);Assert.That(g.ExportSave(),Is.EqualTo(before));
   Apply(g,0,CommandKind.ChooseEffectTarget,"hero:1");Apply(g,0,CommandKind.ChoosePlacement,cell:new Hex(0,-4));Apply(g,0,CommandKind.ChooseEffectTarget,"skip");Assert.That(g.View(0).Events.Count(e=>e.Kind=="UnitPlaced"),Is.EqualTo(1));ChargeTests.Restore(cat,g);
  }
  [Test] public void SameMinionCanBeSelectedAgainAfterMandatoryReturnChangesItsPosition()
  {
   var cat=BattlefieldTests.Catalog();var g=MindControlTests.Ready(cat,Card);Apply(g,0,CommandKind.BeginPrimary);Apply(g,0,CommandKind.ChooseEffectTarget,"minion:-1,-3");Apply(g,0,CommandKind.ChoosePlacement,cell:new Hex(0,-4));
   Assert.That(g.View(1).Pending.Kind,Is.EqualTo("minion_return"));Assert.That(g.View(0).EffectTargets,Is.Empty);g=ChargeTests.Restore(cat,g);
   Apply(g,1,CommandKind.ChooseMinionReturn,"minion:-1,-3",cell:new Hex(-1,-3));Assert.That(g.View(0).Pending.ResumeAt,Is.EqualTo("unit_placement_repeat"));Assert.That(g.View(0).EffectTargets,Does.Contain("minion:-1,-3"));
   Apply(g,0,CommandKind.ChooseEffectTarget,"minion:-1,-3");Apply(g,0,CommandKind.ChoosePlacement,cell:new Hex(0,-4));g=ChargeTests.Restore(cat,g);
   Apply(g,1,CommandKind.ChooseMinionReturn,"minion:-1,-3",cell:new Hex(0,-3));Assert.That(g.View(0).Events.Count(e=>e.Kind=="CardResolved" && e.CardId==Card),Is.EqualTo(1));ChargeTests.Restore(cat,g);
  }
  [Test] public void NoSecondTargetFinishesWithoutAForcedEmptyChoice()
  {
   var cat=BattlefieldTests.Catalog();var g=MindControlTests.Ready(cat,Card);Apply(g,0,CommandKind.BeginPrimary);Apply(g,0,CommandKind.ChooseEffectTarget,"hero:1");var s=new JsonStateCodec().Read(g.ExportSave());s.Units.RemoveAll(u=>u.Seat!=0 && u.Seat!=1);
   new GameRules().Apply(cat,s,new Command{ActorSeat=0,Kind=CommandKind.ChoosePlacement,Destination=new Hex(0,-4)});Assert.That(s.Pending?.ResumeAt,Is.Not.EqualTo("unit_placement_repeat"));Assert.That(s.Events.Count(e=>e.Kind=="CardResolved" && e.CardId==Card),Is.EqualTo(1));
  }
  [Test] public void RepeatChoiceIsPrivateAndDuplicateDoesNotSpendAnotherAction()
  {
   var cat=BattlefieldTests.Catalog();var g=MindControlTests.Ready(cat,Card);Apply(g,0,CommandKind.BeginPrimary);Apply(g,0,CommandKind.ChooseEffectTarget,"hero:1");Apply(g,0,CommandKind.ChoosePlacement,cell:new Hex(0,-4));Assert.That(g.View(1).EffectTargets,Is.Empty);
   string before=g.ExportSave();Assert.That(g.Execute(2,Cmd(g,2,CommandKind.ChooseEffectTarget,"hero:2")).Accepted,Is.False);Assert.That(g.ExportSave(),Is.EqualTo(before));
   var pick=Cmd(g,0,CommandKind.ChooseEffectTarget,"hero:2");Assert.That(g.Execute(0,pick).Accepted,Is.True);before=g.ExportSave();Assert.That(g.Execute(0,pick).Duplicate,Is.True);Assert.That(g.ExportSave(),Is.EqualTo(before));ChargeTests.Restore(cat,g);
  }
  [Test] public void Old78PlacementSaveKeepsOriginalBytesAndDoesNotEnableRepeatCard()
  {
   var cat=BattlefieldTests.Catalog();string save=File.ReadAllText(Path.Combine(ContentTests.Root(),"tests/fixtures/engine78-mind-control-placement.json"));var g=LocalGameFactory.Restore(cat,save);
   Assert.That(g.ExportSave(),Is.EqualTo(save));Assert.That(g.View(0).SupportedPrimaryCards,Does.Not.Contain(Card));
  }
 }
}
