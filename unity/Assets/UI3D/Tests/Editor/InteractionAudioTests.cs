using System.Linq;
using Goa2.Domain;
using Goa2.Presentation.UI3D;
using NUnit.Framework;
namespace Goa2.UI3D.Tests
{
 public sealed class InteractionAudioTests
 {
  [Test] public void ClipsAreFiniteShortAndFadeAtBothEnds(){foreach(var cue in InteractionAudio.Cues){var samples=InteractionAudio.Samples(cue);Assert.That(samples.Length,Is.InRange(1000,44100));Assert.That(samples.All(x=>!float.IsNaN(x)&&!float.IsInfinity(x)&&System.Math.Abs(x)<=.7f));Assert.That(samples[0],Is.EqualTo(0));Assert.That(System.Math.Abs(samples.Last()),Is.LessThan(.001));Assert.That(samples.Any(x=>System.Math.Abs(x)>.01));}}
  [Test] public void LoadAndRefreshDoNotReplayResponse(){var state=new InteractionAudioState();var v=new GameView{MatchId="a",Pending=new PendingChoice{Id="p",ChooserSeat=0}};Assert.That(state.Observe(v,0),Is.Empty);v.Revision++;Assert.That(state.Observe(v,0),Is.Empty);}
  [Test] public void NewOwnResponsePlaysOnce(){var state=new InteractionAudioState();var v=new GameView{MatchId="a"};state.Observe(v,0);v.Pending=new PendingChoice{Id="p",ChooserSeat=0};Assert.That(state.Observe(v,0),Is.EqualTo("response"));Assert.That(state.Observe(v,0),Is.Empty);v.Pending.Id="q";Assert.That(state.Observe(v,0),Is.EqualTo("response"));}
  [Test] public void OtherSeatsResponseDoesNotPromptLocalPlayer(){var state=new InteractionAudioState();var v=new GameView{MatchId="a"};state.Observe(v,0);v.Pending=new PendingChoice{Id="p",ChooserSeat=1};Assert.That(state.Observe(v,0),Is.Empty);Assert.That(state.Observe(v,1),Is.Empty);}
  [Test] public void HiddenSelectionRefreshDoesNotMakeSound(){var state=new InteractionAudioState();var v=new GameView{MatchId="a",Phase=Phase.Planning};state.Observe(v,0);v.Revision++;Assert.That(state.Observe(v,0),Is.Empty);}
  [Test] public void NewMatchDoesNotReplayLastMatch(){var state=new InteractionAudioState();var v=new GameView{MatchId="a",Phase=Phase.Planning};state.Observe(v,0);v.MatchId="b";v.Phase=Phase.Action;v.ActiveSeat=0;Assert.That(state.Observe(v,0),Is.Empty);}
  [Test] public void OwnTurnAndVictoryAreAnnouncedOnce(){var state=new InteractionAudioState();var v=new GameView{MatchId="a",Phase=Phase.Planning};state.Observe(v,0);v.Phase=Phase.Action;v.ActiveSeat=0;Assert.That(state.Observe(v,0),Is.EqualTo("turn"));Assert.That(state.Observe(v,0),Is.Empty);v.Phase=Phase.Finished;Assert.That(state.Observe(v,0),Is.EqualTo("victory"));}
 }
}
