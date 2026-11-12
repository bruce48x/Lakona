using Lakona.Game.Cluster.Rpc;
using Lakona.Rpc.Server;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lakona.Game.Server.Sessions;

internal sealed class LocalClientNotificationCommandDispatcher
{
    private readonly GameSessionCallbackResolver _callbacks;
    private readonly ILogger _logger;
    private readonly TimeProvider _timeProvider;
    private readonly IGameSessionRegistry _sessions;
    private long _nextFailureLogAt;

    internal LocalClientNotificationCommandDispatcher(
        GameSessionCallbackResolver callbacks,
        IGameSessionRegistry sessions,
        ILoggerFactory? loggerFactory = null,
        TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<LocalClientNotificationCommandDispatcher>();
        _callbacks = callbacks ?? throw new ArgumentNullException(nameof(callbacks));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
    }

    internal async ValueTask<ClientNotificationStatus> DispatchGeneratedAsync<TCallback, TPayload>(
        GameSessionKey session,
        int serviceId,
        int methodId,
        TPayload payload,
        CancellationToken cancellationToken = default)
        where TCallback : class
    {
        cancellationToken.ThrowIfCancellationRequested();
        var callback = await _callbacks.ResolveAsync<TCallback>(session, cancellationToken).ConfigureAwait(false);
        if (callback is null)
        {
            return ClientNotificationStatus.CallbackUnavailable;
        }

        if (callback is not IRpcNotificationDispatchTarget generatedTarget)
        {
            return ClientNotificationStatus.Failed;
        }

        try
        {
            await generatedTarget.DispatchNotificationAsync(
                serviceId,
                methodId,
                payload,
                metadata: null,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            RecordSendFailure(session, serviceId, methodId, ex);
            throw;
        }
        return ClientNotificationStatus.Accepted;
    }

    public ValueTask<ClientNotificationStatus> DispatchAsync(
        ClientNotificationCommand command,
        CancellationToken cancellationToken = default) =>
        DispatchAsync(command, null, cancellationToken);

    internal async ValueTask<ClientNotificationStatus> DispatchAsync(
        ClientNotificationCommand command, string? connectionId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        var session = ToSessionKey(command);
        if (!await MatchesSessionProfileAsync(session, command, cancellationToken).ConfigureAwait(false))
            return ClientNotificationStatus.Failed;

        var callbackType = Type.GetType(command.CallbackContractType, throwOnError: false);
        if (callbackType is null)
        {
            return ClientNotificationStatus.CallbackUnavailable;
        }

        var callback = connectionId is null
            ? await _callbacks.ResolveAsync(callbackType, session, cancellationToken).ConfigureAwait(false)
            : _callbacks.Resolve(callbackType, connectionId);
        if (callback is null)
        {
            return ClientNotificationStatus.CallbackUnavailable;
        }

        if (command.ServiceId <= 0 ||
            command.MethodId <= 0 ||
            callback is not IRpcNotificationDispatchTarget generatedTarget)
        {
            return ClientNotificationStatus.Failed;
        }

        try
        {
            var metadata = command.Metadata?.ToRpcPushMetadata();
            await generatedTarget.DispatchEncodedNotificationAsync(
                command.ServiceId, command.MethodId, command.Payload, metadata, cancellationToken)
                .ConfigureAwait(false);
            return ClientNotificationStatus.Accepted;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            RecordSendFailure(session, command.ServiceId, command.MethodId, ex);
            return ClientNotificationStatus.Failed;
        }
    }

    internal async ValueTask<bool> MatchesSessionProfileAsync(GameSessionKey session,
        ClientNotificationCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(command.SerializerProfile) || command.Payload is null)
            return false;
        var current = await _sessions.GetNotificationSerializerProfileAsync(session, cancellationToken).ConfigureAwait(false);
        return string.Equals(current, command.SerializerProfile, StringComparison.Ordinal);
    }

    private void RecordSendFailure(GameSessionKey session, int serviceId, int methodId, Exception exception)
    {
        ClientNotificationDiagnostics.SendFailures.Add(1);
        ClientNotificationDiagnostics.LogFailure(_logger, _timeProvider, ref _nextFailureLogAt,
            "send", session, exception, serviceId, methodId);
    }

    private static GameSessionKey ToSessionKey(ClientNotificationCommand command) =>
        new(command.OwnerKey, command.SessionId);
}
