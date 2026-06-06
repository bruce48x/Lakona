using System.Threading;
using System.Threading.Tasks;
using Lakona.Game.Abstractions;

namespace Lakona.Game.Client.ReliablePush
{
    public interface IReliablePushCursorStore
    {
        ValueTask<long> LoadAsync(
            GameSessionKey session,
            CancellationToken cancellationToken = default);

        ValueTask SaveAsync(
            GameSessionKey session,
            long sequence,
            CancellationToken cancellationToken = default);

        ValueTask ClearAsync(
            GameSessionKey session,
            CancellationToken cancellationToken = default);
    }
}
