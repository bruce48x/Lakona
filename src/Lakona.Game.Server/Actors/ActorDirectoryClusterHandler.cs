using System.Text.Json;
using Lakona.Game.Cluster;

namespace Lakona.Game.Server.Actors;

internal sealed class ActorDirectoryClusterHandler : IClusterMessageHandler
{
    private readonly IActorDirectory _directory;
    private readonly IClusterNodeSender _nodeSender;
    private readonly LocalActorNodeIdentity _localNode;

    public ActorDirectoryClusterHandler(
        IActorDirectory directory,
        IClusterNodeSender nodeSender,
        LocalActorNodeIdentity localNode)
    {
        _directory = directory ?? throw new ArgumentNullException(nameof(directory));
        _nodeSender = nodeSender ?? throw new ArgumentNullException(nameof(nodeSender));
        _localNode = localNode ?? throw new ArgumentNullException(nameof(localNode));
    }

    public async ValueTask<ClusterSendStatus> HandleAsync(
        ClusterMessage message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (message.Kind != ActorDirectoryClusterProtocol.ResolveKind &&
            message.Kind != ActorDirectoryClusterProtocol.RegisterKind &&
            message.Kind != ActorDirectoryClusterProtocol.UnregisterKind)
        {
            return ClusterSendStatus.RouteNotFound;
        }

        ActorDirectoryRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<ActorDirectoryRequest>(message.Payload.Span);
        }
        catch (JsonException)
        {
            return await SendReplyAsync(
                message,
                new ActorDirectoryReply(
                    ActorDirectoryOperationStatus.InvalidRequest,
                    Error: "Actor directory request payload is invalid."),
                cancellationToken).ConfigureAwait(false);
        }

        if (request is null)
        {
            return await SendReplyAsync(
                message,
                new ActorDirectoryReply(
                    ActorDirectoryOperationStatus.InvalidRequest,
                    Error: "Actor directory request payload is empty."),
                cancellationToken).ConfigureAwait(false);
        }

        if (string.IsNullOrWhiteSpace(request.ActorId))
        {
            return await SendReplyAsync(
                message,
                new ActorDirectoryReply(
                    ActorDirectoryOperationStatus.InvalidRequest,
                    Error: "Actor directory actor id is required."),
                cancellationToken).ConfigureAwait(false);
        }

        if ((message.Kind == ActorDirectoryClusterProtocol.RegisterKind ||
             message.Kind == ActorDirectoryClusterProtocol.UnregisterKind) &&
            string.IsNullOrWhiteSpace(request.OwnerNode))
        {
            return await SendReplyAsync(
                message,
                new ActorDirectoryReply(
                    ActorDirectoryOperationStatus.InvalidRequest,
                    Error: "Actor directory owner node is required."),
                cancellationToken).ConfigureAwait(false);
        }

        ActorDirectoryReply reply;
        if (message.Kind == ActorDirectoryClusterProtocol.ResolveKind)
        {
            var record = await _directory.ResolveAsync(
                ActorId.From(request.ActorId),
                cancellationToken).ConfigureAwait(false);
            reply = record is null
                ? new ActorDirectoryReply(ActorDirectoryOperationStatus.NotFound)
                : new ActorDirectoryReply(
                    ActorDirectoryOperationStatus.Succeeded,
                    new ActorDirectoryRecordDto(
                        record.ActorId.Value,
                        record.Node.Value,
                        record.Version,
                        record.UpdatedAt));
        }
        else if (message.Kind == ActorDirectoryClusterProtocol.RegisterKind)
        {
            var status = await _directory.RegisterAsync(
                ActorId.From(request.ActorId),
                new NodeId(request.OwnerNode!),
                cancellationToken).ConfigureAwait(false);
            reply = new ActorDirectoryReply(status switch
            {
                ActorDirectoryRegisterStatus.Registered => ActorDirectoryOperationStatus.Registered,
                ActorDirectoryRegisterStatus.AlreadyRegistered => ActorDirectoryOperationStatus.AlreadyRegistered,
                ActorDirectoryRegisterStatus.Conflict => ActorDirectoryOperationStatus.Conflict,
                _ => ActorDirectoryOperationStatus.Failed
            });
        }
        else
        {
            var status = await _directory.UnregisterAsync(
                ActorId.From(request.ActorId),
                new NodeId(request.OwnerNode!),
                cancellationToken).ConfigureAwait(false);
            reply = new ActorDirectoryReply(status switch
            {
                ActorDirectoryUnregisterStatus.Unregistered => ActorDirectoryOperationStatus.Unregistered,
                ActorDirectoryUnregisterStatus.NotFound => ActorDirectoryOperationStatus.NotFound,
                ActorDirectoryUnregisterStatus.OwnershipMismatch => ActorDirectoryOperationStatus.OwnershipMismatch,
                _ => ActorDirectoryOperationStatus.Failed
            });
        }

        return await SendReplyAsync(message, reply, cancellationToken).ConfigureAwait(false);
    }

    private ValueTask<ClusterSendStatus> SendReplyAsync(
        ClusterMessage message,
        ActorDirectoryReply reply,
        CancellationToken cancellationToken)
    {
        return RemoteActorGateway.SendReplyAsync(
            _nodeSender,
            _localNode.NodeId,
            message.SourceNode,
            message.CorrelationId!,
            JsonSerializer.SerializeToUtf8Bytes(reply),
            cancellationToken);
    }
}
