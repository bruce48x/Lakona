using System.Threading;
using System.Threading.Tasks;

namespace Lakona.Game.Client.ReliablePush
{
    public interface IReliablePushCursorStore
    {
        ValueTask<long> LoadAsync(
            string sessionId,
            CancellationToken cancellationToken = default);

        ValueTask SaveAsync(
            string sessionId,
            long sequence,
            CancellationToken cancellationToken = default);

        ValueTask ClearAsync(
            string sessionId,
            CancellationToken cancellationToken = default);
    }
}
