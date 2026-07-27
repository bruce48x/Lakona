using System.Reflection;
using System.Text.Json;
using Lakona.Game.Cluster.Rpc;
using Lakona.Rpc.Server;

namespace Lakona.Game.Server.Sessions;

public sealed class LocalClientNotificationCommandDispatcher
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
        string methodName,
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

        if (callback is IRpcNotificationDispatchTarget generatedTarget)
        {
            try
            {
                await generatedTarget.DispatchNotificationAsync(
                    serviceId,
                    methodId,
                    payload,
                    metadata: null,
                    cancellationToken).ConfigureAwait(false);
                return ClientNotificationStatus.Accepted;
            }
            catch (NotSupportedException)
            {
            }
        }

        return await DispatchAsync(
            ClientNotificationCommandFactory.CreateGenerated<TCallback, TPayload>(
                session,
                serviceId,
                methodId,
                methodName,
                payload),
            cancellationToken).ConfigureAwait(false);
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

        var method = ResolveMethod(callbackType, command);
        if (method is null)
        {
            return ClientNotificationStatus.Failed;
        }

        try
        {
            if (command.ServiceId > 0 && command.MethodId > 0 &&
                callback is IRpcNotificationDispatchTarget generatedTarget)
            {
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
                catch (NotSupportedException)
                {
                    // Compatibility targets implement the legacy typed-object dispatcher only.
                }
            }

            var arguments = DecodeArguments(method, command);
            if (callback is IRpcNotificationDispatchTarget dispatchTarget)
            {
                await dispatchTarget
                    .DispatchNotificationAsync(
                        command.MethodName,
                        arguments,
                        command.Metadata?.ToRpcPushMetadata(),
                        cancellationToken)
                    .ConfigureAwait(false);
                return ClientNotificationStatus.Accepted;
            }

            var result = method.Invoke(callback, arguments);
            if (result is ValueTask valueTask)
            {
                await valueTask.ConfigureAwait(false);
            }

            return ClientNotificationStatus.Accepted;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TargetInvocationException ex)
            when (ex.InnerException is OperationCanceledException && cancellationToken.IsCancellationRequested)
        {
            throw ex.InnerException;
        }
        catch
        {
            return ClientNotificationStatus.Failed;
        }
    }

    private static MethodInfo? ResolveMethod(
        Type callbackType,
        ClientNotificationCommand command)
    {
        var payloadCount = command.ServiceId > 0 ? 1 : command.Arguments.Count;
        return callbackType.GetMethods()
            .Where(method => string.Equals(method.Name, command.MethodName, StringComparison.Ordinal))
            .FirstOrDefault(method => method
                .GetParameters()
                .Count(parameter => parameter.ParameterType != typeof(CancellationToken)) == payloadCount);
    }

    private static object?[] DecodeArguments(
        MethodInfo method,
        ClientNotificationCommand command)
    {
        var parameters = method.GetParameters();
        var arguments = new object?[parameters.Length];
        var commandArgumentIndex = 0;
        for (var i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].ParameterType == typeof(CancellationToken))
            {
                arguments[i] = CancellationToken.None;
                continue;
            }

            var payload = command.ServiceId > 0
                ? command.Payload
                : command.Arguments[commandArgumentIndex].Payload;
            arguments[i] = JsonSerializer.Deserialize(
                payload,
                parameters[i].ParameterType);
            commandArgumentIndex++;
        }

        return arguments;
    }

    private static GameSessionKey ToSessionKey(ClientNotificationCommand command) =>
        new(command.OwnerKey, command.SessionId);
}
