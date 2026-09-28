using System;
using Goa2.Domain;
using Goa2.Network.Client;
using NUnit.Framework;
namespace Goa2.Presentation.Tests {
 public class NetworkIntentTests {
  [TestCase(CommandKind.CancelCardSelection)] [TestCase(CommandKind.ConfirmCard)] [TestCase(CommandKind.BeginPrimary)]
  public void NoArgumentCommandsDropLegacyDefaults(CommandKind kind){var p=UiIntent.Create(kind,"",-1,new Hex(3,4),MoveMode.Fast);Assert.IsNull(p.Value);Assert.IsNull(p.Destination);Assert.IsNull(p.TargetSeat);Assert.IsNull(p.MoveMode);}
  [TestCase(CommandKind.DebugPrepare)] [TestCase(CommandKind.SetQuickSelection)] [TestCase(CommandKind.UpgradeEngine)]
  public void AuthorityOperationsCannotBeSent(CommandKind kind){Assert.Throws<InvalidOperationException>(()=>UiIntent.Create(kind,"",0,default,MoveMode.Secondary));}
  [Test] public void BeforeMoveDoesNotSendPrematureDestination(){var p=UiIntent.Create(CommandKind.Move,"begin",-1,new Hex(3,4),MoveMode.Fast);Assert.IsNull(p.Destination);Assert.AreEqual("begin",p.Value);Assert.AreEqual(MoveMode.Fast,p.MoveMode);}
  [Test] public void MoveKeepsChosenDestinationAndMode(){var p=UiIntent.Create(CommandKind.Move,"",-1,new Hex(3,4),MoveMode.Secondary);Assert.AreEqual(new Hex(3,4),p.Destination);Assert.IsNull(p.Value);}
  [Test] public void EffectSkipDoesNotSendDestination(){var p=UiIntent.Create(CommandKind.ChooseEffectMove,"skip",-1,new Hex(3,4),MoveMode.Secondary);Assert.IsNull(p.Destination);}
  [Test] public void GoldTransferRetainsNoTargetSentinel(){var p=UiIntent.Create(CommandKind.ChooseGoldTransfer,"0",-1,default,MoveMode.Secondary);Assert.AreEqual(-1,p.TargetSeat);Assert.AreEqual("0",p.Value);}
  [TestCase("127.0.0.1")] [TestCase("192.168.1.10")] [TestCase("10.1.2.3")] [TestCase("172.16.0.1")]
  public void LocalAndLanTicketsSupported(string host){using(var client=new NetworkPlayerSession("{\"Host\":\""+host+"\"}")){Assert.AreEqual(ConnectionState.Disconnected,client.Connection);Assert.IsNull(client.AuthenticatedSeat);Assert.IsNull(client.View);}}
  [TestCase("8.8.8.8")] [TestCase("0.0.0.0")] [TestCase("example.com")]
  public void PublicEndpointsAreNotAdvertisedAsSupported(string host){Assert.Throws<ArgumentException>(()=>new NetworkPlayerSession("{\"Host\":\""+host+"\"}"));}
 }
}
