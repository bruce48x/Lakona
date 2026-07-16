using System.Reflection;

namespace Lakona.Game.Server.Hotfix.Dispatch;

public sealed class HotfixTimerMethodDescriptor
{
    internal HotfixTimerMethodDescriptor(
        string methodKey,
        Type callbackType,
        Type argsType,
        MethodInfo method)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(methodKey);
        ArgumentNullException.ThrowIfNull(callbackType);
        ArgumentNullException.ThrowIfNull(argsType);
        ArgumentNullException.ThrowIfNull(method);

        MethodKey = methodKey;
        MethodId = HotfixActorApiMetadata.CreateMethodId(methodKey);
        CallbackType = callbackType;
        ArgsType = argsType;
        Method = method;
        Invoker = HotfixTimerMethodInvoker.Create(callbackType, argsType, method);
    }

    public string MethodKey { get; }

    public ulong MethodId { get; }

    public Type CallbackType { get; }

    public Type ArgsType { get; }

    public string MethodName => Method.Name;

    internal MethodInfo Method { get; }

    internal IHotfixTimerMethodInvoker Invoker { get; }
}
