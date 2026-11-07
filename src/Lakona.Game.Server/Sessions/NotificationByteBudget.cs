using Lakona.Game.Cluster.Rpc;

namespace Lakona.Game.Server.Sessions;

// Bounds retained encoded data, not the object graph of a not-yet-serialized DTO.
internal sealed class NotificationByteBudget<TKey> where TKey : notnull
{
    private readonly object gate = new();
    private readonly Dictionary<TKey, long> sessions = new();
    private readonly long maximumSessionBytes;
    private readonly long maximumProcessBytes;
    private long total;

    public NotificationByteBudget(long maximumSessionBytes, long maximumProcessBytes)
    {
        if (maximumSessionBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maximumSessionBytes));
        if (maximumProcessBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maximumProcessBytes));
        this.maximumSessionBytes = maximumSessionBytes;
        this.maximumProcessBytes = maximumProcessBytes;
    }

    public IDisposable? TryReserve(TKey key, long bytes)
    {
        if (bytes < 0) throw new ArgumentOutOfRangeException(nameof(bytes));
        lock (gate)
        {
            sessions.TryGetValue(key, out var current);
            if (bytes > maximumSessionBytes - current || bytes > maximumProcessBytes - total)
                return null;
            sessions[key] = current + bytes;
            total += bytes;
            return new Reservation(this, key, bytes);
        }
    }

    private void Release(TKey key, long bytes)
    {
        lock (gate)
        {
            var remaining = sessions[key] - bytes;
            if (remaining == 0) sessions.Remove(key);
            else sessions[key] = remaining;
            total -= bytes;
        }
    }

    private sealed class Reservation(NotificationByteBudget<TKey> owner, TKey key, long bytes) : IDisposable
    {
        private NotificationByteBudget<TKey>? budget = owner;
        public void Dispose() => Interlocked.Exchange(ref budget, null)?.Release(key, bytes);
    }
}

internal static class ClientNotificationSize
{
    // Include metadata and a conservative allowance for routing strings, record
    // overhead and the reliable metadata attached later by the gateway.
    public static long RetainedBytes(ClientNotificationCommand command) =>
        512L + command.Payload.LongLength + (command.Metadata?.Payload.LongLength ?? 0)
        + 4L * (command.OwnerKey.Length + command.SessionId.Length
            + command.CallbackContractType.Length + command.MethodName.Length
            + command.SerializerProfile.Length + (command.Metadata?.Type.Length ?? 0));
}
