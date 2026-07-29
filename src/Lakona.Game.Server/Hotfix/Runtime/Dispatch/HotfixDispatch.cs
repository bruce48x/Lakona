using Lakona.Game.Server.Hotfix.Abstractions;

namespace Lakona.Game.Server.Hotfix.Dispatch;

public static class HotfixDispatch
{
    private static HotfixDispatchTable current = new(0, Array.Empty<HotfixMethodBinding>());
    private static Func<HotfixDispatchTable>? currentProvider;

    public static HotfixDispatchTable Current
    {
        get
        {
            var provider = Volatile.Read(ref currentProvider);
            return provider is null ? CurrentFallback : provider();
        }
    }

    internal static HotfixDispatchTable CurrentFallback => Volatile.Read(ref current);

    internal static HotfixDispatchTable ActiveTable => HotfixDispatchRuntimeScope.CurrentTable ?? Current;

    public static void Replace(HotfixDispatchTable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        Volatile.Write(ref currentProvider, null);
        Interlocked.Exchange(ref current, table);
    }

    internal static void ReplaceProvider(Func<HotfixDispatchTable> provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        Volatile.Write(ref currentProvider, provider);
    }

    internal static void RemoveProvider(Func<HotfixDispatchTable> provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        Interlocked.CompareExchange(ref currentProvider, null, provider);
    }

    public static string CreateServiceKey<TContract, TResult>(int methodId, params Type[] parameterTypes)
    {
        return CreateServiceKey(typeof(TContract), methodId, typeof(TResult), parameterTypes);
    }

    public static async ValueTask InvokeActorAsync(
        ulong methodId,
        object actor,
        object? request,
        CancellationToken cancellationToken = default)
    {
        var result = await ActiveTable.InvokeActorAsync(
            methodId,
            actor,
            request,
            typeof(void),
            cancellationToken).ConfigureAwait(false);
        if (result is not null)
        {
            throw new InvalidOperationException($"Hotfix actor method id '{methodId}' returned a result from a resultless invocation.");
        }
    }

    public static async ValueTask<TResult> InvokeActorAsync<TResult>(
        ulong methodId,
        object actor,
        object? request,
        CancellationToken cancellationToken = default)
    {
        var result = await ActiveTable.InvokeActorAsync(
            methodId,
            actor,
            request,
            typeof(TResult),
            cancellationToken).ConfigureAwait(false);
        if (result is TResult typedResult)
        {
            return typedResult;
        }

        if (result is null && default(TResult) is null)
        {
            return default!;
        }

        throw new InvalidOperationException($"Hotfix actor method id '{methodId}' returned an invalid result.");
    }

    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static async ValueTask InvokeActorAsync(
        IHotfixRuntimeAccessor runtimeAccessor,
        ulong methodId,
        object actor,
        object? request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runtimeAccessor);
        using var lease = runtimeAccessor.AcquireCurrent();
        var table = lease.Snapshot.DispatchTable ?? Current;
        var result = await table.InvokeActorAsync(
            methodId,
            actor,
            request,
            typeof(void),
            cancellationToken).ConfigureAwait(false);
        if (result is not null)
        {
            throw new InvalidOperationException($"Hotfix actor method id '{methodId}' returned a result from a resultless invocation.");
        }
    }

    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static async ValueTask<TResult> InvokeActorAsync<TResult>(
        IHotfixRuntimeAccessor runtimeAccessor,
        ulong methodId,
        object actor,
        object? request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runtimeAccessor);
        using var lease = runtimeAccessor.AcquireCurrent();
        var table = lease.Snapshot.DispatchTable ?? Current;
        var result = await table.InvokeActorAsync(
            methodId,
            actor,
            request,
            typeof(TResult),
            cancellationToken).ConfigureAwait(false);
        if (result is TResult typedResult)
        {
            return typedResult;
        }

        if (result is null && default(TResult) is null)
        {
            return default!;
        }

        throw new InvalidOperationException($"Hotfix actor method id '{methodId}' returned an invalid result.");
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
}
