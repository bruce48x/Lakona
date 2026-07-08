namespace Lakona.Game.Server.Actors;

public sealed record ActorHostCreateRequest(
    string Actor,
    string ActorId,
    string Mode,
    string BuildTag);

public sealed record ActorHostCreateReply(
    bool Succeeded,
    string? OwnerNode,
    string Message);
