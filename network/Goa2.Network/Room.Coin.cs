using System.Text.Json;
using Goa2.Domain;
namespace Goa2.Network;

public sealed partial class Room
{
    private CoinMotion? motion;
    private readonly HashSet<string> acceptedOpeningCommands=new();
    private DateTime motionReceived;
    private CoinMotion? CurrentMotion()
    {
        var opening=session.View(0).Opening;
        return opening!=null && motion?.TossId==opening.TossId ? motion:null;
    }
    private void BroadcastPresence()
    {
        int[] seats=Enumerable.Range(0,4).Where(i=>peers[i]!=null && !peers[i]!.Lifetime.IsCancellationRequested).ToArray();
        for(int i=0;i<4;i++) peers[i]?.Send(new {Type="Presence",Generation=generations[i],ConnectedSeats=seats});
    }
    private void ReceiveMotion(Peer peer,JsonElement item)
    {
        Wire.Shape(item,"Type","Frame");
        var body=item.GetProperty("Frame");
        Wire.Shape(body,"TossId","Sequence","Time","Position","Rotation","Velocity","AngularVelocity");
        var opening=session.View(peer.Seat).Opening;
        if(opening==null || opening.HostSeat!=peer.Seat || opening.Status!="throwing" || Wire.Text(body,"TossId")!=opening.TossId) throw new WireError("stale_coin_motion");
        CoinMotion frame;
        try{frame=JsonSerializer.Deserialize<CoinMotion>(body.GetRawText(),Wire.Json) ?? throw new WireError("invalid_coin_motion");}
        catch(JsonException){throw new WireError("invalid_coin_motion");}
        static bool Vector(float[] v,int length,float limit)=>v!=null && v.Length==length && v.All(x=>float.IsFinite(x) && Math.Abs(x)<=limit);
        if(!Vector(frame.Position,3,30) || frame.Position[1]<-.5 || !Vector(frame.Rotation,4,1.001f) ||
           Math.Abs(frame.Rotation.Sum(x=>x*x)-1)>.03 || !Vector(frame.Velocity,3,40) || !Vector(frame.AngularVelocity,3,100) ||
           !float.IsFinite(frame.Time) || frame.Time<0 || frame.Time>60 || frame.Sequence<1) throw new WireError("invalid_coin_motion");
        var previous=CurrentMotion();var now=DateTime.UtcNow;
        if(previous!=null && (frame.Sequence<=previous.Sequence || frame.Time<previous.Time ||
            frame.Time-previous.Time>(now-motionReceived).TotalSeconds+.75)) throw new WireError("stale_coin_motion");
        motion=frame;motionReceived=now;
        for(int i=0;i<4;i++) if(i!=peer.Seat) peers[i]?.Send(new {Type="CoinMotion",Generation=generations[i],Frame=frame});
    }
    private void ValidateOpeningIntent(Command command)
    {
        if(acceptedOpeningCommands.Contains(command.Id))return; // Core still compares the full command fingerprint.
        if(command.Kind==CommandKind.VoteCoinReroll && peers.Any(p=>p==null || p.Lifetime.IsCancellationRequested)) throw new WireError("waiting_for_four_players");
        if(command.Kind!=CommandKind.ReportCoinToss && command.Kind!=CommandKind.MarkCoinStuck)return;
        var opening=session.View(command.ActorSeat).Opening;
        // Core validates identity and toss ID as well. Transport additionally binds results to the latest motion.
        if(opening==null || opening.HostSeat!=command.ActorSeat) throw new WireError("host_only");
        var frame=CurrentMotion();
        if(frame==null || frame.Time<.5 || (DateTime.UtcNow-motionReceived).TotalSeconds>5) throw new WireError("coin_motion_required");
        if(command.Kind==CommandKind.MarkCoinStuck)
        { if(frame.Time<12)throw new WireError("coin_not_stuck");return; }
        var parts=command.Value.Split('|');
        if(parts.Length!=3)throw new WireError("invalid_coin_pose");
        var values=parts[2].Split(',');
        if(values.Length!=4)throw new WireError("invalid_coin_pose");
        for(int i=0;i<4;i++)
            if(!float.TryParse(values[i],System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out float v) ||
               !float.IsFinite(v) || Math.Abs(v-frame.Rotation[i])>.01)throw new WireError("coin_pose_mismatch");
        if(frame.Velocity.Sum(x=>x*x)>.04 || frame.AngularVelocity.Sum(x=>x*x)>.04 || frame.Position[1]>.45)throw new WireError("coin_not_settled");
    }
}
