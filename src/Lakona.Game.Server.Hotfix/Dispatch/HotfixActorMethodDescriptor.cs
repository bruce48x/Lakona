using System.Reflection;

namespace Lakona.Game.Server.Hotfix.Dispatch;

public sealed class HotfixActorMethodDescriptor
{
    internal HotfixActorMethodDescriptor(
        string methodKey,
        Type actorType,
        string methodName,
        Type requestType,
        Type? resultType,
        MethodInfo method,
        bool hasCancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(methodKey);
        ArgumentNullException.ThrowIfNull(actorType);
        ArgumentException.ThrowIfNullOrWhiteSpace(methodName);
        ArgumentNullException.ThrowIfNull(requestType);
        ArgumentNullException.ThrowIfNull(method);

        MethodKey = methodKey;
        ActorType = actorType;
        MethodName = methodName;
        RequestType = requestType;
        ResultType = resultType;
        Method = method;
        HasCancellationToken = hasCancellationToken;
    }

    public string MethodKey { get; }

    public Type ActorType { get; }

    public string MethodName { get; }

    public Type RequestType { get; }

    public Type? ResultType { get; }

    internal MethodInfo Method { get; }

    internal bool HasCancellationToken { get; }
}
