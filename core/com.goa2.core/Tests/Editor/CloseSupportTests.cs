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
 public sealed class CloseSupportTests
 {
  internal const string Support="sabina-13-近身支援";
  internal static GameSession Setup(ContentCatalog cat,bool minion=true)
  {
   var g=DrillTests.Setup(cat,card:Support);while(g.View(0).ActiveSeat!=0){var v=g.View(0);if(v.Phase==Phase.InitiativeChoice)Apply(g,v.Pending!.ChooserSeat,CommandKind.ChooseInitiative,target:0);else Apply(g,v.ActiveSeat!.Value,CommandKind.Pass);}
   Apply(g,0,CommandKind.DebugTeleport,DrillTests.Melee,cell:new Hex(minion?4:1,-8));Apply(g,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(5,-8));Apply(g,0,CommandKind.DebugTeleport,"hero:3",cell:new Hex(4,-9));return g;
  }
  [Test] public void ExactTextAndVersionGate()
  {var c=BattlefieldTests.Catalog().Card(Support);Assert.That(c.PrimaryFamily,Is.EqualTo("skill"));Assert.That(c.Subtype,Is.EqualTo("范围"));Assert.That(CombatRules.HasPrimaryProgram(c),Is.True);Assert.That(CombatRules.HasPrimaryProgram(c,33),Is.False);c.Text+="否则被击败";Assert.That(CombatRules.HasPrimaryProgram(c),Is.False);}
  [Test] public void BothNonAttackingEnemiesAreCandidatesButFriendAndMinionsAreNot()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);Apply(g,0,CommandKind.BeginPrimary);Assert.That(g.View(0).EffectTargets,Is.EquivalentTo(new[]{"hero:1","hero:3"}));ChargeTests.Restore(cat,g);}
  [Test] public void WithoutAdjacentFriendlyMinionTheSkillEnds()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat,false);Apply(g,0,CommandKind.BeginPrimary);Assert.That(g.View(0).Pending?.Kind,Is.Not.EqualTo("effect_target"));Assert.That(g.View(0).Events.Any(e=>e.Kind=="ForcedDiscardRequired"),Is.False);ChargeTests.Restore(cat,g);}
  [Test] public void ProtectedHeavyCanSupplyTheAdjacentCondition()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat,false);Apply(g,0,CommandKind.DebugTeleport,DrillTests.Heavy,cell:new Hex(4,-8));Apply(g,0,CommandKind.BeginPrimary);Assert.That(g.View(0).EffectTargets,Does.Contain("hero:1"));ChargeTests.Restore(cat,g);}
  [TestCase(false)] [TestCase(true)] public void TargetDiscardsOrAutomaticallySkipsWhenEmpty(bool empty)
  {
   var cat=BattlefieldTests.Catalog();var g=Setup(cat);if(empty)foreach(var c in g.View(1).OwnCards.Where(c=>c.Zone==CardZone.InHand).ToArray())Apply(g,0,CommandKind.DebugDiscard,c.CardId,target:1);Apply(g,0,CommandKind.BeginPrimary);Apply(g,0,CommandKind.ChooseEffectTarget,"hero:1");
   if(!empty){ChargeTests.Restore(cat,g);Assert.That(g.View(1).CanDeclineRetaliationDiscard,Is.False);Assert.That(g.View(0).ForcedDiscardCards,Is.Empty);Apply(g,1,CommandKind.ForcedDiscard,g.View(1).ForcedDiscardCards.First());}
   Assert.That(g.View(1).Players[1].AwaitingRespawn,Is.False);Assert.That(g.View(1).Events.Any(e=>e.Kind=="AttackCalculated"),Is.False);ChargeTests.Restore(cat,g);
  }
  [Test] public void RangeBonusAndRangedBonusAreDifferent()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);Apply(g,0,CommandKind.DebugTeleport,"hero:3",cell:new Hex(7,-8));Apply(g,0,CommandKind.BeginPrimary);var codec=new JsonStateCodec();var state=codec.Read(g.ExportSave());state.Players[0].RangedBonus=10;Assert.That(GameRules.LegalEffectTargets(cat,state,0),Does.Not.Contain("hero:3"));state.Players[0].RangeBonus=1;Assert.That(GameRules.LegalEffectTargets(cat,state,0),Does.Contain("hero:3"));}
  [Test] public void FrozenBashWindowKeepsOldCapabilities()
  {var cat=BattlefieldTests.Catalog();string s=File.ReadAllText(Path.Combine(ContentTests.Root(),"tests/fixtures/engine33-bash-discard.json"));var g=LocalGameFactory.Restore(cat,s);Assert.That(g.ExportSave(),Is.EqualTo(s));Assert.That(g.View(0).SupportedPrimaryCards,Does.Not.Contain(Support));Apply(g,1,CommandKind.ForcedDiscard,g.View(1).ForcedDiscardCards.First());ChargeTests.Restore(cat,g);}
 }
}
