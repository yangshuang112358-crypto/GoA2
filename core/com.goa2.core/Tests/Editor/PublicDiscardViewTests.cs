using System.Linq;
using Goa2.Application;
using Goa2.Domain;
using Goa2.Infrastructure;
using Goa2.Rules;
using NUnit.Framework;
namespace Goa2.Tests {
 public sealed class PublicDiscardViewTests {
  [TestCase(0)] [TestCase(1)] [TestCase(null)]
  public void DiscardProjectionRevealsOnlyActualPileAndCannotMutateAuthority(int? viewer) {
   var c=FoundationTests.Fixture();var s=new GameRules().Create(c,"discard-view",new[]{"A","B","C","D"},42);
   s.Players[0].HeroId="hero0";
   s.Players[0].Cards.Add(new CardInstance{CardId="hero0-gold",Zone=CardZone.Discarded});
   s.Players[0].Cards.Add(new CardInstance{CardId="hero0-red",Zone=CardZone.Selected});
   s.Players[0].Cards.Add(new CardInstance{CardId="hero0-blue",Zone=CardZone.InHand});
   var session=new GameSession(c,new JsonStateCodec(),s);var v=session.View(viewer);
   Assert.That(v.Players[0].PublicDiscards.Select(x=>x.CardId),Is.EqualTo(new[]{"hero0-gold"}));
   if(viewer!=0)Assert.That(v.OwnCards.Any(x=>x.CardId=="hero0-red" || x.CardId=="hero0-blue"),Is.False);
   string before=session.ExportSave();v.Players[0].PublicDiscards[0].CardId="changed";v.Players[0].PublicDiscards.Clear();Assert.That(session.ExportSave(),Is.EqualTo(before));
  }
 }
}
