#nullable enable
using System.Collections.Generic;
using System.Linq;
using Goa2.Domain;
namespace Goa2.Presentation.UI3D {
 public sealed class SkillWheelState {
  public sealed class Motion {public float Press,Flip,Hover,Velocity;public bool Ready;}
  public sealed class DiscardBeat {public int Seat;public string Color="";public float Start;}
  public readonly Dictionary<int,Hex> LastPositions=new Dictionary<int,Hex>();
  public readonly Dictionary<string,Motion> Motions=new Dictionary<string,Motion>();
  public readonly Queue<DiscardBeat> Discards=new Queue<DiscardBeat>();
  private string match="";private long revision,sequence;private bool ready;
  public bool Observe(GameView view,float now) {
   bool reset=!ready || match!=view.MatchId || view.Revision<revision;
   if(reset){LastPositions.Clear();Motions.Clear();Discards.Clear();sequence=0;match=view.MatchId;ready=true;}
   foreach(var e in view.Events.Where(e=>e.Sequence>sequence && e.Kind=="DiscardColorShown" && e.Seat.HasValue).OrderBy(e=>e.Sequence))
    if(!reset)Discards.Enqueue(new DiscardBeat{Seat=e.Seat!.Value,Color=e.Detail,Start=Discards.Count==0 ? now : Discards.Last().Start+2.4f});
   foreach(var unit in view.Units.Where(u=>u.Seat.HasValue))LastPositions[unit.Seat!.Value]=unit.Position;
   revision=view.Revision;sequence=System.Math.Max(sequence,view.Events.Select(e=>e.Sequence).DefaultIfEmpty(0).Max());return reset;
  }
  public Motion Get(int seat,string color) {string key=seat+":"+color;if(!Motions.TryGetValue(key,out var m)){m=new Motion();Motions[key]=m;}return m;}
  public static CardInstance? KnownCard(ContentCatalog catalog,GameView view,int viewer,int target,string color) {
   var player=view.Players.Single(p=>p.Seat==target);
   return (viewer==target ? view.OwnCards : player.PublicDiscards.Concat(player.Revealed)).FirstOrDefault(c=>catalog.Card(c.CardId).Color==color);
  }
 }
}
