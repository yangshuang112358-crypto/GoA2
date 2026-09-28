using System.Collections.Generic;
using System.Linq;
using Goa2.Domain;
using NUnit.Framework;
using static Goa2.Tests.TurnFlowTests;
using static Goa2.Tests.SessionTests;
namespace Goa2.Tests {
 public sealed class PublicLoadoutTests {
  private static List<CardInstance> Cards(PlayerView p) {
   var field=typeof(PlayerView).GetField("PublicCards");
   Assert.That(field,Is.Not.Null,"Equipped skills must be public without revealing selection");
   return (List<CardInstance>)field.GetValue(p);
  }
  [Test] public void EquippedNamesArePublicButSelectionAndCancellationArePrivate() {
   var g=Planning();var before=Cards(g.View(1).Players[0]).Select(c=>c.CardId+":"+c.Zone).ToArray();
   Assert.That(before.Length,Is.EqualTo(5));
   Apply(g,0,CommandKind.SelectCard,"hero0-gold");
   foreach(int viewer in new[]{1,2,3,-1}) {
    Assert.That(Cards(g.View(viewer).Players[0]).Select(c=>c.CardId+":"+c.Zone),Is.EqualTo(before));
    Assert.That(g.View(viewer).Events.Any(e=>e.Kind=="CardSelected"),Is.False);
   }
   Assert.That(g.View(0).OwnCards.Single(c=>c.CardId=="hero0-gold").Zone,Is.EqualTo(CardZone.Selected));
   Apply(g,0,CommandKind.CancelCardSelection);
   Assert.That(Cards(g.View(1).Players[0]).Select(c=>c.CardId+":"+c.Zone),Is.EqualTo(before));
  }
  [Test] public void RevealedZonesRemainPublicAndViewsCannotMutateSession() {
   var g=Planning();for(int i=0;i<4;i++){Apply(g,i,CommandKind.SelectCard,"hero"+i+"-gold");Apply(g,i,CommandKind.ConfirmCard);}
   var cards=Cards(g.View(1).Players[0]);
   Assert.That(cards.Single(c=>c.CardId=="hero0-gold").Zone,Is.EqualTo(CardZone.PlayedUnresolved));
   string save=g.ExportSave();cards[0].CardId="tamper";cards[0].Zone=CardZone.Discarded;
   Assert.That(g.ExportSave(),Is.EqualTo(save));
  }
 }
}
