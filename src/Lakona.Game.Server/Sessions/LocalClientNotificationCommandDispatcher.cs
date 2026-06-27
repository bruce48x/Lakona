using System.Reflection;
using System.Text.Json;
using Lakona.Game.Cluster.Rpc;
using Lakona.Rpc.Server;

namespace Lakona.Game.Server.Sessions;

public sealed class LocalClientNotificationCommandDispatcher
{
    private readonly IGameSessionRegistry _sessions;

    public LocalClientNotificationCommandDispatcher(IGameSessionRegistry sessions)
    {
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
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

        var callback = await GetCallbackAsync(callbackType, ToSessionKey(command), cancellationToken)
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
            var arguments = DecodeArguments(method, command);
            if (callback is IRpcNotificationDispatchTarget dispatchTarget)
            {
                await dispatchTarget
                    .DispatchNotificationAsync(
                        command.MethodName,
                        arguments,
                        command.Metadata,
                        cancellationToken)
                    .ConfigureAwait(false);
                return ClientNotificationStatus.Delivered;
            }

            var result = method.Invoke(callback, arguments);
            if (result is ValueTask valueTask)
            {
                await valueTask.ConfigureAwait(false);
            }

            return ClientNotificationStatus.Delivered;
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

    private async ValueTask<object?> GetCallbackAsync(
        Type callbackType,
        GameSessionKey session,
        CancellationToken cancellationToken)
    {
        var method = typeof(IGameSessionRegistry)
            .GetMethod(nameof(IGameSessionRegistry.GetCallbackAsync))!
            .MakeGenericMethod(callbackType);
        var valueTask = method.Invoke(_sessions, [session, cancellationToken]);
        if (valueTask is null)
        {
            return null;
        }

        var asTask = valueTask.GetType().GetMethod("AsTask")!;
        var task = (Task)asTask.Invoke(valueTask, null)!;
        await task.ConfigureAwait(false);
        return task.GetType().GetProperty("Result")?.GetValue(task);
    }

    private static MethodInfo? ResolveMethod(
        Type callbackType,
        ClientNotificationCommand command)
    {
        return callbackType.GetMethods()
            .Where(method => string.Equals(method.Name, command.MethodName, StringComparison.Ordinal))
            .FirstOrDefault(method => method
                .GetParameters()
                .Count(parameter => parameter.ParameterType != typeof(CancellationToken)) == command.Arguments.Count);
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

            arguments[i] = JsonSerializer.Deserialize(
                command.Arguments[commandArgumentIndex].Payload,
                parameters[i].ParameterType);
            commandArgumentIndex++;
        }

        return arguments;
    }

    private static GameSessionKey ToSessionKey(ClientNotificationCommand command) =>
        new(command.OwnerKey, command.SessionId, command.Generation);
}
