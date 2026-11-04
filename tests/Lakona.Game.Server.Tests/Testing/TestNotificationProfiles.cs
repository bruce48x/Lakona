using Lakona.Game.Cluster;
using Lakona.Game.Server.Hosting;
using Lakona.Game.Server.Sessions;
using Lakona.Rpc.Serializer.Json;

namespace Lakona.Game.Server.Tests.Testing;

internal sealed class TestNotificationProfiles : IGameSessionNotificationProfileLookup
{
    public static ClientNotificationCommandFactory JsonCommands() => new(
        new TestNotificationProfiles(),
        new LakonaEndpointRuntimeRegistry([], [new("json", static () => new JsonRpcSerializer())]));

    public ValueTask<string?> GetLocalAsync(GameSessionKey session, CancellationToken cancellationToken = default)
        => new("json");

    public ValueTask<string?> GetRemoteAsync(RouteLocation target, GameSessionKey session,
        CancellationToken cancellationToken = default) => new("json");
}
