using System.IO;
using System.Linq;
using Goa2.Domain;
using Goa2.Infrastructure;
using Goa2.Rules;
using NUnit.Framework;
using static Goa2.Tests.TurnFlowTests;
namespace Goa2.Tests
{
 public sealed class PoisonDartTests
 {
  internal const string Card="tigerclaw-12-剧毒飞镖";
  [Test] public void ExactTextAndGate(){var c=BattlefieldTests.Catalog().Card(Card);Assert.That(CombatRules.HasPrimaryProgram(c),Is.True);Assert.That(CombatRules.HasPrimaryProgram(c,91),Is.False);c.Text+="全部数值";Assert.That(CombatRules.HasPrimaryProgram(c),Is.False);}
  [TestCase(0)] [TestCase(1)] [TestCase(3)] public void AddsDefenseReversalWithoutChangingBaseDefense(int count)
  {var cat=BattlefieldTests.Catalog();var g=PoisonDaggerTests.Poison(cat,card:Card);if(g.View(0).Phase!=Phase.Planning)Apply(g,0,CommandKind.DebugAdvance,"turn");var s=new JsonStateCodec().Read(g.ExportSave());s.Players[1].DefenseBonus=count;new GameRules().Apply(cat,s,new Command{ActorSeat=0,Kind=CommandKind.DebugAttack,Value="hero:1|5"});var options=CombatRules.DefenseOptions(cat,s,1);Assert.That(options,Is.Not.Empty);Assert.That(options.All(o=>o.Assessment.DefenseBonus==-count),Is.True);Assert.That(options.All(o=>o.Assessment.FinalDefense==o.Assessment.BaseDefense-count),Is.True);Assert.That(s.Players[1].DefenseBonus,Is.EqualTo(count));Assert.That(s.PoisonMarkers.Single().IncludesDefense,Is.True);ChargeTests.Restore(cat,g);}
  [Test] public void InfiniteBlockStillWorksWithPoisonedDefensePassives()
  {var cat=BattlefieldTests.Catalog();var g=PoisonDaggerTests.Poison(cat,target:0,card:Card);if(g.View(0).Phase!=Phase.Planning)Apply(g,0,CommandKind.DebugAdvance,"turn");Apply(g,0,CommandKind.DebugEquipCard,"tigerclaw-14-近身格挡",target:0);var s=new JsonStateCodec().Read(g.ExportSave());s.Players[0].DefenseBonus=3;new GameRules().Apply(cat,s,new Command{ActorSeat=1,Kind=CommandKind.DebugAttack,Value="hero:0|99"});var block=CombatRules.DefenseOptions(cat,s,0).Single(o=>o.CardId=="tigerclaw-14-近身格挡");Assert.That(block.Block,Is.True);Assert.That(block.Assessment.Successful,Is.True);Assert.That(block.Assessment.DefenseBonus,Is.EqualTo(-3));}
  [Test] public void DefeatedHolderKeepsFullPoisonAndAdditionalPassivesUseCurrentCounts()
  {var cat=BattlefieldTests.Catalog();var g=PoisonDaggerTests.Poison(cat,card:Card);Apply(g,0,CommandKind.DebugDefeatHero,"hero:1",target:0);var s=new JsonStateCodec().Read(g.ExportSave());s.Players[1].DefenseBonus=2;Assert.That(PassiveRules.Defense(s,1),Is.EqualTo(-2));s.Players[1].DefenseBonus++;Assert.That(PassiveRules.Defense(s,1),Is.EqualTo(-3));ChargeTests.Restore(cat,g);}
  [Test] public void Old91PoisonPendingBytesUnchanged(){var cat=BattlefieldTests.Catalog();var save=File.ReadAllText(Path.Combine(ContentTests.Root(),"tests/fixtures/engine91-poison-dagger-defense.json"));var g=LocalGameFactory.Restore(cat,save);Assert.That(g.ExportSave(),Is.EqualTo(save));Assert.That(g.View(0).SupportedPrimaryCards,Does.Not.Contain(Card));}
 }
}
