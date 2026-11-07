using Lakona.Game.Abstractions;
using Lakona.Game.Cluster.Rpc;
using Lakona.Game.Server.Sessions;
using Lakona.Rpc.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lakona.Game.Server.ReliablePush;

internal sealed class ReliablePushRuntime : IReliablePushRuntime
{
    private readonly ILogger _logger;
    private readonly IReliablePushOutbox _outbox;
    private readonly IReliablePushAckService _acks;
    private readonly LocalClientNotificationCommandDispatcher _localDispatcher;
    private readonly IGameSessionRegistry _sessions;
    private readonly ClientNotificationCommandFactory _commands;

    public ReliablePushRuntime(
        IReliablePushOutbox outbox,
        IReliablePushAckService acks,
        LocalClientNotificationCommandDispatcher localDispatcher,
        IGameSessionRegistry sessions,
        ClientNotificationCommandFactory commands,
        ILoggerFactory? loggerFactory = null)
    {
        _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<ReliablePushRuntime>();
        _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
        _acks = acks ?? throw new ArgumentNullException(nameof(acks));
        _localDispatcher = localDispatcher ?? throw new ArgumentNullException(nameof(localDispatcher));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
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

        if (!await _localDispatcher.MatchesSessionProfileAsync(session, command, cancellationToken).ConfigureAwait(false))
            return ClientNotificationStatus.Failed;

        return await PublishReliableAsync(session, command, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<ClientNotificationStatus> PublishReliableAsync(
        GameSessionKey session,
        ClientNotificationCommand command,
        CancellationToken cancellationToken)
    {
        var immediateStatus = ClientNotificationStatus.RouteNotFound;
        try
        {
            await _outbox.PublishAsync(
                session,
                CreateRecordKind(command),
                command,
                async record =>
                {
                    // Capture the destination before checking its hold. A rebind must never
                    // redirect an admitted write onto a connection still waiting for replay.
                    var connectionId = await _sessions.GetConnectionIdAsync(session, cancellationToken).ConfigureAwait(false);
                    if (await _sessions.IsReliableReplayPendingAsync(session, cancellationToken).ConfigureAwait(false))
                    {
                        immediateStatus = ClientNotificationStatus.Retained;
                        return;
                    }
                    immediateStatus = connectionId is null
                        ? ClientNotificationStatus.CallbackUnavailable
                        : await DispatchRecordAsync(session, record, connectionId, cancellationToken).ConfigureAwait(false);
                },
                cancellationToken, retainedBytes: ClientNotificationSize.RetainedBytes(command)).ConfigureAwait(false);
        }
        catch (ReliablePushContinuityLostException ex)
        {
            if (ex.NewlyLost)
            {
                ReliablePushDiagnostics.ContinuityLost.Add(
                    1,
                    new KeyValuePair<string, object?>("reason", "capacity"));
                _logger.LogWarning(
                    "Reliable push continuity lost: capacity. SessionId={SessionId} PendingCount={PendingCount} LastSequence={LastSequence} AcknowledgedSequence={AcknowledgedSequence}",
                    session.SessionId, ex.PendingCount, ex.LastSequence, ex.AcknowledgedSequence);
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
                payload,
                cancellationToken).ConfigureAwait(false);
        }

        var command = await _commands.CreateAsync<TCallback, TPayload>(
            session, serviceId, methodId, methodName, payload, remoteGateway: null, cancellationToken)
            .ConfigureAwait(false);
        return await PublishReliableAsync(session, command, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask ReplayPendingAsync(
        GameSessionKey session,
        string connectionId,
        CancellationToken cancellationToken = default)
    {
        if (!await _sessions.GetReliablePushPolicyAsync(session, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        var held = await _sessions.IsReliableReplayPendingAsync(session, cancellationToken).ConfigureAwait(false);
        try
        {
            await _outbox.ReplayPendingAsync(
                session,
                async record =>
                {
                    if (await _sessions.GetConnectionIdAsync(session, cancellationToken).ConfigureAwait(false) != connectionId)
                        throw new InvalidOperationException("The connection was replaced during reliable replay.");

                    var status = await DispatchRecordAsync(session, record, connectionId, cancellationToken).ConfigureAwait(false);
                    if (status != ClientNotificationStatus.Accepted)
                        throw new InvalidOperationException($"Reliable replay could not send sequence {record.Sequence}: {status}.");
                },
                held ? CompleteReplayAsync : null,
                cancellationToken).ConfigureAwait(false);
        }
        catch (ReliablePushContinuityLostException)
        {
            await _sessions.MarkReliableContinuityLostAsync(session, cancellationToken).ConfigureAwait(false);
            if (held) throw;
        }

        async ValueTask CompleteReplayAsync()
        {
            if (!await _sessions.TryMarkReliableReplayReadyAsync(session, connectionId, cancellationToken).ConfigureAwait(false))
                throw new InvalidOperationException("The connection can no longer complete reliable replay.");
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
        string connectionId,
        CancellationToken cancellationToken)
    {
        if (record.Payload is not ClientNotificationCommand command)
        {
            return ClientNotificationStatus.Failed;
        }

        command.Metadata = new ClientNotificationMetadata
        {
            Type = LakonaInternalCodec.ReliablePushMetadataType,
            Payload = LakonaInternalCodec.EncodeReliablePushMetadata(new ReliablePushMetadata(
                session.SessionId,
                ReliablePushSequence.From(record.Sequence),
                record.Kind))
        };

        return await _localDispatcher.DispatchAsync(command, connectionId, cancellationToken).ConfigureAwait(false);
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
