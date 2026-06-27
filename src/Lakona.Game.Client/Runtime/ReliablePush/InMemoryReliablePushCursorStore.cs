using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Lakona.Game.Client.ReliablePush
{
    public sealed class InMemoryReliablePushCursorStore : IReliablePushCursorStore
    {
        private readonly object _gate = new object();
        private readonly Dictionary<(string SessionId, long SessionGeneration), long> _sequences =
            new Dictionary<(string SessionId, long SessionGeneration), long>();

        public ValueTask<long> LoadAsync(
            string sessionId,
            long sessionGeneration,
            CancellationToken cancellationToken = default)
        {
            ValidateKey(sessionId, sessionGeneration);

            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                var key = (sessionId, sessionGeneration);
                return new ValueTask<long>(_sequences.TryGetValue(key, out var sequence) ? sequence : 0);
            }
        }

        public ValueTask SaveAsync(
            string sessionId,
            long sessionGeneration,
            long sequence,
            CancellationToken cancellationToken = default)
        {
            ValidateKey(sessionId, sessionGeneration);

            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                _sequences[(sessionId, sessionGeneration)] = sequence <= 0 ? 0 : sequence;
            }

            return default;
        }

        public ValueTask ClearAsync(
            string sessionId,
            long sessionGeneration,
            CancellationToken cancellationToken = default)
        {
            ValidateKey(sessionId, sessionGeneration);

            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                _sequences.Remove((sessionId, sessionGeneration));
            }

            return default;
        }

        private static void ValidateKey(string sessionId, long sessionGeneration)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                throw new ArgumentException("Session id is required.", nameof(sessionId));
            }

            if (sessionGeneration <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(sessionGeneration),
                    "Session generation must be positive.");
            }
        }
    }
}
