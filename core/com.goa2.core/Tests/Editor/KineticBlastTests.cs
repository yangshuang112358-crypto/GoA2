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
 public sealed class KineticBlastTests
 {
  internal const string Card="wasp-16-动能震爆";
  internal static GameSession Setup(ContentCatalog cat)=>PowerBoostTests.Setup(cat,Card);
  private static void Pick(GameSession g,string id)=>Apply(g,0,CommandKind.ChooseEffectTarget,id);
  [Test] public void ContractAndEngineGate(){var c=BattlefieldTests.Catalog().Card(Card);Assert.That(c.Initiative,Is.EqualTo(10));Assert.That(c.SecondaryMovement,Is.EqualTo(3));Assert.That(c.SecondaryDefense,Is.EqualTo(6));Assert.That(CombatRules.HasPrimaryProgram(c),Is.True);Assert.That(CombatRules.HasPrimaryProgram(c,48),Is.False);c.Text+="可选";Assert.That(CombatRules.HasPrimaryProgram(c),Is.False);}
  [Test] public void ExactlyThreeStepsEvenWhenFourthCellIsOccupied()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);Apply(g,0,CommandKind.DebugTeleport,"minion:1,0",cell:new Hex(-2,-5));Apply(g,0,CommandKind.BeginPrimary);Pick(g,"hero:1");var e=g.View(0).Events.Last(x=>x.Kind=="UnitPushed");Assert.That(e.Path.Count,Is.EqualTo(4));Assert.That(e.To,Is.EqualTo(new Hex(-2,-4)));Assert.That(g.View(0).Events.Any(x=>x.Kind=="PushStopped"),Is.False);g=ChargeTests.Restore(cat,g);Pick(g,"hero:3");Pick(g,PowerBoostTests.Minion);Assert.That(g.View(0).EffectTargets,Is.EqualTo(new[]{"hero:3"}));Pick(g,"hero:3");Apply(g,3,CommandKind.ForcedDiscard,"arien-13-挑战者");ChargeTests.Restore(cat,g);}
  [TestCase(PowerBoostTests.Card,false)] [TestCase(Card,true)] public void ThirdCellObstacleOnlyAffectsThreeStepCard(string card,bool blocked)
  {var cat=BattlefieldTests.Catalog();var g=PowerBoostTests.Setup(cat,card);Apply(g,0,CommandKind.DebugTeleport,"minion:1,0",cell:new Hex(-2,-4));Apply(g,0,CommandKind.BeginPrimary);Pick(g,"hero:1");Assert.That(g.View(0).Events.Last(e=>e.Kind=="UnitPushed").To,Is.EqualTo(new Hex(-2,-3)));Pick(g,"hero:3");Pick(g,PowerBoostTests.Minion);Assert.That(g.View(0).EffectTargets.Contains("hero:1"),Is.EqualTo(blocked));if(blocked){Pick(g,"hero:1");g=ChargeTests.Restore(cat,g);Apply(g,1,CommandKind.ForcedDiscard,"sabina-00-近身射击");}Pick(g,"hero:3");Apply(g,3,CommandKind.ForcedDiscard,"arien-13-挑战者");Assert.That(g.View(0).Pending,Is.Null);}
  [Test] public void NoTargetStillResolvesExactlyOnce(){var cat=BattlefieldTests.Catalog();var g=Setup(cat);Apply(g,0,CommandKind.DebugTeleport,"hero:0",cell:new Hex(6,-8));Apply(g,0,CommandKind.BeginPrimary);Assert.That(g.View(0).Events.Count(e=>e.Kind=="CardResolved" && e.CardId==Card),Is.EqualTo(1));Assert.That(g.View(0).Events.Any(e=>e.Kind=="UnitPushed"),Is.False);}
  [Test] public void TargetOwnershipAndDuplicateCommandRemainAtomic(){var cat=BattlefieldTests.Catalog();var g=Setup(cat);Apply(g,0,CommandKind.BeginPrimary);var before=g.ExportSave();Assert.That(g.Execute(1,Cmd(g,1,CommandKind.ChooseEffectTarget,"hero:1")).Accepted,Is.False);Assert.That(g.ExportSave(),Is.EqualTo(before));var cmd=Cmd(g,0,CommandKind.ChooseEffectTarget,"hero:1");Assert.That(g.Execute(0,cmd).Accepted,Is.True);var after=g.ExportSave();Assert.That(g.Execute(0,cmd).Duplicate,Is.True);Assert.That(g.ExportSave(),Is.EqualTo(after));ChargeTests.Restore(cat,g);}
  [Test] public void ThreeStepMinionPushReturnsAfterHeroDiscard()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);Apply(g,0,CommandKind.DebugTeleport,"hero:0",cell:new Hex(-1,-2));Apply(g,0,CommandKind.DebugTeleport,PowerBoostTests.Minion,cell:new Hex(-1,-3));Apply(g,0,CommandKind.BeginPrimary);Pick(g,PowerBoostTests.Minion);Assert.That(g.View(0).Events.Last(e=>e.Kind=="UnitPushed").To,Is.EqualTo(new Hex(-1,-6)));Pick(g,"hero:1");Pick(g,"hero:1");g=ChargeTests.Restore(cat,g);Apply(g,1,CommandKind.ForcedDiscard,"sabina-00-近身射击");int moves=0;while(g.View(0).Pending?.Kind=="minion_return" && moves++<10){var p=g.View(0).Pending!;var o=g.View(p.ChooserSeat).MinionReturns.First();Apply(g,p.ChooserSeat,CommandKind.ChooseMinionReturn,o.UnitId,cell:o.Destination);g=ChargeTests.Restore(cat,g);}Assert.That(moves,Is.EqualTo(3));Assert.That(g.View(0).Pending,Is.Null);}
  [Test] public void Old48DiscardKeepsExactBytesAndCompletesOriginalTwoStepCard()
  {var cat=BattlefieldTests.Catalog();var save=File.ReadAllText(Path.Combine(ContentTests.Root(),"tests/fixtures/engine48-boost-discard.json"));var g=LocalGameFactory.Restore(cat,save);Assert.That(g.ExportSave(),Is.EqualTo(save));Assert.That(g.View(0).SupportedPrimaryCards,Does.Not.Contain(Card));Apply(g,3,CommandKind.ForcedDiscard,"arien-13-挑战者");Assert.That(g.View(0).Events.First(e=>e.Kind=="UnitPushed" && e.Seat==1).Path.Count,Is.EqualTo(3));ChargeTests.Restore(cat,g);}
 }
}
