using System.Linq;
using Goa2.Application;
using Goa2.Domain;
using Goa2.Infrastructure;
using Goa2.Rules;
using NUnit.Framework;
namespace Goa2.Tests {
 public sealed class PrimaryAvailabilityTests {
  [Test] public void NoTargetAttackIsDisabledOnlyInPresentationAndViewIsReadOnly() {
   var c=BattlefieldTests.Catalog();var g=ShiningBladeTests.Setup(c,false);var codec=new JsonStateCodec();var s=codec.Read(g.ExportSave());
   s.Units.RemoveAll(u=>u.Team!=s.Players[0].Team);g=new GameSession(c,codec,s);string before=g.ExportSave();
   Assert.That(g.View(0).CanBeginPrimary,Is.True);Assert.That(g.View(0).PrimaryImmediatelySkips,Is.True);Assert.That(g.ExportSave(),Is.EqualTo(before));
  }
  [Test] public void ShargathaNoTargetAndHerPreludeAreDistinguished() {
   var c=BattlefieldTests.Catalog();var g=ShiningBladeTests.Setup(c,false);var s=new JsonStateCodec().Read(g.ExportSave());
   s.Players[0].HeroId="shargatha";s.Players[0].PurpleCardId="shargatha-12-幻化";s.Players[0].Cards.Single(x=>x.Zone==CardZone.PlayedUnresolved).CardId="shargatha-00-反击";
   Assert.That(CombatRules.PrimaryImmediatelySkips(c,s,0),Is.False);
   s.Units.RemoveAll(u=>u.Team!=s.Players[0].Team);Assert.That(CombatRules.PrimaryImmediatelySkips(c,s,0),Is.True);
  }
  [Test] public void ExistingTargetKeepsAttackEnabled() {var c=BattlefieldTests.Catalog();var g=ShiningBladeTests.Setup(c,false);Assert.That(g.View(0).PrimaryImmediatelySkips,Is.False);}
  [Test] public void PreAttackMovementAndOptionalDiscardAreNotMistakenForNoOp() {
   var c=BattlefieldTests.Catalog();var g=ShiningBladeTests.Setup(c,false);var s=new JsonStateCodec().Read(g.ExportSave());s.Units.RemoveAll(u=>u.Team!=s.Players[0].Team);
   foreach(var card in c.Cards.Where(c=>new[]{"tigerclaw-00-瞬闪打击","brogan-03-奋勇冲锋","brogan-05-勇往直前","brogan-02-投掷飞斧"}.Contains(c.Id))) {
    s.Players[0].Cards.Single(x=>x.Zone==CardZone.PlayedUnresolved).CardId=card.Id;
    Assert.That(CombatRules.PrimaryImmediatelySkips(c,s,0),Is.False,card.Id);
   }
  }
 }
}