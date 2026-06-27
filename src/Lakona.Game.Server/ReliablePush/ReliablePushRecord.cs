namespace Lakona.Game.Server.ReliablePush;

internal sealed class ReliablePushRecord
{
    public required string OwnerKey { get; init; }

    public required string Kind { get; init; }

    public required object Payload { get; init; }

    public long Sequence { get; init; }

    public DateTime CreatedAtUtc { get; init; }

    public DateTime LastAttemptAtUtc { get; set; }

    public int AttemptCount { get; set; }
}
