namespace Lakona.Game.Server.Actors;

public sealed record ActorHostCreateRequest(
    string Actor,
    string ActorId,
    string Mode,
    string BuildTag,
    string? ClusterIncarnation = null,
    string? NodeIncarnation = null,
    string? ActivationId = null);

public sealed record ActorHostCreateReply(
    bool Succeeded,
    string? OwnerNode,
    string Message);
