using System.Security.Cryptography;
using System.Threading.Channels;
using Goa2.Application;
using Goa2.Domain;
using Goa2.Infrastructure;
using System.Text.Json;

namespace Goa2.Network;

public sealed class Peer : IDisposable
{
    public readonly Channel<byte[]> Outbound = Channel.CreateBounded<byte[]>(32);
    public readonly CancellationTokenSource Lifetime = new();
    public int Seat = -1;
    public long Generation;
    public void Send(object message)
    {
        if (!Outbound.Writer.TryWrite(Wire.Encode(message))) Dispose();
    }
    public void Dispose() { Lifetime.Cancel(); Outbound.Writer.TryComplete(); }
}

public sealed class Room
{
    private readonly object gate = new();
    private readonly GameSession session;
    private readonly Peer?[] peers = new Peer?[4];
    private readonly long[] generations = new long[4];
    public string Id { get; }
    public string[] Credentials { get; } = Enumerable.Range(0, 4).Select(_ => Convert.ToHexString(RandomNumberGenerator.GetBytes(32))).ToArray();
    public object Capabilities { get; }
    private readonly ContentCatalog catalog;
    public Room(ContentCatalog content, string id) : this(content,
        LocalGameFactory.Create(content, id, ["Player 1", "Player 2", "Player 3", "Player 4"], 1729)) { }
    internal Room(ContentCatalog content, GameSession initial)
    {
        catalog = content; session = initial; Id = initial.View(0).MatchId;
        Capabilities = new { WireVersion = Wire.Version, ProtocolVersion = GameState.CurrentProtocol,
            EngineVersion = GameState.CurrentEngineVersion, ContentHash = catalog.Hash, ContentVersion = catalog.Version, RulesVersion = catalog.Rules.Version };
    }
    public void Authenticate(Peer peer, JsonElement hello)
    {
        lock (gate)
        {
            string type = Wire.Text(hello, "Type");
            if (type != "Hello" && type != "Resume") throw new WireError("authentication_required");
            Wire.Shape(hello, "Type", "RoomId", "Credential", "WireVersion", "ProtocolVersion", "EngineVersion", "ContentHash", "ContentVersion", "RulesVersion", "LastRevision?");
            if (hello.TryGetProperty("LastRevision", out _)) _ = Wire.Number(hello, "LastRevision", 0, long.MaxValue);
            if (Wire.Number(hello, "WireVersion", 0, int.MaxValue) != Wire.Version ||
                Wire.Text(hello, "ProtocolVersion") != GameState.CurrentProtocol ||
                Wire.Number(hello, "EngineVersion", 0, int.MaxValue) != GameState.CurrentEngineVersion ||
                Wire.Text(hello, "ContentHash") != catalog.Hash || Wire.Text(hello, "ContentVersion") != catalog.Version ||
                Wire.Text(hello, "RulesVersion") != catalog.Rules.Version) throw new WireError("version_mismatch");
            if (Wire.Text(hello, "RoomId") != Id) throw new WireError("invalid_credentials");
            string credential = Wire.Text(hello, "Credential", 64);
            int seat = Array.FindIndex(Credentials, c => CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(c), System.Text.Encoding.UTF8.GetBytes(credential)));
            if (seat < 0) throw new WireError("invalid_credentials");
            peers[seat]?.Dispose();
            peer.Seat = seat; peer.Generation = ++generations[seat]; peers[seat] = peer;
            peer.Send(new { Type = "Welcome", RoomId = Id, Seat = seat, Generation = peer.Generation, Capabilities, Snapshot = View(seat) });
        }
    }
    private GameView View(int seat)
    {
        var view = session.View(seat);
        // Transport privacy narrowing only; never compute or add candidates here.
        // Core currently projects Pending candidate metadata to every seat.
        if (view.Pending != null && view.Pending.ChooserSeat != seat)
        {
            view.Pending.CandidateCells.Clear(); view.Pending.CandidateSeats.Clear(); view.Pending.CandidateUnits.Clear();
        }
        return view;
    }
    public void Execute(Peer peer, JsonElement item)
    {
        lock (gate)
        {
            if (peer.Seat < 0 || !ReferenceEquals(peers[peer.Seat], peer) || generations[peer.Seat] != peer.Generation)
                throw new WireError("connection_replaced");
            var command = Wire.Intent(item, peer.Seat);
            var result = session.Execute(peer.Seat, command);
            peer.Send(new { Type = "Result", CommandId = command.Id, result.Accepted, result.Duplicate, result.Code, result.Message,
                Generation = peer.Generation, Snapshot = View(peer.Seat) });
            if (result.Accepted && !result.Duplicate)
                for (int seat = 0; seat < 4; seat++)
                    peers[seat]?.Send(new { Type = "Snapshot", Generation = generations[seat], Snapshot = View(seat) });
            Console.WriteLine(JsonSerializer.Serialize(new { Type = "Command", Seat = peer.Seat, command.Id, Kind = command.Kind.ToString(),
                result.Code, result.Accepted, result.Duplicate, Revision = result.View.Revision }));
        }
    }
    public void Detach(Peer peer)
    {
        lock (gate) if (peer.Seat >= 0 && ReferenceEquals(peers[peer.Seat], peer)) peers[peer.Seat] = null;
        peer.Dispose();
    }
    // Operator-only evidence export. Never exposed through the network protocol.
    public string ExportForVerification() { lock (gate) return session.ExportSave(); }
}
