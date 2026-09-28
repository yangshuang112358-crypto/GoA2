using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Goa2.Infrastructure;

namespace Goa2.Network;

public static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if ((args.Length != 3 && args.Length != 4) || args[0] != "serve")
        {
            Console.Error.WriteLine("Usage: Goa2.Network serve <repository-root> <new-private-output-directory> [local-LAN-IPv4]"); return 2;
        }
        string root = Path.GetFullPath(args[1]);
        var content = ContentLoader.LoadDirectory(root);
        return await RunServer(new Room(content, Guid.NewGuid().ToString("N")), root, args[2], args.Length==4 ? args[3] : "127.0.0.1");
    }
    internal static async Task<int> RunServer(Room room, string root, string outputDirectory, string host="127.0.0.1")
    {
        var address=IPAddress.Parse(host);var bytes=address.GetAddressBytes();
        if(address.AddressFamily!=AddressFamily.InterNetwork || !(IPAddress.IsLoopback(address) || bytes[0]==10 || bytes[0]==192 && bytes[1]==168 || bytes[0]==172 && bytes[1]>=16 && bytes[1]<=31)) throw new ArgumentException("Bind must be a local private IPv4 address.");
        string output = Path.GetFullPath(outputDirectory);
        if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Output must be new.");
        Directory.CreateDirectory(output);
        var listener = new TcpListener(address, 0); listener.Start(32);
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        // These files are local bearer secrets, not public logs. Give each player only their own file.
        for (int seat = 0; seat < 4; seat++)
            File.WriteAllBytes(Path.Combine(output, $"seat-{seat}.private.json"), Wire.Encode(new { Host = host, Port = port,
                Type = "Hello", RoomId = room.Id, Credential = room.Credentials[seat], room.Capabilities }));
        File.WriteAllBytes(Path.Combine(output, "ready.json"), Wire.Encode(new { Port = port, ProcessId = Environment.ProcessId, RoomId = room.Id }));
        Console.WriteLine(JsonSerializer.Serialize(new { Type = "Ready", Port = port, ProcessId = Environment.ProcessId }));
        var sessions = new List<Task>();
        using var stop = new CancellationTokenSource();
        // Local operator stdin controls shutdown/export; clients have no equivalent message.
        _ = Task.Run(async () => { while (await Console.In.ReadLineAsync() is string line) if (line == "stop") { stop.Cancel(); break; } });
        _ = Task.Run(async()=>{try{while(!stop.IsCancellationRequested){if(File.Exists(Path.Combine(output,"stop.request"))){stop.Cancel();break;}await Task.Delay(250,stop.Token);}}catch(OperationCanceledException){}});
        using var slots = new SemaphoreSlim(32);
        try
        {
            while (!stop.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(stop.Token);
                if (!slots.Wait(0)) { client.Dispose(); continue; }
                sessions.RemoveAll(t => t.IsCompleted);
                sessions.Add(Serve(client, room, slots, stop.Token));
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            listener.Stop(); stop.Cancel(); await Task.WhenAll(sessions);
            string saved = room.ExportForVerification();
            // Validate using existing Restore before calling the export usable.
            _ = LocalGameFactory.Restore(ContentLoader.LoadDirectory(root), saved);
            File.WriteAllText(Path.Combine(output, "authority.private.save.json"), saved);
            File.WriteAllText(Path.Combine(output, "restore-check.json"), "{\"passed\":true}");
        }
        return 0;
    }
    private static async Task Serve(TcpClient client, Room room, SemaphoreSlim slots, CancellationToken stop)
    {
        using (client)
        using (var peer = new Peer())
        using (var life = CancellationTokenSource.CreateLinkedTokenSource(stop, peer.Lifetime.Token))
        {
            client.NoDelay = true; var stream = client.GetStream();
            var writer = Task.Run(async () =>
            {
                try
                {
                    await foreach (var bytes in peer.Outbound.Reader.ReadAllAsync(life.Token))
                    {
                        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(life.Token); deadline.CancelAfter(TimeSpan.FromSeconds(10));
                        await Wire.Write(stream, bytes, deadline.Token);
                    }
                }
                catch (Exception e) when (e is IOException or OperationCanceledException or SocketException or WireError) { peer.Dispose(); }
            });
            try
            {
                using (var handshake = CancellationTokenSource.CreateLinkedTokenSource(life.Token))
                {
                    handshake.CancelAfter(TimeSpan.FromSeconds(10));
                    room.Authenticate(peer, Wire.Parse(await Wire.Read(stream, Wire.MaxRequest, handshake.Token)));
                }
                while (!life.IsCancellationRequested)
                {
                    var body = await Wire.Read(stream, Wire.MaxRequest, life.Token);
                    JsonElement? parsed = null;
                    try { parsed = Wire.Parse(body); room.Execute(peer, parsed.Value); }
                    catch (WireError error)
                    {
                        string? id = null;
                        if (parsed.HasValue && parsed.Value.TryGetProperty("CommandId", out var commandId) && commandId.ValueKind == JsonValueKind.String)
                        { string candidate = commandId.GetString()!; if (candidate.Length <= 100) id = candidate; }
                        peer.Send(new { Type = "Error", Code = error.Message, CommandId = id });
                    }
                }
            }
            catch (WireError error)
            {
                peer.Send(new { Type = "Error", Code = error.Message });
                peer.Outbound.Writer.TryComplete();
                try { await writer.WaitAsync(TimeSpan.FromSeconds(2)); } catch (TimeoutException) { }
            }
            catch (Exception e) when (e is IOException or OperationCanceledException or SocketException) { }
            finally { room.Detach(peer); await writer; slots.Release(); }
        }
    }
}
