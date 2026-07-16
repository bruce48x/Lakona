using Lakona.Game.Server.Hotfix.Abstractions.Timers;

namespace Lakona.Game.Server.Hotfix.Timers;

internal sealed class LakonaTimerDescriptor
{
    public LakonaTimerDescriptor(
        TimerId timerId,
        string callbackAssemblyName,
        string callbackFullName,
        string methodName,
        string argsAssemblyName,
        string argsFullName,
        string serializerId,
        ReadOnlyMemory<byte> jsonPayload,
        DateTimeOffset nextDueAtUtc,
        TimeSpan? period,
        long generation)
        : this(
            timerId,
            callbackAssemblyName,
            callbackFullName,
            methodName,
            HotfixActorApiMetadata.CreateMethodId(
                $"timer:{callbackFullName}, {callbackAssemblyName}|method:{methodName}|args:{argsFullName}, {argsAssemblyName}"),
            argsAssemblyName,
            argsFullName,
            serializerId,
            jsonPayload,
            nextDueAtUtc,
            period,
            generation)
    {
    }

    public LakonaTimerDescriptor(
        TimerId timerId,
        string callbackAssemblyName,
        string callbackFullName,
        string methodName,
        ulong methodId,
        string argsAssemblyName,
        string argsFullName,
        string serializerId,
        ReadOnlyMemory<byte> jsonPayload,
        DateTimeOffset nextDueAtUtc,
        TimeSpan? period,
        long generation)
    {
        TimerId = timerId;
        CallbackAssemblyName = RequireNonWhiteSpace(callbackAssemblyName, nameof(callbackAssemblyName));
        CallbackFullName = RequireNonWhiteSpace(callbackFullName, nameof(callbackFullName));
        MethodName = RequireNonWhiteSpace(methodName, nameof(methodName));
        MethodId = methodId;
        ArgsAssemblyName = RequireNonWhiteSpace(argsAssemblyName, nameof(argsAssemblyName));
        ArgsFullName = RequireNonWhiteSpace(argsFullName, nameof(argsFullName));
        SerializerId = RequireNonWhiteSpace(serializerId, nameof(serializerId));
        JsonPayload = jsonPayload.ToArray();
        NextDueAtUtc = nextDueAtUtc;
        Period = period;
        Generation = generation;
    }

    public TimerId TimerId { get; }

    public string CallbackAssemblyName { get; }

    public string CallbackFullName { get; }

    public string MethodName { get; }

    public ulong MethodId { get; }

    public string ArgsAssemblyName { get; }

    public string ArgsFullName { get; }

    public string SerializerId { get; }

    public ReadOnlyMemory<byte> JsonPayload { get; }

    public DateTimeOffset NextDueAtUtc { get; }

    public TimeSpan? Period { get; }

    public long Generation { get; }

    private static string RequireNonWhiteSpace(string value, string parameterName)
    {
        return string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Value cannot be null or whitespace.", parameterName)
            : value;
    }
}
