using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Lakona.Game.Client.ReliablePush
{
    public sealed class InMemoryReliablePushCursorStore : IReliablePushCursorStore
    {
        private readonly object _gate = new object();
        private readonly Dictionary<string, long> _sequences = new Dictionary<string, long>(System.StringComparer.Ordinal);

        public ValueTask<long> LoadAsync(
            string sessionId,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                throw new System.ArgumentException("Session id is required.", nameof(sessionId));
            }

            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                return new ValueTask<long>(_sequences.TryGetValue(sessionId, out var sequence) ? sequence : 0);
            }
        }

        public ValueTask SaveAsync(
            string sessionId,
            long sequence,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                throw new System.ArgumentException("Session id is required.", nameof(sessionId));
            }

            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                _sequences[sessionId] = sequence <= 0 ? 0 : sequence;
            }

            return default;
        }

        public ValueTask ClearAsync(
            string sessionId,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                throw new System.ArgumentException("Session id is required.", nameof(sessionId));
            }

            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                _sequences.Remove(sessionId);
            }

            return default;
        }
    }
}
