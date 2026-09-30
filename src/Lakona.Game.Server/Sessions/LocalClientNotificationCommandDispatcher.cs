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
    private long _nextFailureLogAt;

    internal LocalClientNotificationCommandDispatcher(
        GameSessionCallbackResolver callbacks,
        ILoggerFactory? loggerFactory = null,
        TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<LocalClientNotificationCommandDispatcher>();
        _callbacks = callbacks ?? throw new ArgumentNullException(nameof(callbacks));
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

    public async ValueTask<ClientNotificationStatus> DispatchAsync(
        ClientNotificationCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        var callbackType = Type.GetType(command.CallbackContractType, throwOnError: false);
        if (callbackType is null)
        {
            return ClientNotificationStatus.CallbackUnavailable;
        }

        var callback = await _callbacks
            .ResolveAsync(callbackType, ToSessionKey(command), cancellationToken)
            .ConfigureAwait(false);
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
            await generatedTarget
                .DispatchNotificationAsync(
                    command.ServiceId,
                    command.MethodId,
                    new ReadOnlyMemory<byte>(command.Payload),
                    command.Metadata?.ToRpcPushMetadata(),
                    cancellationToken)
                .ConfigureAwait(false);
            return ClientNotificationStatus.Accepted;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            RecordSendFailure(ToSessionKey(command), command.ServiceId, command.MethodId, ex);
            return ClientNotificationStatus.Failed;
        }
    }

    private void RecordSendFailure(GameSessionKey session, int serviceId, int methodId, Exception exception)
    {
        ClientNotificationDiagnostics.SendFailures.Add(1);
        // Bound warnings without retaining per-session state. Exception messages and
        // stacks may contain serializer payloads or credentials, so log only the type.
        var now = _timeProvider.GetTimestamp();
        var next = Volatile.Read(ref _nextFailureLogAt);
        if (!_logger.IsEnabled(LogLevel.Warning) || now < next ||
            Interlocked.CompareExchange(ref _nextFailureLogAt, now + 30 * _timeProvider.TimestampFrequency, next) != next)
            return;

        _logger.LogWarning(
            "Client notification send failed. SessionId={SessionId} ServiceId={ServiceId} MethodId={MethodId} ExceptionType={ExceptionType}. Further send failure warnings suppressed for 30 seconds; all failures counted by lakona.game.notification.send_failure.",
            session.SessionId, serviceId, methodId, exception.GetType().FullName);
    }

    private static GameSessionKey ToSessionKey(ClientNotificationCommand command) =>
        new(command.OwnerKey, command.SessionId);
}
