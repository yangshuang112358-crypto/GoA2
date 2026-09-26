using Goa2.Domain;
using Goa2.Network.Client;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

var settings = new JsonSerializerSettings { Converters = { new StringEnumConverter() } };
using var client = new NetworkPlayerSession(File.ReadAllText(args[0]));
using var log = new StreamWriter(new FileStream(args[1], FileMode.CreateNew));
async Task Wait(Func<bool> predicate)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    while (!predicate()) await Task.Delay(10, timeout.Token);
}
while (await Console.In.ReadLineAsync() is string line)
{
    try
    {
        var request = JObject.Parse(line); string op = (string)request["op"]!;
        object result;
        switch (op)
        {
            case "connect":
                await client.ReconnectAsync(); result = new { seat = client.AuthenticatedSeat }; break;
            case "disconnect":
                client.Disconnect(); result = new { state = client.Connection.ToString() }; break;
            case "state":
                if ((bool?)request["wait_disconnected"] == true) await Wait(() => client.Connection == ConnectionState.Disconnected);
                result = new { state = client.Connection.ToString() }; break;
            case "view":
                await Wait(() => client.View != null && client.View.Revision >= ((long?)request["revision"] ?? 0));
                result = client.View!; break;
            case "metrics": result = new { max_snapshot_bytes = client.MaxResponseBytes }; break;
            case "submit":
            case "retry":
                IntentResult submitted;
                if (op == "retry") submitted = await client.RetryAsync((string)request["command_id"]!);
                else
                {
                    var values = (JObject?)request["args"] ?? new JObject();
                    submitted = await client.SubmitAsync(new PlayerIntent(Enum.Parse<CommandKind>((string)request["kind"]!),
                        (string?)values["Value"], (int?)values["TargetSeat"], values["Destination"]?.ToObject<Hex>(),
                        values["MoveMode"] == null ? null : Enum.Parse<MoveMode>((string)values["MoveMode"]!)));
                }
                result = new { Type = submitted.Uncertain ? "Uncertain" : "Result", submitted.CommandId, submitted.Accepted,
                    submitted.Duplicate, submitted.Code, submitted.Message, Snapshot = client.View };
                log.WriteLine(JsonConvert.SerializeObject(new { Type = "Result", submitted.CommandId, submitted.Code, Revision = client.View?.Revision }));
                log.Flush();
                break;
            default: throw new ArgumentException("Unknown harness operation.");
        }
        Console.WriteLine(JsonConvert.SerializeObject(new { ok = true, result }, settings));
    }
    catch (Exception error) { Console.WriteLine(JsonConvert.SerializeObject(new { ok = false, error = error.Message })); }
}
