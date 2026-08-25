namespace Lakona.Game.Server.Actors;

public sealed class RemoteActorInvoker : IRemoteActorInvoker
{
    private readonly IClusterActorTransport transport;
    private readonly IActorDirectory? directory;
    private readonly IActorDirectoryCache? directoryCache;

    internal RemoteActorInvoker(
        IClusterActorTransport transport,
        IActorDirectory? directory = null,
        IActorDirectoryCache? directoryCache = null)
    {
        this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
        this.directory = directory;
        this.directoryCache = directoryCache;
    }

    public async ValueTask<RemoteActorInvocationResult> AskAsync(
        RemoteActorInvocation invocation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        return await InvokeAsync(invocation, ask: true, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<RemoteActorInvocationResult> TellAsync(
        RemoteActorInvocation invocation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        return await InvokeAsync(invocation, ask: false, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<RemoteActorInvocationResult> InvokeAsync(
        RemoteActorInvocation invocation,
        bool ask,
        CancellationToken cancellationToken)
    {
        var mayReresolve = invocation.OwnerReference is null || invocation.ActivationId is null;
        var attached = await AttachActivationAsync(invocation, cancellationToken).ConfigureAwait(false);
        var result = ask
            ? await transport.AskAsync(attached, cancellationToken).ConfigureAwait(false)
            : await transport.TellAsync(attached, cancellationToken).ConfigureAwait(false);
        if (!mayReresolve || directory is null || !IsSafeStaleRoute(result)) return result;

        directoryCache?.Remove(invocation.ActorId);
        var refreshed = await AttachActivationAsync(invocation, cancellationToken).ConfigureAwait(false);
        if (refreshed.OwnerReference is null || refreshed.ActivationId is null) return result;
        return ask
            ? await transport.AskAsync(refreshed, cancellationToken).ConfigureAwait(false)
            : await transport.TellAsync(refreshed, cancellationToken).ConfigureAwait(false);
    }

    private static bool IsSafeStaleRoute(RemoteActorInvocationResult result) =>
        result.RetrySafety == RemoteActorRetrySafety.DefinitelyNotExecuted
        && result.Status is RemoteActorStatus.RouteNotFound
            or RemoteActorStatus.NodeUnavailable
            or RemoteActorStatus.HandlerUnavailable;

    private async ValueTask<RemoteActorInvocation> AttachActivationAsync(
        RemoteActorInvocation invocation,
        CancellationToken cancellationToken)
    {
        if (invocation.OwnerReference is not null
            && invocation.ActivationId is not null)
        {
            return invocation;
        }

        if (directory is null)
        {
            return invocation;
        }

        ActorDirectoryRecord? record = null;
        if (directoryCache is null
            || !directoryCache.TryGetRecord(invocation.ActorId, out record)
            || record is null)
        {
            record = await directory.ResolveAsync(invocation.ActorId, cancellationToken)
                .ConfigureAwait(false);
            if (record is null)
            {
                directoryCache?.Remove(invocation.ActorId);
                return invocation;
            }

            directoryCache?.Set(record);
        }

        return invocation.WithActivation(record);
    }
}
