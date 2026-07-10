using Lakona.Game.Cluster;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Dispatch;
using Microsoft.Extensions.DependencyInjection;
using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace Lakona.Game.Server.Actors;

public sealed class HotfixActorClusterHandler : IClusterMessageHandler
{
    private readonly IActorRuntime _runtime;
    private readonly IRemoteActorSerializer _serializer;
    private readonly IClusterNodeSender _nodeSender;
    private readonly LocalActorNodeIdentity _localNode;
    private readonly IServiceProvider _services;

    public HotfixActorClusterHandler(
        IActorRuntime runtime,
        IRemoteActorSerializer serializer,
        IClusterNodeSender nodeSender,
        LocalActorNodeIdentity localNode,
        IServiceProvider services)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _nodeSender = nodeSender ?? throw new ArgumentNullException(nameof(nodeSender));
        _localNode = localNode ?? throw new ArgumentNullException(nameof(localNode));
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public async ValueTask<ClusterSendStatus> HandleAsync(
        ClusterMessage message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (string.Equals(message.Kind, ActorHostClient.MessageKind, StringComparison.Ordinal))
        {
            return await HandleActorHostCreateAsync(message, cancellationToken).ConfigureAwait(false);
        }

        if (!ClusterActorEnvelope.TryFromClusterMessage(message, out var envelope) ||
            envelope is null ||
            !string.Equals(envelope.Kind, HotfixActorApiMetadata.ActorMessageKind, StringComparison.Ordinal) ||
            !envelope.Metadata.TryGetValue(HotfixActorApiMetadata.MethodIdKey, out var methodIdText) ||
            !ulong.TryParse(methodIdText, NumberStyles.None, CultureInfo.InvariantCulture, out var methodId))
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
        if (table is null || !table.TryResolveActorMethod(methodId, out var descriptor))
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
            if (envelope.ReplyCorrelationId is not null)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    lease.Dispose();
                    cancellationToken.ThrowIfCancellationRequested();
                }

                var callState = new ActorDispatchState(
                    lease,
                    table,
                    descriptor.MethodKey,
                    request);
                try
                {
                    await _runtime.TellAsync(
                        descriptor.ActorType,
                        actorId,
                        async (actor, ct) =>
                        {
                            try
                            {
                                await callState.Table.InvokeActorAsync(
                                    callState.MethodKey,
                                    actor,
                                    callState.Request,
                                    expectedResultType: null,
                                    ct).ConfigureAwait(false);
                            }
                            finally
                            {
                                callState.DisposeLease();
                            }
                        },
                        CancellationToken.None).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    callState.DisposeLease();
                    throw;
                }
                catch (ActorCallException exception)
                {
                    callState.DisposeLease();
                    return MapCallException(exception);
                }
                catch
                {
                    callState.DisposeLease();
                    return ClusterSendStatus.Failed;
                }

                return await RemoteActorGateway.SendReplyAsync(
                    _nodeSender,
                    _localNode.NodeId,
                    envelope.SourceNode,
                    envelope.ReplyCorrelationId,
                    ReadOnlyMemory<byte>.Empty,
                    cancellationToken).ConfigureAwait(false);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                lease.Dispose();
                cancellationToken.ThrowIfCancellationRequested();
            }

            var postState = new ActorDispatchState(
                lease,
                table,
                descriptor.MethodKey,
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
                            await postState.Table.InvokeActorAsync(
                                postState.MethodKey,
                                actor,
                                postState.Request,
                                expectedResultType: null,
                                ct).ConfigureAwait(false);
                        }
                        finally
                        {
                            postState.DisposeLease();
                        }
                    },
                    CancellationToken.None);
            }
            catch
            {
                postState.DisposeLease();
                return ClusterSendStatus.Failed;
            }

            if (result != ActorTellResult.Accepted)
            {
                postState.DisposeLease();
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
                        descriptor.MethodKey,
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

                return await RemoteActorGateway.SendReplyAsync(
                    _nodeSender,
                    _localNode.NodeId,
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

    private async ValueTask<ClusterSendStatus> HandleActorHostCreateAsync(
        ClusterMessage message,
        CancellationToken cancellationToken)
    {
        var hosting = _services.GetService<ActorHosting>();
        var accessor = _services.GetService<IHotfixRuntimeAccessor>();
        if (hosting is null || accessor is null || message.CorrelationId is null)
        {
            return ClusterSendStatus.RouteNotFound;
        }

        ActorHostCreateRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<ActorHostCreateRequest>(message.Payload.Span);
        }
        catch
        {
            return ClusterSendStatus.DeserializationFailed;
        }

        if (request is null)
        {
            return ClusterSendStatus.DeserializationFailed;
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

        ActorHostCreateReply reply;
        try
        {
            var actorType = ResolvePlacementActorType(lease.Snapshot, request.Actor);
            if (actorType is null)
            {
                return ClusterSendStatus.RouteNotFound;
            }

            var actorId = ActorId.From(request.ActorId);
            try
            {
                await InvokeHostingAsync(hosting, actorType, actorId, request.Mode, cancellationToken)
                    .ConfigureAwait(false);
                reply = new ActorHostCreateReply(true, _localNode.NodeId.Value, "created");
            }
            catch (ActorHostedElsewhereException ex)
            {
                reply = new ActorHostCreateReply(false, ex.OwnerNode.Value, ex.Message);
            }
            catch (Exception ex)
            {
                reply = new ActorHostCreateReply(false, null, ex.Message);
            }
        }
        finally
        {
            lease.Dispose();
        }

        return await RemoteActorGateway.SendReplyAsync(
            _nodeSender,
            _localNode.NodeId,
            message.SourceNode,
            message.CorrelationId,
            JsonSerializer.SerializeToUtf8Bytes(reply),
            cancellationToken).ConfigureAwait(false);
    }

    private static Type? ResolvePlacementActorType(
        HotfixRuntimeSnapshot snapshot,
        string actorName)
    {
        foreach (var placement in snapshot.ActorPlacements)
        {
            if (string.Equals(ResolveActorName(placement.ActorType), actorName, StringComparison.Ordinal))
            {
                return placement.ActorType;
            }
        }

        return null;
    }

    private static async ValueTask InvokeHostingAsync(
        ActorHosting hosting,
        Type actorType,
        ActorId actorId,
        string mode,
        CancellationToken cancellationToken)
    {
        var methodName = string.Equals(mode, "create", StringComparison.OrdinalIgnoreCase)
            ? nameof(ActorHosting.CreateAsync)
            : string.Equals(mode, "ensure", StringComparison.OrdinalIgnoreCase)
                ? nameof(ActorHosting.EnsureAsync)
                : throw new InvalidOperationException($"Unknown actor host create mode '{mode}'.");
        var method = typeof(ActorHosting)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Single(candidate => candidate.Name == methodName && candidate.IsGenericMethodDefinition);
        var task = (ValueTask)method
            .MakeGenericMethod(actorType)
            .Invoke(hosting, [actorId, cancellationToken])!;
        await task.ConfigureAwait(false);
    }

    private static string ResolveActorName(Type actorType)
    {
        var attribute = (ActorNameAttribute?)Attribute.GetCustomAttribute(
            actorType,
            typeof(ActorNameAttribute),
            inherit: false);
        return attribute?.Name ?? actorType.Name;
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

    private sealed class ActorDispatchState(
        HotfixRuntimeSnapshotLease lease,
        HotfixDispatchTable table,
        string methodKey,
        object? request)
    {
        private int _leaseDisposed;

        public HotfixDispatchTable Table { get; } = table;

        public string MethodKey { get; } = methodKey;

        public object? Request { get; } = request;

        public void DisposeLease()
        {
            if (Interlocked.Exchange(ref _leaseDisposed, 1) == 0)
            {
                lease.Dispose();
            }
        }
    }
}
