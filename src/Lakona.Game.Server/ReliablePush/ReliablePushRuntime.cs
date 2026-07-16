using Lakona.Game.Abstractions;
using Lakona.Game.Cluster.Rpc;
using Lakona.Game.Server.Sessions;
using Lakona.Rpc.Core;

namespace Lakona.Game.Server.ReliablePush;

internal sealed class ReliablePushRuntime : IReliablePushRuntime
{
    private readonly IReliablePushOutbox _outbox;
    private readonly IReliablePushAckService _acks;
    private readonly LocalClientNotificationCommandDispatcher _localDispatcher;
    private readonly IGameSessionRegistry _sessions;

    public ReliablePushRuntime(
        IReliablePushOutbox outbox,
        IReliablePushAckService acks,
        LocalClientNotificationCommandDispatcher localDispatcher,
        IGameSessionRegistry sessions)
    {
        _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
        _acks = acks ?? throw new ArgumentNullException(nameof(acks));
        _localDispatcher = localDispatcher ?? throw new ArgumentNullException(nameof(localDispatcher));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
    }

    public async ValueTask<ClientNotificationStatus> PublishAsync(
        GameSessionKey session,
        ClientNotificationCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!await _sessions.GetReliablePushPolicyAsync(session, cancellationToken).ConfigureAwait(false))
        {
            command.Metadata = null;
            return await _localDispatcher.DispatchAsync(command, cancellationToken).ConfigureAwait(false);
        }

        var immediateStatus = ClientNotificationStatus.RouteNotFound;
        var replayPending = await _sessions
            .IsReliableReplayPendingAsync(session, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            await _outbox.PublishAsync(
                session,
                CreateRecordKind(command),
                command,
                async record =>
                {
                    if (replayPending)
                    {
                        immediateStatus = ClientNotificationStatus.Accepted;
                        return;
                    }
                    immediateStatus = await DispatchRecordAsync(
                        session,
                        record,
                        cancellationToken).ConfigureAwait(false);
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (ReliablePushContinuityLostException ex)
        {
            if (ex.NewlyLost)
            {
                ReliablePushDiagnostics.ContinuityLost.Add(
                    1,
                    new KeyValuePair<string, object?>("reason", "capacity"));
            }
            await _sessions.MarkReliableContinuityLostAsync(session, cancellationToken).ConfigureAwait(false);
            return ClientNotificationStatus.Failed;
        }

        return immediateStatus;
    }

    public async ValueTask<ClientNotificationStatus> PublishGeneratedAsync<TCallback, TPayload>(
        GameSessionKey session,
        int serviceId,
        int methodId,
        string methodName,
        TPayload payload,
        CancellationToken cancellationToken = default)
        where TCallback : class
    {
        if (!await _sessions.GetReliablePushPolicyAsync(session, cancellationToken).ConfigureAwait(false))
        {
            return await _localDispatcher.DispatchGeneratedAsync<TCallback, TPayload>(
                session,
                serviceId,
                methodId,
                methodName,
                payload,
                cancellationToken).ConfigureAwait(false);
        }

        return await PublishAsync(
            session,
            ClientNotificationCommandFactory.CreateGenerated<TCallback, TPayload>(
                session,
                serviceId,
                methodId,
                methodName,
                payload),
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask ReplayPendingAsync(
        GameSessionKey session,
        CancellationToken cancellationToken = default)
    {
        if (!await _sessions.GetReliablePushPolicyAsync(session, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            await _sessions.MarkReliableReplayReadyAsync(session, cancellationToken).ConfigureAwait(false);
            await _outbox.ReplayPendingAsync(
                session,
                async record =>
                {
                    await DispatchRecordAsync(session, record, cancellationToken).ConfigureAwait(false);
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (ReliablePushContinuityLostException)
        {
            await _sessions.MarkReliableContinuityLostAsync(session, cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask<ReliablePushAckOutcome> AckAsync(
        GameSessionKey currentSession,
        GameSessionKey acknowledgedSession,
        long sequence,
        CancellationToken cancellationToken = default)
    {
        if (!await _sessions.GetReliablePushPolicyAsync(currentSession, cancellationToken).ConfigureAwait(false))
        {
            return ReliablePushAckOutcome.SessionMismatch(
                "Reliable push acknowledgement is disabled for this game session.");
        }

        return await _acks.AckAsync(
            currentSession,
            acknowledgedSession,
            sequence,
            cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<ClientNotificationStatus> DispatchRecordAsync(
        GameSessionKey session,
        ReliablePushRecord record,
        CancellationToken cancellationToken)
    {
        if (record.Payload is not ClientNotificationCommand command)
        {
            return ClientNotificationStatus.Failed;
        }

        command.Metadata = new RpcPushMetadata
        {
            Type = LakonaInternalCodec.ReliablePushMetadataType,
            Payload = LakonaInternalCodec.EncodeReliablePushMetadata(new ReliablePushMetadata(
                session.SessionId,
                session.Generation,
                ReliablePushSequence.From(record.Sequence),
                record.Kind))
        };

        return await _localDispatcher.DispatchAsync(command, cancellationToken).ConfigureAwait(false);
    }

    private static string CreateRecordKind(ClientNotificationCommand command)
    {
        var contract = command.CallbackContractType;
        var assemblySeparator = contract.IndexOf(',', StringComparison.Ordinal);
        if (assemblySeparator > 0)
        {
            contract = contract[..assemblySeparator];
        }

        if (string.IsNullOrWhiteSpace(contract))
        {
            return string.IsNullOrWhiteSpace(command.MethodName)
                ? "notification"
                : command.MethodName;
        }

        return string.IsNullOrWhiteSpace(command.MethodName)
            ? contract
            : contract + "." + command.MethodName;
    }
}
