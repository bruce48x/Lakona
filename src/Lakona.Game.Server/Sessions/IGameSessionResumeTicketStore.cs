using Lakona.Game.Abstractions;

namespace Lakona.Game.Server.Sessions;

internal interface IGameSessionResumeTicketStore
{
    ValueTask<string> IssueAsync(
        GameSessionKey session,
        string endpointScope,
        CancellationToken cancellationToken = default);

    ValueTask<GameSessionKey?> ResolveAsync(
        string ticket,
        string endpointScope,
        CancellationToken cancellationToken = default);

    ValueTask RevokeAsync(
        GameSessionKey session,
        CancellationToken cancellationToken = default);
}
