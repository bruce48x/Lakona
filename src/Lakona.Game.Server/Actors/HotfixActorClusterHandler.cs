using Lakona.Game.Cluster;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Dispatch;
using Microsoft.Extensions.DependencyInjection;

namespace Lakona.Game.Server.Actors;

public sealed class HotfixActorClusterHandler : IClusterMessageHandler
{
    private readonly IActorRuntime _runtime;
    private readonly IRemoteActorSerializer _serializer;
    private readonly IClusterRouter _router;
    private readonly IServiceProvider _services;

    public HotfixActorClusterHandler(
        IActorRuntime runtime,
        IRemoteActorSerializer serializer,
        IClusterRouter router,
        IServiceProvider services)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public async ValueTask<ClusterSendStatus> HandleAsync(
        ClusterMessage message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (!ClusterActorEnvelope.TryFromClusterMessage(message, out var envelope) ||
            envelope is null ||
            !envelope.Metadata.TryGetValue(HotfixActorApiMetadata.MethodKeyKey, out var methodKey) ||
            string.IsNullOrWhiteSpace(methodKey))
        {
            return ClusterSendStatus.RouteNotFound;
        }

        var accessor = _services.GetService<IHotfixRuntimeAccessor>();
        if (accessor is null)
        {
            return ClusterSendStatus.RouteNotFound;
        }

        HotfixRuntimeSnapshotLease lease;
        try
        {
            lease = accessor.AcquireCurrent();
        }
        catch (ObjectDisposedException)
        {
            return ClusterSendStatus.RouteNotFound;
        }

        var snapshot = lease.Snapshot;
        var table = snapshot.DispatchTable;
        if (table is null || !table.TryResolveActorMethod(methodKey, out var descriptor))
        {
            lease.Dispose();
            return ClusterSendStatus.RouteNotFound;
        }

        object? request;
        try
        {
            request = _serializer.Deserialize(envelope.Payload, descriptor.RequestType);
        }
        catch
        {
            lease.Dispose();
            return ClusterSendStatus.DeserializationFailed;
        }

        var actorId = ActorId.From(envelope.ActorId);
        if (descriptor.ResultType is null)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                lease.Dispose();
                cancellationToken.ThrowIfCancellationRequested();
            }

            var state = new TellDispatchState(
                lease,
                table,
                methodKey,
                request);
            ActorTellResult result;
            try
            {
                result = _runtime.TryTell(
                    descriptor.ActorType,
                    actorId,
                    async (actor, ct) =>
                    {
                        try
                        {
                            await state.Table.InvokeActorAsync(
                                state.MethodKey,
                                actor,
                                state.Request,
                                expectedResultType: null,
                                ct).ConfigureAwait(false);
                        }
                        finally
                        {
                            state.Lease.Dispose();
                        }
                    },
                    CancellationToken.None);
            }
            catch
            {
                lease.Dispose();
                return ClusterSendStatus.Failed;
            }

            if (result != ActorTellResult.Accepted)
            {
                lease.Dispose();
            }

            return MapTellResult(result);
        }

        try
        {
            object? result;
            try
            {
                result = await _runtime.AskAsync(
                    descriptor.ActorType,
                    actorId,
                    async (actor, ct) => await table.InvokeActorAsync(
                        methodKey,
                        actor,
                        request,
                        descriptor.ResultType,
                        ct).ConfigureAwait(false),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (ActorCallException exception)
            {
                return MapCallException(exception);
            }
            catch
            {
                return ClusterSendStatus.Failed;
            }

            if (envelope.ReplyCorrelationId is not null)
            {
                ReadOnlyMemory<byte> replyPayload;
                try
                {
                    replyPayload = _serializer.Serialize(result, descriptor.ResultType);
                }
                catch
                {
                    return ClusterSendStatus.SerializationFailed;
                }

                await RemoteActorGateway.SendReplyAsync(
                    _router,
                    envelope.SourceNode,
                    envelope.ReplyCorrelationId,
                    replyPayload,
                    cancellationToken).ConfigureAwait(false);
            }

            return ClusterSendStatus.Accepted;
        }
        finally
        {
            lease.Dispose();
        }
    }

    private static ClusterSendStatus MapTellResult(ActorTellResult result)
    {
        return result switch
        {
            ActorTellResult.ActorNotFound => ClusterSendStatus.RouteNotFound,
            ActorTellResult.MailboxFull => ClusterSendStatus.Backpressure,
            ActorTellResult.ActorUnavailable => ClusterSendStatus.HandlerUnavailable,
            _ => ClusterSendStatus.Accepted
        };
    }

    private static ClusterSendStatus MapCallException(ActorCallException exception)
    {
        return exception.Status switch
        {
            ActorCallStatus.ActorNotFound => ClusterSendStatus.RouteNotFound,
            ActorCallStatus.Backpressure => ClusterSendStatus.Backpressure,
            ActorCallStatus.Timeout => ClusterSendStatus.Timeout,
            ActorCallStatus.Expired => ClusterSendStatus.Expired,
            _ => ClusterSendStatus.Failed
        };
    }

    private sealed record TellDispatchState(
        HotfixRuntimeSnapshotLease Lease,
        HotfixDispatchTable Table,
        string MethodKey,
        object? Request);
}
