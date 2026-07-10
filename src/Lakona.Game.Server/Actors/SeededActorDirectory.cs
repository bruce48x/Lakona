using System.Text.Json;
using Lakona.Game.Cluster;

namespace Lakona.Game.Server.Actors;

public sealed class SeededActorDirectory : IActorDirectory
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    private readonly RemoteActorGateway _gateway;
    private readonly INodeMessenger _messenger;
    private readonly LocalActorNodeIdentity _localNode;
    private readonly RouteLocation _seedTarget;

    public SeededActorDirectory(
        RemoteActorGateway gateway,
        INodeMessenger messenger,
        LocalActorNodeIdentity localNode,
        string seedEndpoint)
    {
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        _messenger = messenger ?? throw new ArgumentNullException(nameof(messenger));
        _localNode = localNode ?? throw new ArgumentNullException(nameof(localNode));
        _seedTarget = CreateSeedTarget(seedEndpoint);
    }

    public async ValueTask<ActorDirectoryRecord?> ResolveAsync(
        ActorId actorId,
        CancellationToken cancellationToken = default)
    {
        var reply = await ExecuteAsync(
            ActorDirectoryClusterProtocol.ResolveKind,
            new ActorDirectoryRequest(actorId.Value, null),
            cancellationToken).ConfigureAwait(false);

        if (reply.Status == ActorDirectoryOperationStatus.NotFound)
        {
            return null;
        }

        if (reply.Status != ActorDirectoryOperationStatus.Succeeded || reply.Record is null)
        {
            throw new ActorDirectoryUnavailableException("Actor directory returned an invalid resolve reply.");
        }

        return new ActorDirectoryRecord(
            ActorId.From(reply.Record.ActorId),
            new NodeId(reply.Record.Node),
            reply.Record.Version,
            reply.Record.UpdatedAt);
    }

    public async ValueTask<ActorDirectoryRegisterStatus> RegisterAsync(
        ActorId actorId,
        NodeId node,
        CancellationToken cancellationToken = default)
    {
        var reply = await ExecuteAsync(
            ActorDirectoryClusterProtocol.RegisterKind,
            new ActorDirectoryRequest(actorId.Value, node.Value),
            cancellationToken).ConfigureAwait(false);

        return ToRegisterStatus(reply.Status);
    }

    public async ValueTask<ActorDirectoryUnregisterStatus> UnregisterAsync(
        ActorId actorId,
        NodeId node,
        CancellationToken cancellationToken = default)
    {
        var reply = await ExecuteAsync(
            ActorDirectoryClusterProtocol.UnregisterKind,
            new ActorDirectoryRequest(actorId.Value, node.Value),
            cancellationToken).ConfigureAwait(false);

        return ToUnregisterStatus(reply.Status);
    }

    private async ValueTask<ActorDirectoryReply> ExecuteAsync(
        string kind,
        ActorDirectoryRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var correlationId = Guid.NewGuid().ToString("N");
        var pending = _gateway.RegisterPendingAsync(correlationId, RequestTimeout, cancellationToken);
        ClusterSendStatus status;
        try
        {
            status = await _messenger.SendAsync(
                _seedTarget,
                new ClusterMessage(
                    ActorDirectoryClusterProtocol.Route,
                    kind,
                    JsonSerializer.SerializeToUtf8Bytes(request),
                    DateTimeOffset.UtcNow.Add(RequestTimeout),
                    _localNode.NodeId,
                    correlationId,
                    orderedBy: request.ActorId),
                cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            _gateway.TryCancelPending(correlationId);
            throw;
        }

        if (status != ClusterSendStatus.Accepted)
        {
            _gateway.TryCancelPending(correlationId);
            throw new ActorDirectoryUnavailableException(
                $"Actor directory send failed with cluster status: {status}.");
        }

        var payload = await pending.ConfigureAwait(false);
        try
        {
            var reply = JsonSerializer.Deserialize<ActorDirectoryReply>(payload.Span)
                ?? throw new ActorDirectoryUnavailableException("Actor directory returned an empty reply.");

            if (!string.IsNullOrWhiteSpace(reply.Error))
            {
                throw new ActorDirectoryUnavailableException(
                    $"Actor directory returned an error: {reply.Error}");
            }

            return reply;
        }
        catch (JsonException exception)
        {
            throw new ActorDirectoryUnavailableException(
                "Actor directory returned a malformed reply.",
                exception);
        }
    }

    private static RouteLocation CreateSeedTarget(string endpoint) => new(
        ActorDirectoryClusterProtocol.Route,
        new NodeId("actor-directory-seed"),
        new NodeEndpoint(endpoint),
        DateTimeOffset.MaxValue);

    private static ActorDirectoryRegisterStatus ToRegisterStatus(
        ActorDirectoryOperationStatus status) => status switch
        {
            ActorDirectoryOperationStatus.Registered => ActorDirectoryRegisterStatus.Registered,
            ActorDirectoryOperationStatus.AlreadyRegistered => ActorDirectoryRegisterStatus.AlreadyRegistered,
            ActorDirectoryOperationStatus.Conflict => ActorDirectoryRegisterStatus.Conflict,
            _ => throw new ActorDirectoryUnavailableException(
                "Actor directory returned an invalid register status.")
        };

    private static ActorDirectoryUnregisterStatus ToUnregisterStatus(
        ActorDirectoryOperationStatus status) => status switch
        {
            ActorDirectoryOperationStatus.Unregistered => ActorDirectoryUnregisterStatus.Unregistered,
            ActorDirectoryOperationStatus.NotFound => ActorDirectoryUnregisterStatus.NotFound,
            ActorDirectoryOperationStatus.OwnershipMismatch => ActorDirectoryUnregisterStatus.OwnershipMismatch,
            _ => throw new ActorDirectoryUnavailableException(
                "Actor directory returned an invalid unregister status.")
        };
}
