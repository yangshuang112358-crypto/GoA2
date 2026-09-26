#nullable enable
using System;
using System.Threading.Tasks;
using Goa2.Domain;

namespace Goa2.Network.Client
{
    // Proposed UI boundary; integration still requires coordination with the main UI task.
    public enum ConnectionState { Disconnected, Connecting, Connected }
    public interface IPlayerSession : IDisposable
    {
        int? AuthenticatedSeat { get; }
        GameView? View { get; }
        ConnectionState Connection { get; }
        event Action? StateUpdated;
        Task<IntentResult> SubmitAsync(PlayerIntent intent);
        Task<IntentResult> RetryAsync(string commandId);
        Task ReconnectAsync();
        void Disconnect();
    }
    public sealed class PlayerIntent
    {
        public CommandKind Kind { get; }
        public string? Value { get; }
        public int? TargetSeat { get; }
        public Hex? Destination { get; }
        public MoveMode? MoveMode { get; }
        public PlayerIntent(CommandKind kind, string? value = null, int? targetSeat = null, Hex? destination = null, MoveMode? moveMode = null)
        { Kind = kind; Value = value; TargetSeat = targetSeat; Destination = destination; MoveMode = moveMode; }
    }
    public sealed class IntentResult
    {
        public string CommandId { get; internal set; } = "";
        public bool Accepted { get; internal set; }
        public bool Duplicate { get; internal set; }
        // Uncertain is retained for explicit original-ID retry after reconnect.
        public bool Uncertain { get; internal set; }
        public string Code { get; internal set; } = "";
        public string Message { get; internal set; } = "";
    }
}
