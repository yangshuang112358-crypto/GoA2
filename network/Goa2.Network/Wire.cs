using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using Goa2.Domain;

namespace Goa2.Network;

public sealed class WireError(string code) : Exception(code);

public static class Wire
{
    public const int Version = 1, MaxRequest = 16384, MaxResponse = 8 * 1024 * 1024;
    public static readonly JsonSerializerOptions Json = new()
    {
        IncludeFields = true, Converters = { new JsonStringEnumConverter() }, MaxDepth = 48
    };
    // Per-kind transport syntax only. All legality and candidates remain in GameSession.
    public static readonly Dictionary<string, string[]> Fields = new()
    {
        ["ChooseHero"] = ["Value"], ["DeployHero"] = ["TargetSeat", "Destination"],
        ["SelectCard"] = ["Value"], ["ConfirmCard"] = [], ["CancelCardSelection"] = [], ["ChooseInitiative"] = ["TargetSeat"],
        ["Move"] = ["MoveMode", "Destination?", "Value?"], ["Pass"] = [],
        ["ChooseMinionSpawn"] = ["Destination", "Value?"], ["BeginPrimary"] = [],
        ["CommitPrimaryAttack"] = ["Value"], ["CommitPrimaryMove"] = ["Destination?", "Value?"], ["CommitPrimaryPlacement"] = ["Destination"],
        ["ChooseAttackTarget"] = ["Value"], ["Defend"] = ["Value"], ["DeclineDefense"] = [],
        ["RespawnHero"] = ["Destination"], ["ResolveRoundEnd"] = [],
        ["ChooseRoundMinionRemoval"] = ["Value"], ["ChooseUpgrade"] = ["Value"],
        ["ForcedDiscard"] = ["Value"], ["ChooseOptionalDiscard"] = ["Value"],
        ["DeclineRetaliationDiscard"] = [], ["ChooseEffectMove"] = ["Destination?", "Value?"],
        ["ChooseRecoveredCard"] = ["Value"], ["ChooseEffectTarget"] = ["Value"],
        ["ChooseCardSwap"] = ["Value"], ["ChooseGoldTransfer"] = ["TargetSeat", "Value"],
        ["ChoosePlacement"] = ["Destination"], ["ChooseMinionReturn"] = ["Value", "Destination"],
        ["ChoosePrimaryOption"] = ["Value"], ["ChooseMinionProtection"] = ["Value"],
        ["ChooseDiscardAttack"] = ["Value"]
    };
    public static JsonElement Parse(byte[] bytes)
    {
        try
        {
            using var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
            CheckTree(doc.RootElement);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) throw new WireError("invalid_message");
            return doc.RootElement.Clone();
        }
        catch (JsonException) { throw new WireError("invalid_json"); }
    }
    private static void CheckTree(JsonElement item)
    {
        if (item.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in item.EnumerateObject())
            {
                if (!names.Add(p.Name)) throw new WireError("duplicate_field");
                CheckTree(p.Value);
            }
        }
        else if (item.ValueKind == JsonValueKind.Array)
            foreach (var child in item.EnumerateArray()) CheckTree(child);
    }
    public static void Shape(JsonElement item, params string[] fields)
    {
        if (item.ValueKind != JsonValueKind.Object) throw new WireError("invalid_object");
        var allowed = fields.Select(x => x.TrimEnd('?')).ToHashSet(StringComparer.Ordinal);
        if (item.EnumerateObject().Any(p => !allowed.Contains(p.Name))) throw new WireError("unknown_field");
        if (fields.Where(x => !x.EndsWith('?')).Any(x => !item.TryGetProperty(x, out _))) throw new WireError("missing_field");
    }
    public static string Text(JsonElement obj, string key, int max = 256)
    {
        if (!obj.TryGetProperty(key, out var v) || v.ValueKind != JsonValueKind.String) throw new WireError("invalid_" + key);
        var value = v.GetString()!;
        if (value.Length > max) throw new WireError("invalid_" + key);
        return value;
    }
    public static long Number(JsonElement obj, string key, long min, long max)
    {
        if (!obj.TryGetProperty(key, out var v) || v.ValueKind != JsonValueKind.Number || !v.TryGetInt64(out long n) || n < min || n > max)
            throw new WireError("invalid_" + key);
        return n;
    }
    public static Command Intent(JsonElement item, int seat)
    {
        string kind = Text(item, "Kind");
        if (!Fields.TryGetValue(kind, out var args)) throw new WireError("unknown_kind");
        Shape(item, ["Type", "CommandId", "MatchId", "ExpectedRevision", "Kind", .. args]);
        if (Text(item, "Type") != "Intent") throw new WireError("invalid_message");
        var cmd = new Command { ActorSeat = seat, Kind = Enum.Parse<CommandKind>(kind), Id = Text(item, "CommandId", 100),
            MatchId = Text(item, "MatchId", 100), ExpectedRevision = Number(item, "ExpectedRevision", 0, long.MaxValue) };
        if (string.IsNullOrWhiteSpace(cmd.Id)) throw new WireError("invalid_command_id");
        if (item.TryGetProperty("Value", out _)) cmd.Value = Text(item, "Value");
        if (item.TryGetProperty("TargetSeat", out _)) cmd.TargetSeat = (int)Number(item, "TargetSeat", kind == "ChooseGoldTransfer" ? -1 : 0, 3);
        if (item.TryGetProperty("MoveMode", out _))
        {
            string mode = Text(item, "MoveMode");
            if (mode != "Secondary" && mode != "Fast") throw new WireError("invalid_MoveMode");
            cmd.MoveMode = Enum.Parse<MoveMode>(mode);
        }
        if (item.TryGetProperty("Destination", out var dest))
        {
            Shape(dest, "X", "Y");
            cmd.Destination = new Hex((int)Number(dest, "X", -1000, 1000), (int)Number(dest, "Y", -1000, 1000));
        }
        if (kind == "Move" && !item.TryGetProperty("Destination", out _) && cmd.Value != "begin") throw new WireError("missing_Destination");
        if ((kind == "ChooseEffectMove" || kind == "CommitPrimaryMove") && !item.TryGetProperty("Destination", out _) && cmd.Value != "skip") throw new WireError("missing_Destination");
        if ((kind == "Move" && cmd.Value == "begin" || (kind == "ChooseEffectMove" || kind == "CommitPrimaryMove") && cmd.Value == "skip") && item.TryGetProperty("Destination", out _))
            throw new WireError("ambiguous_destination");
        return cmd;
    }
    public static async Task<byte[]> Read(NetworkStream stream, int max, CancellationToken token)
    {
        var header = new byte[4]; await stream.ReadExactlyAsync(header, token);
        int length = BinaryPrimitives.ReadInt32BigEndian(header);
        if (length <= 0 || length > max) throw new WireError("frame_too_large");
        var body = new byte[length]; await stream.ReadExactlyAsync(body, token); return body;
    }
    public static async Task Write(NetworkStream stream, byte[] body, CancellationToken token)
    {
        if (body.Length > MaxResponse) throw new WireError("snapshot_too_large");
        var header = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(header, body.Length);
        await stream.WriteAsync(header, token); await stream.WriteAsync(body, token);
    }
    public static byte[] Encode(object message) => JsonSerializer.SerializeToUtf8Bytes(message, Json);
}
