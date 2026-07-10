using Lakona.Game.Cluster;

namespace Lakona.Game.Server.Actors;

internal static class ActorDirectoryClusterProtocol
{
    public static readonly RouteKey Route = new("actor-directory:command");
    public const string ResolveKind = "_actor_directory_resolve";
    public const string RegisterKind = "_actor_directory_register";
    public const string UnregisterKind = "_actor_directory_unregister";
}

internal sealed record ActorDirectoryRequest(string ActorId, string? OwnerNode);

internal sealed record ActorDirectoryRecordDto(
    string ActorId,
    string Node,
    long Version,
    DateTimeOffset UpdatedAt);

internal enum ActorDirectoryOperationStatus
{
    Succeeded,
    Registered,
    AlreadyRegistered,
    Conflict,
    Unregistered,
    NotFound,
    OwnershipMismatch,
    InvalidRequest,
    Failed
}

internal sealed record ActorDirectoryReply(
    ActorDirectoryOperationStatus Status,
    ActorDirectoryRecordDto? Record = null,
    string? Error = null);
