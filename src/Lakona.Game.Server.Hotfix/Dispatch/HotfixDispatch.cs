using Lakona.Game.Server.Hotfix.Abstractions;

namespace Lakona.Game.Server.Hotfix.Dispatch;

public static class HotfixDispatch
{
    private static HotfixDispatchTable current = new(0, Array.Empty<HotfixMethodBinding>());

    public static HotfixDispatchTable Current => Volatile.Read(ref current);

    public static void Replace(HotfixDispatchTable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        Interlocked.Exchange(ref current, table);
    }

    public static HotfixMethodKey CreateKey<TState, TResult>(string methodName, params Type[] parameterTypes)
    {
        return CreateKey(typeof(TState), methodName, typeof(TResult), parameterTypes);
    }

    public static string CreateServiceKey<TContract, TResult>(string methodName, params Type[] parameterTypes)
    {
        return CreateServiceKey(typeof(TContract), methodName, typeof(TResult), parameterTypes);
    }

    public static string CreateServiceKey<TContract, TResult>(int methodId, params Type[] parameterTypes)
    {
        return CreateServiceKey(typeof(TContract), methodId, typeof(TResult), parameterTypes);
    }

    public static TResult Invoke<TState, TResult>(string methodName, TState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var table = Current;
        var key = CreateKey<TState, TResult>(methodName);
        return table.Resolve<TState, TResult>(key)(state);
    }

    public static TResult Invoke<TState, TArg, TResult>(string methodName, TState state, TArg arg)
    {
        ArgumentNullException.ThrowIfNull(state);

        var table = Current;
        var key = CreateKey<TState, TResult>(methodName, typeof(TArg));
        return table.Resolve<TState, TArg, TResult>(key)(state, arg);
    }

    public static void Invoke<TState>(
        string methodName,
        TState state,
        Type[] parameterTypes,
        object?[] arguments)
    {
        var invocation = PrepareInvocation<TState>(methodName, state, typeof(void), parameterTypes, arguments);

        if (invocation.Method.ReturnType != typeof(void))
        {
            throw new InvalidOperationException(
                $"Hotfix method '{invocation.Key}' returns '{invocation.Method.ReturnType.FullName ?? invocation.Method.ReturnType.Name}' and cannot be invoked through the void dispatch overload.");
        }

        var result = invocation.Method.Invoke(null, invocation.Arguments);
        if (result is not null)
        {
            throw new InvalidOperationException($"Hotfix method '{invocation.Key}' returned a result from the void dispatch overload.");
        }
    }

    public static TResult Invoke<TState, TResult>(
        string methodName,
        TState state,
        Type[] parameterTypes,
        object?[] arguments)
    {
        var invocation = PrepareInvocation<TState>(methodName, state, typeof(TResult), parameterTypes, arguments);
        var result = invocation.Method.Invoke(null, invocation.Arguments);
        if (result is TResult typedResult)
        {
            return typedResult;
        }

        if (result is null && default(TResult) is null)
        {
            return default!;
        }

        throw new InvalidOperationException($"Hotfix method '{invocation.Key}' returned an invalid result.");
    }

    private static PreparedInvocation PrepareInvocation<TState>(
        string methodName,
        TState state,
        Type returnType,
        Type[] parameterTypes,
        object?[] arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(methodName);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(returnType);
        ArgumentNullException.ThrowIfNull(parameterTypes);
        ArgumentNullException.ThrowIfNull(arguments);

        if (parameterTypes.Length != arguments.Length)
        {
            throw new ArgumentException(
                "Parameter type count must match argument count.",
                nameof(arguments));
        }

        if (parameterTypes.Any(static type => type is null))
        {
            throw new ArgumentException("Parameter types cannot contain null.", nameof(parameterTypes));
        }

        var table = Current;
        var key = CreateKey(typeof(TState), methodName, returnType, parameterTypes);
        var method = table.Resolve(key);
        var invokeArguments = new object?[arguments.Length + 1];
        invokeArguments[0] = state;
        Array.Copy(arguments, 0, invokeArguments, 1, arguments.Length);

        return new PreparedInvocation(key, method, invokeArguments);
    }

    private static HotfixMethodKey CreateKey(
        Type stateType,
        string methodName,
        Type returnType,
        Type[] parameterTypes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(methodName);
        ArgumentNullException.ThrowIfNull(stateType);
        ArgumentNullException.ThrowIfNull(returnType);
        ArgumentNullException.ThrowIfNull(parameterTypes);

        if (parameterTypes.Any(static type => type is null))
        {
            throw new ArgumentException("Parameter types cannot contain null.", nameof(parameterTypes));
        }

        return new HotfixMethodKey(
            stateType.FullName ?? stateType.Name,
            methodName,
            returnType.FullName ?? returnType.Name,
            parameterTypes.Select(static type => type.FullName ?? type.Name).ToArray());
    }

    internal static string CreateServiceKey(
        Type contractType,
        string methodName,
        Type returnType,
        Type[] parameterTypes)
    {
        ArgumentNullException.ThrowIfNull(contractType);
        ArgumentException.ThrowIfNullOrWhiteSpace(methodName);
        ArgumentNullException.ThrowIfNull(returnType);
        ArgumentNullException.ThrowIfNull(parameterTypes);

        if (parameterTypes.Any(static type => type is null))
        {
            throw new ArgumentException("Parameter types cannot contain null.", nameof(parameterTypes));
        }

        return $"{contractType.FullName ?? contractType.Name}.{methodName}({string.Join(", ", parameterTypes.Select(static type => type.FullName ?? type.Name))}) -> {returnType.FullName ?? returnType.Name}";
    }

    internal static string CreateServiceKey(
        Type contractType,
        int methodId,
        Type returnType,
        Type[] parameterTypes)
    {
        ArgumentNullException.ThrowIfNull(contractType);
        if (methodId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(methodId), "RPC method id must be positive.");
        }

        ArgumentNullException.ThrowIfNull(returnType);
        ArgumentNullException.ThrowIfNull(parameterTypes);

        if (parameterTypes.Any(static type => type is null))
        {
            throw new ArgumentException("Parameter types cannot contain null.", nameof(parameterTypes));
        }

        return $"{contractType.FullName ?? contractType.Name}#{methodId}({string.Join(", ", parameterTypes.Select(static type => type.FullName ?? type.Name))}) -> {returnType.FullName ?? returnType.Name}";
    }

    private sealed record PreparedInvocation(
        HotfixMethodKey Key,
        System.Reflection.MethodInfo Method,
        object?[] Arguments);
}
