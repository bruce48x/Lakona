using System.Threading;
using System.Threading.Tasks;

namespace Lakona.Game.Client.ReliablePush
{
    public interface IReliablePushCursorStore
    {
        ValueTask<long> LoadAsync(
            string sessionId,
            long sessionGeneration,
            CancellationToken cancellationToken = default);

        ValueTask SaveAsync(
            string sessionId,
            long sessionGeneration,
            long sequence,
            CancellationToken cancellationToken = default);

        ValueTask ClearAsync(
            string sessionId,
            long sessionGeneration,
            CancellationToken cancellationToken = default);
    }
}
