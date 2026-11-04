using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc;
using Microsoft.Extensions.Caching.Memory;

namespace Lakona.Game.Server.Sessions;

internal interface IGameSessionNotificationProfileLookup
{
    ValueTask<string?> GetLocalAsync(GameSessionKey session, CancellationToken cancellationToken = default);
    ValueTask<string?> GetRemoteAsync(RouteLocation target, GameSessionKey session, CancellationToken cancellationToken = default);
}

internal sealed class GameSessionNotificationProfileLookup(
    IGameSessionRegistry sessions,
    IClusterClientFactory? clients = null) : IGameSessionNotificationProfileLookup, IDisposable
{
    // The owner pins the profile for the complete session generation. Its locator
    // includes the gateway incarnation. Delivery still checks the authoritative owner.
    private readonly MemoryCache remoteProfiles = new(new MemoryCacheOptions { SizeLimit = 4096 });

    public ValueTask<string?> GetLocalAsync(GameSessionKey session, CancellationToken cancellationToken = default) =>
        sessions.GetNotificationSerializerProfileAsync(session, cancellationToken);

    public async ValueTask<string?> GetRemoteAsync(RouteLocation target, GameSessionKey session,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (clients is null || !MembershipSessionLocator.TryDecode(session.SessionId, out var gateway) ||
            gateway != target.NodeReference)
            return null;

        if (remoteProfiles.TryGetValue(session, out string? cached)) return cached;

        var client = await clients.GetClientAsync(target, cancellationToken).ConfigureAwait(false);
        var reply = await client.CallAsync(SessionNotificationProfileProtocol.Lookup, session, cancellationToken)
            .ConfigureAwait(false);
        if (reply?.Profile is not { Length: > 0 } profile) return null;

        remoteProfiles.Set(session, profile, new MemoryCacheEntryOptions { Size = 1 });
        return profile;
    }

    public void Dispose() => remoteProfiles.Dispose();
}
