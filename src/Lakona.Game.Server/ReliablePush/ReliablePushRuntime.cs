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
    private readonly ReliablePushOptions _options;

    public ReliablePushRuntime(
        IReliablePushOutbox outbox,
        IReliablePushAckService acks,
        LocalClientNotificationCommandDispatcher localDispatcher,
        ReliablePushOptions options)
    {
        _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
        _acks = acks ?? throw new ArgumentNullException(nameof(acks));
        _localDispatcher = localDispatcher ?? throw new ArgumentNullException(nameof(localDispatcher));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async ValueTask<ClientNotificationStatus> PublishAsync(
        GameSessionKey session,
        ClientNotificationCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!_options.Enabled)
        {
            command.Metadata = null;
            return await _localDispatcher.DispatchAsync(command, cancellationToken).ConfigureAwait(false);
        }

        var immediateStatus = ClientNotificationStatus.RouteNotFound;
        await _outbox.PublishAsync(
            session,
            CreateRecordKind(command),
            command,
            async record =>
            {
                immediateStatus = await DispatchRecordAsync(
                    session,
                    record,
                    cancellationToken).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);

        return immediateStatus;
    }

    public async ValueTask ReplayPendingAsync(
        GameSessionKey session,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return;
        }

        await _outbox.ReplayPendingAsync(
            session,
            async record =>
            {
                await DispatchRecordAsync(session, record, cancellationToken).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    public ValueTask<ReliablePushAckOutcome> AckAsync(
        GameSessionKey currentSession,
        GameSessionKey acknowledgedSession,
        long sequence,
        CancellationToken cancellationToken = default)
    {
        return _acks.AckAsync(currentSession, acknowledgedSession, sequence, cancellationToken);
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
