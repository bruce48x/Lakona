using Lakona.Game.Abstractions;

namespace Lakona.Game.Server.Sessions;

public interface IGameSessionResumeService
{
    ValueTask<SessionResumeDecision> TryResumeAsync(
        GameSessionResumeRequest request,
        CancellationToken cancellationToken = default);
}
