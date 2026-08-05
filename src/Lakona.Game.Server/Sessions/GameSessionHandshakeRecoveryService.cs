using Lakona.Game.Abstractions.Sessions;

namespace Lakona.Game.Server.Sessions;

internal interface IGameSessionHandshakeRecoveryService
{
    ValueTask<GameSessionRecoveryHandshakeResult> RecoverAsync(
        string? resumeTicket,
        string connectionId,
        string endpointScope,
        CancellationToken cancellationToken = default);
}

internal sealed class GameSessionHandshakeRecoveryService(
    IGameSessionResumeTicketStore tickets,
    IGameSessionRegistry sessions,
    ILakonaGameServer gameServer) : IGameSessionHandshakeRecoveryService
{
    public async ValueTask<GameSessionRecoveryHandshakeResult> RecoverAsync(
        string? resumeTicket,
        string connectionId,
        string endpointScope,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(resumeTicket))
            return new GameSessionRecoveryHandshakeResult { Status = GameSessionRecoveryStatus.NotRequested };

        var session = await tickets.ResolveAsync(resumeTicket, endpointScope, cancellationToken).ConfigureAwait(false);
        if (session is null)
            return Lost("The resume ticket is unknown or expired.");

        var decision = await sessions.TryResumeAsync(session.Value, cancellationToken).ConfigureAwait(false);
        if (decision.Status == SessionResumeStatus.Terminated)
            return new GameSessionRecoveryHandshakeResult
            {
                Status = GameSessionRecoveryStatus.Terminated,
                Reason = decision.Reason,
            };
        if (decision.Status != SessionResumeStatus.Resumed || decision.Session is null)
            return Lost(decision.Reason);

        if (await sessions.IsReliableContinuityLostAsync(session.Value, cancellationToken)
            .ConfigureAwait(false))
        {
            return new GameSessionRecoveryHandshakeResult
            {
                Status = GameSessionRecoveryStatus.StateRefreshRequired,
                Reason = "Reliable push continuity was lost.",
            };
        }

        await gameServer.BindSessionAsync(
            session.Value,
            connectionId,
            cancellationToken).ConfigureAwait(false);

        return new GameSessionRecoveryHandshakeResult { Status = GameSessionRecoveryStatus.Resumed };
    }

    private static GameSessionRecoveryHandshakeResult Lost(string? reason) => new()
    {
        Status = GameSessionRecoveryStatus.StateLost,
        Reason = reason,
    };
}
