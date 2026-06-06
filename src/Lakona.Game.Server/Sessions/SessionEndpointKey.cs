using Lakona.Game.Abstractions;

namespace Lakona.Game.Server.Sessions;

public readonly record struct SessionEndpointKey(
    GameSessionKey Session,
    GameEndpointName EndpointName);
