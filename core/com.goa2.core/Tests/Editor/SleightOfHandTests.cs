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
 public sealed class SleightOfHandTests
 {
  internal const string Sleight="tigerclaw-10-探囊取物";
  [Test] public void ContractAndEngineGate()
  {var c=BattlefieldTests.Catalog().Card(Sleight);Assert.That(c.Initiative,Is.EqualTo(2));Assert.That(c.SecondaryMovement,Is.EqualTo(3));Assert.That(c.SecondaryDefense,Is.EqualTo(2));Assert.That(CombatRules.HasPrimaryProgram(c),Is.True);Assert.That(CombatRules.HasPrimaryProgram(c,37),Is.False);c.Text+="然后攻击";Assert.That(CombatRules.HasPrimaryProgram(c),Is.False);}
  [TestCase(0)] [TestCase(1)] [TestCase(3)] public void ChoicesRespectEachAdjacentEnemiesOwnPurse(int gold)
  {var cat=BattlefieldTests.Catalog();var g=PickpocketTests.Setup(cat,card:Sleight);Apply(g,0,CommandKind.DebugSetGold,gold.ToString(),target:1);Apply(g,0,CommandKind.DebugSetGold,"1",target:3);PickpocketTests.GoldStep(g);Assert.That(g.View(0).GoldTransfers.Where(o=>o.TargetSeat==1).Select(o=>o.Amount),Is.EqualTo(Enumerable.Range(1,System.Math.Min(gold,2))));Assert.That(g.View(0).GoldTransfers.Where(o=>o.TargetSeat==3).Select(o=>o.Amount),Is.EqualTo(new[]{1}));ChargeTests.Restore(cat,g);}
  [TestCase(1)] [TestCase(2)] public void SelectedAmountTransfersExactlyOnceAndResumesMovement(int amount)
  {
   var cat=BattlefieldTests.Catalog();var g=PickpocketTests.Setup(cat,card:Sleight);PickpocketTests.GoldStep(g);g=ChargeTests.Restore(cat,g);string before=g.ExportSave();Assert.That(g.Execute(0,Cmd(g,0,CommandKind.ChooseGoldTransfer,"3",target:1)).Accepted,Is.False);Assert.That(g.ExportSave(),Is.EqualTo(before));
   var valid=Cmd(g,0,CommandKind.ChooseGoldTransfer,amount.ToString(),target:1);Assert.That(g.Execute(0,valid).Accepted,Is.True);before=g.ExportSave();Assert.That(g.Execute(0,valid).Duplicate,Is.True);Assert.That(g.ExportSave(),Is.EqualTo(before));Assert.That(g.View(0).Players[0].Gold,Is.EqualTo(amount));Assert.That(g.View(0).Players[1].Gold,Is.EqualTo(3-amount));Assert.That(g.View(0).Players.Sum(p=>p.Gold),Is.EqualTo(3));g=ChargeTests.Restore(cat,g);Assert.That(g.View(0).Pending!.Optional,Is.False);Apply(g,0,CommandKind.ChooseEffectMove,cell:g.View(0).EffectMoves.First().Destination);ChargeTests.Restore(cat,g);
  }
  [Test] public void CannotTakeTwoFromOneCoinEnemyOrCombineTwoEnemies()
  {var cat=BattlefieldTests.Catalog();var g=PickpocketTests.Setup(cat,card:Sleight);Apply(g,0,CommandKind.DebugSetGold,"1",target:1);Apply(g,0,CommandKind.DebugSetGold,"1",target:3);PickpocketTests.GoldStep(g);string before=g.ExportSave();Assert.That(g.Execute(0,Cmd(g,0,CommandKind.ChooseGoldTransfer,"2",target:1)).Accepted,Is.False);Assert.That(g.ExportSave(),Is.EqualTo(before));Apply(g,0,CommandKind.ChooseGoldTransfer,"1",target:1);before=g.ExportSave();Assert.That(g.Execute(0,Cmd(g,0,CommandKind.ChooseGoldTransfer,"1",target:3)).Accepted,Is.False);Assert.That(g.ExportSave(),Is.EqualTo(before));}
  [Test] public void PreviousFrozenGoldChoiceCannotGainTheNewCardsLimit()
  {var cat=BattlefieldTests.Catalog();string save=File.ReadAllText(Path.Combine(ContentTests.Root(),"tests/fixtures/engine37-pick-gold.json"));var g=LocalGameFactory.Restore(cat,save);Assert.That(g.ExportSave(),Is.EqualTo(save));Assert.That(g.View(0).SupportedPrimaryCards,Does.Not.Contain(Sleight));Assert.That(g.View(0).GoldTransfers.Select(o=>o.Amount),Is.EqualTo(new[]{1}));Assert.That(g.Execute(0,Cmd(g,0,CommandKind.ChooseGoldTransfer,"2",target:1)).Accepted,Is.False);Assert.That(g.ExportSave(),Is.EqualTo(save));Apply(g,0,CommandKind.ChooseGoldTransfer,"1",target:1);ChargeTests.Restore(cat,g);}
 }
}
