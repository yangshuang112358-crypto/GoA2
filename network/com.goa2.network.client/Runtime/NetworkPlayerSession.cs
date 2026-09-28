#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Goa2.Domain;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Goa2.Network.Client
{
    // Domain DTOs + Newtonsoft only. No GameSession, save codec, rules or UnityEngine.
    public sealed class NetworkPlayerSession : IPlayerSession
    {
        private readonly object gate = new object();
        private readonly SemaphoreSlim sending = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim connecting = new SemaphoreSlim(1, 1);
        private readonly SynchronizationContext context;
        private readonly JObject ticket;
        private readonly Dictionary<string, Pending> pending = new Dictionary<string, Pending>();
        private TcpClient? socket;
        private CancellationTokenSource? lifetime;
        private TaskCompletionSource<bool>? welcome;
        private GameView? view;
        private int? seat;
        private long epoch, generation;
        private bool disposed;
        private ConnectionState connection;
        public int MaxResponseBytes { get; private set; }
        public event Action? StateUpdated;
        private sealed class Pending
        {
            public string Json = "";
            public TaskCompletionSource<IntentResult> Completion = NewCompletion<IntentResult>();
        }
        private static TaskCompletionSource<T> NewCompletion<T>() => new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        private static readonly JsonSerializerSettings Json = new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.None, MaxDepth = 48 };
        public NetworkPlayerSession(string privateTicketJson, SynchronizationContext? uiContext = null)
        {
            ticket = JObject.Parse(privateTicketJson);
            context = uiContext ?? SynchronizationContext.Current ?? new SynchronizationContext();
            if(!IPAddress.TryParse((string?)ticket["Host"],out var address) || address.AddressFamily!=AddressFamily.InterNetwork) throw new ArgumentException("A numeric IPv4 host is required.");
            var bytes=address.GetAddressBytes();
            if(!(IPAddress.IsLoopback(address) || bytes[0]==10 || bytes[0]==192 && bytes[1]==168 || bytes[0]==172 && bytes[1]>=16 && bytes[1]<=31)) throw new ArgumentException("Only local or private LAN endpoints are supported.");
        }
        public int? AuthenticatedSeat { get { lock (gate) return seat; } }
        public ConnectionState Connection { get { lock (gate) return connection; } }
        // Return detached DTOs so UI mutation cannot corrupt revision/identity tracking.
        public GameView? View { get { lock (gate) return view == null ? null : JsonConvert.DeserializeObject<GameView>(JsonConvert.SerializeObject(view, Json), Json); } }
        private void Notify() => context.Post(_ => StateUpdated?.Invoke(), null);
        public async Task ReconnectAsync()
        {
            await connecting.WaitAsync().ConfigureAwait(false);
            try
            {
                Disconnect();
                TcpClient current;
                CancellationToken token;
                long currentEpoch;
                JObject hello;
                Task<bool> authenticated;
                lock (gate)
                {
                    if (disposed) throw new ObjectDisposedException(nameof(NetworkPlayerSession));
                    current = socket = new TcpClient();
                    lifetime = new CancellationTokenSource(); token = lifetime.Token; currentEpoch = ++epoch;
                    welcome = NewCompletion<bool>(); authenticated = welcome.Task;
                    connection = ConnectionState.Connecting;
                    hello = (JObject)ticket["Capabilities"]!.DeepClone();
                    hello["Type"] = view == null ? "Hello" : "Resume";
                    hello["RoomId"] = ticket["RoomId"]; hello["Credential"] = ticket["Credential"];
                    if (view != null) hello["LastRevision"] = view.Revision;
                }
                Notify();
                try
                {
                    var connect = current.ConnectAsync((string)ticket["Host"]!, (int)ticket["Port"]!);
                    if (await Task.WhenAny(connect, Task.Delay(10000, token)).ConfigureAwait(false) != connect)
                        throw new IOException("Connection timed out.");
                    await connect.ConfigureAwait(false);
                    current.NoDelay = true;
                    _ = ReadLoop(current, currentEpoch, token);
                    await Send(hello.ToString(Formatting.None), currentEpoch, token).ConfigureAwait(false);
                    if (await Task.WhenAny(authenticated, Task.Delay(10000, token)).ConfigureAwait(false) != authenticated)
                        throw new IOException("Authentication timed out.");
                    await authenticated.ConfigureAwait(false);
                }
                catch { DisconnectEpoch(currentEpoch); throw; }
            }
            finally { connecting.Release(); }
        }
        public Task<IntentResult> SubmitAsync(PlayerIntent intent)
        {
            string id = Guid.NewGuid().ToString("N");
            lock (gate)
            {
                if (connection != ConnectionState.Connected || view == null) throw new InvalidOperationException("Disconnected: submission disabled.");
                var json = new JObject { ["Type"] = "Intent", ["CommandId"] = id, ["MatchId"] = view.MatchId,
                    ["ExpectedRevision"] = view.Revision, ["Kind"] = intent.Kind.ToString() };
                if (intent.Value != null) json["Value"] = intent.Value;
                if (intent.TargetSeat.HasValue) json["TargetSeat"] = intent.TargetSeat.Value;
                if (intent.Destination.HasValue) json["Destination"] = JObject.FromObject(intent.Destination.Value);
                if (intent.MoveMode.HasValue) json["MoveMode"] = intent.MoveMode.Value.ToString();
                string body = json.ToString(Formatting.None);
                if (Encoding.UTF8.GetByteCount(body) > 16384) throw new ArgumentException("Request exceeds NET-01 limit.");
                pending.Add(id, new Pending { Json = body });
            }
            return SendPending(id);
        }
        public Task<IntentResult> RetryAsync(string commandId)
        {
            lock (gate)
            {
                if (connection != ConnectionState.Connected) throw new InvalidOperationException("Disconnected: retry disabled.");
                if (!pending.TryGetValue(commandId, out var original)) throw new ArgumentException("No unresolved command with that ID.");
                if (!original.Completion.Task.IsCompleted) throw new InvalidOperationException("Command already awaiting a result.");
                original.Completion = NewCompletion<IntentResult>();
            }
            return SendPending(commandId);
        }
        private async Task<IntentResult> SendPending(string id)
        {
            Pending item; long currentEpoch; CancellationToken token;
            lock (gate) { item = pending[id]; currentEpoch = epoch; token = lifetime?.Token ?? new CancellationToken(true); }
            try { await Send(item.Json, currentEpoch, token).ConfigureAwait(false); }
            catch (Exception e) when (e is IOException || e is SocketException || e is OperationCanceledException || e is ObjectDisposedException)
            { DisconnectEpoch(currentEpoch); }
            // A silent transport is uncertain, never implicitly accepted or retried with a new ID.
            if (await Task.WhenAny(item.Completion.Task, Task.Delay(15000)).ConfigureAwait(false) != item.Completion.Task)
                DisconnectEpoch(currentEpoch);
            return await item.Completion.Task.ConfigureAwait(false);
        }
        private async Task Send(string json, long currentEpoch, CancellationToken token)
        {
            byte[] body = Encoding.UTF8.GetBytes(json);
            if (body.Length > 16384) throw new ArgumentException("Request exceeds NET-01 limit.");
            byte[] header = { (byte)(body.Length >> 24), (byte)(body.Length >> 16), (byte)(body.Length >> 8), (byte)body.Length };
            await sending.WaitAsync(token).ConfigureAwait(false);
            try
            {
                NetworkStream stream;
                lock (gate)
                {
                    if (epoch != currentEpoch || socket == null) throw new IOException("Connection replaced.");
                    stream = socket.GetStream();
                }
                await stream.WriteAsync(header, 0, 4, token).ConfigureAwait(false);
                await stream.WriteAsync(body, 0, body.Length, token).ConfigureAwait(false);
            }
            finally { sending.Release(); }
        }
        private static async Task<byte[]> Exact(NetworkStream stream, int count, CancellationToken token)
        {
            var data = new byte[count]; int read = 0;
            while (read < count)
            {
                int n = await stream.ReadAsync(data, read, count - read, token).ConfigureAwait(false);
                if (n == 0) throw new EndOfStreamException(); read += n;
            }
            return data;
        }
        private async Task ReadLoop(TcpClient current, long currentEpoch, CancellationToken token)
        {
            try
            {
                var stream = current.GetStream();
                while (!token.IsCancellationRequested)
                {
                    byte[] header = await Exact(stream, 4, token).ConfigureAwait(false);
                    int count = header[0] << 24 | header[1] << 16 | header[2] << 8 | header[3];
                    if (count <= 0 || count > 8 * 1024 * 1024) throw new IOException("Invalid response size.");
                    MaxResponseBytes = Math.Max(MaxResponseBytes, count);
                    var message = JObject.Parse(Encoding.UTF8.GetString(await Exact(stream, count, token).ConfigureAwait(false)));
                    bool changed = false;
                    lock (gate)
                    {
                        if (epoch != currentEpoch) return;
                        string type = (string)message["Type"]!;
                        if (type == "Welcome")
                        {
                            int assigned = (int)message["Seat"]!;
                            if (assigned < 0 || assigned > 3 || seat.HasValue && seat != assigned) throw new IOException("Identity changed.");
                            seat = assigned; generation = (long)message["Generation"]!;
                            connection = ConnectionState.Connected; changed = true;
                        }
                        else if (message["Generation"] != null && (long)message["Generation"]! != generation) continue;
                        if (message["Snapshot"] is JObject projection)
                        {
                            var next = projection.ToObject<GameView>(JsonSerializer.Create(Json))!;
                            if (view == null || next.MatchId == view.MatchId && next.Revision > view.Revision) { view = next; changed = true; }
                        }
                        if (type == "Welcome") welcome?.TrySetResult(true);
                        if (type == "Result" || type == "Error")
                        {
                            string? id = (string?)message["CommandId"];
                            if (id != null && pending.TryGetValue(id, out var item))
                            {
                                pending.Remove(id);
                                item.Completion.TrySetResult(new IntentResult { CommandId = id, Accepted = (bool?)message["Accepted"] ?? false,
                                    Duplicate = (bool?)message["Duplicate"] ?? false, Code = (string?)message["Code"] ?? "protocol_error",
                                    Message = (string?)message["Message"] ?? "" });
                            }
                            else if (type == "Error" && connection == ConnectionState.Connecting)
                                welcome?.TrySetException(new IOException((string?)message["Code"]));
                        }
                    }
                    if (changed) Notify();
                }
            }
            catch (Exception e) when (e is IOException || e is SocketException || e is OperationCanceledException || e is JsonException || e is ObjectDisposedException) { }
            finally { DisconnectEpoch(currentEpoch); }
        }
        public void Disconnect() { lock (gate) DisconnectEpoch(epoch); }
        private void DisconnectEpoch(long currentEpoch)
        {
            lock (gate)
            {
                if (epoch != currentEpoch) return;
                ++epoch; lifetime?.Cancel(); socket?.Dispose(); socket = null;
                connection = ConnectionState.Disconnected;
                welcome?.TrySetException(new IOException("Disconnected."));
                foreach (var pair in pending)
                    pair.Value.Completion.TrySetResult(new IntentResult { CommandId = pair.Key, Uncertain = true, Code = "disconnected" });
            }
            Notify();
        }
        public void Dispose() { lock (gate) { disposed = true; DisconnectEpoch(epoch); } }
    }
}
