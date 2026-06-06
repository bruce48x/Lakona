using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lakona.Game.Abstractions;

namespace Lakona.Game.Client.ReliablePush
{
    public sealed class InMemoryReliablePushCursorStore : IReliablePushCursorStore
    {
        private readonly object _gate = new object();
        private readonly Dictionary<GameSessionKey, long> _sequences = new Dictionary<GameSessionKey, long>();

        public ValueTask<long> LoadAsync(
            GameSessionKey session,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                return new ValueTask<long>(_sequences.TryGetValue(session, out var sequence) ? sequence : 0);
            }
        }

        public ValueTask SaveAsync(
            GameSessionKey session,
            long sequence,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                _sequences[session] = sequence <= 0 ? 0 : sequence;
            }

            return default;
        }

        public ValueTask ClearAsync(
            GameSessionKey session,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                _sequences.Remove(session);
            }

            return default;
        }
    }
}
