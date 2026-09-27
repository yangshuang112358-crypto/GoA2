using System.Linq;
using Goa2.Domain;
using Goa2.Infrastructure;
using Goa2.Application;
using NUnit.Framework;
using static Goa2.Tests.TurnFlowTests;
using static Goa2.Tests.SessionTests;
namespace Goa2.Tests {
 public sealed class CancelSelectionTests {
  [Test] public void CancelIsPrivateIdempotentAndReplays() {
   var g=Planning();Apply(g,0,CommandKind.SelectCard,"hero0-gold");var cmd=Cmd(g,0,CommandKind.CancelCardSelection);
   Assert.That(g.Execute(0,cmd).Accepted,Is.True);Assert.That(g.Execute(0,cmd).Duplicate,Is.True);
   Assert.That(g.View(0).OwnCards.Any(c=>c.Zone==CardZone.Selected),Is.False);
   Assert.That(g.View(1).Events.Any(e=>e.Kind=="CardSelectionCancelled"),Is.False);
   var s=g.ExportSave();Assert.That(LocalGameFactory.Restore(FoundationTests.Fixture(),s).ExportSave(),Is.EqualTo(s));
  }
  [Test] public void ConfirmedChoiceCannotBeCancelled() {
   var g=Planning();Apply(g,0,CommandKind.SelectCard,"hero0-gold");Apply(g,0,CommandKind.ConfirmCard);string s=g.ExportSave();
   Assert.That(g.Execute(0,Cmd(g,0,CommandKind.CancelCardSelection)).Accepted,Is.False);Assert.That(g.ExportSave(),Is.EqualTo(s));
  }
  [Test] public void CancelPreventsQuickRevealUntilSeatSelectsAgain() {
   var g=Planning();var codec=new JsonStateCodec();var state=codec.Read(g.ExportSave());state.QuickSelection=true;g=new GameSession(FoundationTests.Fixture(),codec,state);Apply(g,0,CommandKind.SelectCard,"hero0-gold");Apply(g,0,CommandKind.CancelCardSelection);
   for(int i=1;i<4;i++)Apply(g,i,CommandKind.SelectCard,"hero"+i+"-gold");Assert.That(g.View(0).Phase,Is.EqualTo(Phase.Planning));
   Apply(g,0,CommandKind.SelectCard,"hero0-red");Assert.That(g.View(0).Phase,Is.Not.EqualTo(Phase.Planning));
  }
 }
}