using Lakona.Game.Abstractions.Sessions;
using Lakona.Rpc.Server;

namespace Lakona.Game.Server.Sessions;

internal interface IGameSessionHandshakeRecoveryService
{
    ValueTask<GameSessionRecoveryHandshakeResult> RecoverAsync(
        string? resumeTicket,
        RpcSession connection,
        string endpointScope,
        bool reliablePush,
        CancellationToken cancellationToken = default);
}

internal sealed class GameSessionHandshakeRecoveryService(
    IGameSessionResumeTicketStore tickets,
    IGameSessionRegistry sessions,
    GameSessionCallbackProxyRegistry callbackProxies,
    IClientSessionRouteRegistrar routes,
    IEnumerable<IGameSessionLifecycleHandler> lifecycleHandlers) : IGameSessionHandshakeRecoveryService
{
    public async ValueTask<GameSessionRecoveryHandshakeResult> RecoverAsync(
        string? resumeTicket,
        RpcSession connection,
        string endpointScope,
        bool reliablePush,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(resumeTicket))
            return new GameSessionRecoveryHandshakeResult { Status = GameSessionRecoveryStatus.NotRequested };

        var session = await tickets.ResolveAsync(resumeTicket, endpointScope, cancellationToken).ConfigureAwait(false);
        if (session is null)
            return Lost("The resume ticket is unknown or expired.");

        var callbackTypes = await sessions
            .GetCallbackContractTypesAsync(session.Value, cancellationToken)
            .ConfigureAwait(false);
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

        try
        {
            await sessions.SetReliablePushPolicyAsync(session.Value, reliablePush, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException exception)
        {
            return Lost(exception.Message);
        }

        GameSessionSnapshot? activated = null;
        foreach (var callbackType in callbackTypes)
        {
            var callback = callbackProxies.Create(callbackType, connection);
            var result = await sessions.BindSessionCallbackAsync(
                session.Value,
                connection.ContextId,
                callbackType,
                callback,
                cancellationToken).ConfigureAwait(false);
            activated ??= result.SessionBecameActive;
        }

        if (activated is not null)
        {
            await routes.RegisterAsync(activated.Session, cancellationToken).ConfigureAwait(false);
            var context = new GameSessionBindingContext(
                activated.Session,
                activated.ConnectionId,
                activated.CallbackContractTypes);
            foreach (var handler in lifecycleHandlers)
                await handler.OnSessionBoundAsync(context, cancellationToken).ConfigureAwait(false);
        }

        return new GameSessionRecoveryHandshakeResult { Status = GameSessionRecoveryStatus.Resumed };
    }

    private static GameSessionRecoveryHandshakeResult Lost(string? reason) => new()
    {
        Status = GameSessionRecoveryStatus.StateLost,
        Reason = reason,
    };
}
