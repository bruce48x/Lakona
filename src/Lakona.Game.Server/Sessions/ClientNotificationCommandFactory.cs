using System.Reflection;
using System.Text.Json;
using Lakona.Game.Cluster.Rpc;

namespace Lakona.Game.Server.Sessions;

public static class ClientNotificationCommandFactory
{
    public static ClientNotificationCommand? Create<TCallback>(
        GameSessionKey session,
        Action<TCallback> notify)
        where TCallback : class
    {
        return CreateAsync<TCallback>(
            session,
            callback =>
            {
                notify(callback);
                return default;
            }).AsTask().GetAwaiter().GetResult();
    }

    public static ClientNotificationCommand? Create<TCallback>(
        GameSessionKey session,
        Func<TCallback, ValueTask> notify)
        where TCallback : class
    {
        return CreateAsync(session, notify).AsTask().GetAwaiter().GetResult();
    }

    public static async ValueTask<ClientNotificationCommand?> CreateAsync<TCallback>(
        GameSessionKey session,
        Func<TCallback, ValueTask> notify)
        where TCallback : class
    {
        var callbackType = typeof(TCallback);
        if (!callbackType.IsInterface)
        {
            return null;
        }

        var proxy = DispatchProxy.Create<TCallback, CaptureProxy<TCallback>>();
        var capture = (CaptureProxy<TCallback>)(object)proxy!;
        await notify(proxy!).ConfigureAwait(false);

        if (capture.Invocation is null)
        {
            return null;
        }

        var invocation = capture.Invocation;
        var parameters = invocation.Method.GetParameters();
        var arguments = new List<ClientNotificationArgument>(parameters.Length);
        var invocationArgumentIndex = 0;
        for (var i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].ParameterType == typeof(CancellationToken))
            {
                invocationArgumentIndex++;
                continue;
            }

            var payload = JsonSerializer.SerializeToUtf8Bytes(
                invocation.Arguments[invocationArgumentIndex],
                parameters[i].ParameterType);
            arguments.Add(new ClientNotificationArgument
            {
                TypeName = parameters[i].ParameterType.AssemblyQualifiedName ?? parameters[i].ParameterType.FullName ?? "",
                Payload = payload
            });
            invocationArgumentIndex++;
        }

        return new ClientNotificationCommand
        {
            OwnerKey = session.OwnerKey,
            SessionId = session.SessionId,
            Generation = session.Generation,
            CallbackContractType = callbackType.AssemblyQualifiedName ?? callbackType.FullName ?? "",
            MethodName = invocation.Method.Name,
            Arguments = arguments
        };
    }

    public class CaptureProxy<TCallback> : DispatchProxy
    {
        internal CapturedInvocation? Invocation { get; private set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is null || Invocation is not null)
            {
                throw new InvalidOperationException("A remote client notification must invoke exactly one callback method.");
            }

            Invocation = new CapturedInvocation(targetMethod, args ?? []);
            if (targetMethod.ReturnType == typeof(ValueTask))
            {
                return default(ValueTask);
            }

            return null;
        }
    }

    internal sealed record CapturedInvocation(MethodInfo Method, object?[] Arguments);
}
