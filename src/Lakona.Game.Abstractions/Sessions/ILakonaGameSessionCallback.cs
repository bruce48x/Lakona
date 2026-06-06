using System.Threading;
using System.Threading.Tasks;

namespace Lakona.Game.Abstractions
{
    public interface ILakonaGameSessionCallback
    {
        ValueTask OnSessionTerminatedAsync(
            SessionTerminationNotice notice,
            CancellationToken cancellationToken = default);
    }
}
