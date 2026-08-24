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
        var attached = await AttachActivationAsync(invocation, cancellationToken).ConfigureAwait(false);
        return await transport.AskAsync(attached, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<RemoteActorInvocationResult> TellAsync(
        RemoteActorInvocation invocation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        var attached = await AttachActivationAsync(invocation, cancellationToken).ConfigureAwait(false);
        return await transport.TellAsync(attached, cancellationToken).ConfigureAwait(false);
    }

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
            || record is null
            || record.Node != invocation.Node)
        {
            record = await directory.ResolveAsync(invocation.ActorId, cancellationToken)
                .ConfigureAwait(false);
            if (record is null || record.Node != invocation.Node)
            {
                directoryCache?.Remove(invocation.ActorId);
                return invocation;
            }

            directoryCache?.Set(record);
        }

        return invocation.WithActivation(record);
    }
}
