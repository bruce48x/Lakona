using Agar.Sample.State.Leaderboard;
using Agar.Sample.State.Users;
using Lakona.Game.Cluster;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Server.Hotfix.Services;
using System.Text.Json;

namespace Server.Hotfix.Features;

[HotfixFeature("state-store")]
public sealed class StateStoreFeature : HotfixGameFeature
{
    public override void Configure(HotfixFeatureContext context)
    {
        var services = GetServices(context);
        services.AddSingleton<MatchmakingNotifier>();
        services.AddSingleton<RoomNotifier>();
        services.AddSingleton<IFeatureMessageHandler, StateStoreFeatureMessageHandler>();
    }

    private static IServiceCollection GetServices(HotfixFeatureContext context)
    {
        return (IServiceCollection)(context.GetType().GetProperty("Services")?.GetValue(context)
            ?? throw new InvalidOperationException("Hotfix feature services are not available."));
    }
}

internal sealed class StateStoreFeatureMessageHandler : IFeatureMessageHandler
{
    private readonly IActorLifecycle _lifecycle;
    private readonly IActorDirectory _directory;
    private readonly IActorDirectoryCache _directoryCache;
    private readonly LocalActorNodeIdentity _localNode;
    private readonly ILogger<StateStoreFeatureMessageHandler> _logger;

    public StateStoreFeatureMessageHandler(
        IActorLifecycle lifecycle,
        IActorDirectory directory,
        IActorDirectoryCache directoryCache,
        LocalActorNodeIdentity localNode,
        ILogger<StateStoreFeatureMessageHandler> logger)
    {
        _lifecycle = lifecycle;
        _directory = directory;
        _directoryCache = directoryCache;
        _localNode = localNode;
        _logger = logger;
    }

    public async ValueTask<FeatureMessageReply> HandleAsync(
        FeatureMessageRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(
                request.Feature.Value,
                StateStoreUserActorPlacement.FeatureName,
                StringComparison.OrdinalIgnoreCase))
        {
            return new FeatureMessageReply(ClusterSendStatus.FeatureNotFound, ReadOnlyMemory<byte>.Empty);
        }

        if (string.Equals(
                request.Kind,
                StateStoreUserActorPlacement.EnsureUserActorKind,
                StringComparison.Ordinal))
        {
            return await HandleEnsureUserActorAsync(request, cancellationToken).ConfigureAwait(false);
        }

        if (string.Equals(
                request.Kind,
                StateStoreUserActorPlacement.EnsureLeaderboardActorKind,
                StringComparison.Ordinal))
        {
            return await HandleEnsureLeaderboardActorAsync(request, cancellationToken).ConfigureAwait(false);
        }

        return new FeatureMessageReply(ClusterSendStatus.FeatureNotFound, ReadOnlyMemory<byte>.Empty);
    }

    private async ValueTask<FeatureMessageReply> HandleEnsureUserActorAsync(
        FeatureMessageRequest request,
        CancellationToken cancellationToken)
    {
        EnsureUserActorRequest? payload;
        try
        {
            payload = JsonSerializer.Deserialize<EnsureUserActorRequest>(
                request.Payload.Span,
                StateStoreUserActorPlacement.JsonOptions);
        }
        catch (JsonException ex)
        {
            return new FeatureMessageReply(
                ClusterSendStatus.DeserializationFailed,
                ReadOnlyMemory<byte>.Empty,
                ex.Message);
        }

        if (payload is null || string.IsNullOrWhiteSpace(payload.UserId))
        {
            return new FeatureMessageReply(
                ClusterSendStatus.Rejected,
                ReadOnlyMemory<byte>.Empty,
                "UserId is required.");
        }

        var actorId = ActorId.From(payload.UserId);
        var registerStatus = await _directory
            .RegisterAsync(actorId, _localNode.NodeId, cancellationToken)
            .ConfigureAwait(false);
        var registeredHere = registerStatus == ActorDirectoryRegisterStatus.Registered;
        if (registerStatus == ActorDirectoryRegisterStatus.Conflict)
        {
            _directoryCache.Remove(actorId);
            return new FeatureMessageReply(
                ClusterSendStatus.StaleRoute,
                ReadOnlyMemory<byte>.Empty,
                $"User actor {payload.UserId} is owned by another node.");
        }

        try
        {
            var createResult = await _lifecycle
                .CreateLocalAsync<UserActor>(actorId, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (!createResult.Succeeded)
            {
                if (registeredHere)
                {
                    await _directory.UnregisterAsync(actorId, _localNode.NodeId, cancellationToken).ConfigureAwait(false);
                }

                _directoryCache.Remove(actorId);
                return new FeatureMessageReply(
                    ClusterSendStatus.Failed,
                    ReadOnlyMemory<byte>.Empty,
                    createResult.Diagnostic ??
                    $"Could not create user actor '{payload.UserId}'. Status={createResult.Status}.");
            }

            _directoryCache.Set(actorId, _localNode.NodeId);
            _logger.LogDebug(
                "Created state-store user actor {UserId} on node {NodeId}.",
                payload.UserId,
                _localNode.NodeId.Value);
            return new FeatureMessageReply(ClusterSendStatus.Accepted, ReadOnlyMemory<byte>.Empty);
        }
        catch (Exception ex)
        {
            if (registeredHere)
            {
                await _directory.UnregisterAsync(actorId, _localNode.NodeId, cancellationToken).ConfigureAwait(false);
            }

            _directoryCache.Remove(actorId);
            return new FeatureMessageReply(ClusterSendStatus.Failed, ReadOnlyMemory<byte>.Empty, ex.Message);
        }
    }

    private async ValueTask<FeatureMessageReply> HandleEnsureLeaderboardActorAsync(
        FeatureMessageRequest request,
        CancellationToken cancellationToken)
    {
        EnsureLeaderboardActorRequest? payload;
        try
        {
            payload = JsonSerializer.Deserialize<EnsureLeaderboardActorRequest>(
                request.Payload.Span,
                StateStoreUserActorPlacement.JsonOptions);
        }
        catch (JsonException ex)
        {
            return new FeatureMessageReply(
                ClusterSendStatus.DeserializationFailed,
                ReadOnlyMemory<byte>.Empty,
                ex.Message);
        }

        if (payload is null || string.IsNullOrWhiteSpace(payload.LeaderboardId))
        {
            return new FeatureMessageReply(
                ClusterSendStatus.Rejected,
                ReadOnlyMemory<byte>.Empty,
                "LeaderboardId is required.");
        }

        var actorId = ActorId.From(payload.LeaderboardId);
        var registerStatus = await _directory
            .RegisterAsync(actorId, _localNode.NodeId, cancellationToken)
            .ConfigureAwait(false);
        var registeredHere = registerStatus == ActorDirectoryRegisterStatus.Registered;
        if (registerStatus == ActorDirectoryRegisterStatus.Conflict)
        {
            _directoryCache.Remove(actorId);
            return new FeatureMessageReply(
                ClusterSendStatus.StaleRoute,
                ReadOnlyMemory<byte>.Empty,
                $"Leaderboard actor {payload.LeaderboardId} is owned by another node.");
        }

        try
        {
            var createResult = await _lifecycle
                .CreateLocalAsync<LeaderboardActor>(actorId, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (!createResult.Succeeded)
            {
                if (registeredHere)
                {
                    await _directory.UnregisterAsync(actorId, _localNode.NodeId, cancellationToken).ConfigureAwait(false);
                }

                _directoryCache.Remove(actorId);
                return new FeatureMessageReply(
                    ClusterSendStatus.Failed,
                    ReadOnlyMemory<byte>.Empty,
                    createResult.Diagnostic ??
                    $"Could not create leaderboard actor '{payload.LeaderboardId}'. Status={createResult.Status}.");
            }

            _directoryCache.Set(actorId, _localNode.NodeId);
            _logger.LogDebug(
                "Created state-store leaderboard actor {LeaderboardId} on node {NodeId}.",
                payload.LeaderboardId,
                _localNode.NodeId.Value);
            return new FeatureMessageReply(ClusterSendStatus.Accepted, ReadOnlyMemory<byte>.Empty);
        }
        catch (Exception ex)
        {
            if (registeredHere)
            {
                await _directory.UnregisterAsync(actorId, _localNode.NodeId, cancellationToken).ConfigureAwait(false);
            }

            _directoryCache.Remove(actorId);
            return new FeatureMessageReply(ClusterSendStatus.Failed, ReadOnlyMemory<byte>.Empty, ex.Message);
        }
    }
}
