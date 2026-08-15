using Lakona.Game.Cluster.Rpc;
using Lakona.Rpc.Server;

namespace Lakona.Game.Server.Sessions;

internal sealed class LocalClientNotificationCommandDispatcher
{
    private readonly GameSessionCallbackResolver _callbacks;

    internal LocalClientNotificationCommandDispatcher(GameSessionCallbackResolver callbacks)
    {
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

        await generatedTarget.DispatchNotificationAsync(
            serviceId,
            methodId,
            payload,
            metadata: null,
            cancellationToken).ConfigureAwait(false);
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
        catch
        {
            return ClientNotificationStatus.Failed;
        }
    }

    private static GameSessionKey ToSessionKey(ClientNotificationCommand command) =>
        new(command.OwnerKey, command.SessionId);
}
