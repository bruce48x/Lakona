using System.Reflection;
using System.Text.Json;

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

        var callback = await GetCallbackAsync(callbackType, command.ToSessionKey(), cancellationToken)
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
            method.Invoke(callback, arguments);
            return ClientNotificationStatus.Delivered;
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
            .FirstOrDefault(method => method.GetParameters().Length == command.Arguments.Count);
    }

    private static object?[] DecodeArguments(
        MethodInfo method,
        ClientNotificationCommand command)
    {
        var parameters = method.GetParameters();
        var arguments = new object?[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
        {
            arguments[i] = JsonSerializer.Deserialize(
                command.Arguments[i].Payload,
                parameters[i].ParameterType);
        }

        return arguments;
    }
}
