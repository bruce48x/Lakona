using Lakona.Game.Abstractions;

namespace Lakona.Game.Server.Sessions;

internal interface IGameSessionResumeTicketStore
{
    ValueTask<string> IssueAsync(
        GameSessionKey session,
        CancellationToken cancellationToken = default);

    ValueTask<GameSessionKey?> ResolveAsync(
        string ticket,
        CancellationToken cancellationToken = default);

    ValueTask RevokeAsync(
        GameSessionKey session,
        CancellationToken cancellationToken = default);
}
