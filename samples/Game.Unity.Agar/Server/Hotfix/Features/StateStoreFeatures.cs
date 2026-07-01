using Agar.Sample.State.Leaderboard;
using Agar.Sample.State.Users;
using Lakona.Game.Cluster;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Server.Hotfix.Services;

namespace Server.Hotfix.Features;

[HotfixFeature(StateStoreUserActorPlacement.FeatureName)]
public sealed class StateStoreFeature : HotfixGameFeature
{
    private readonly ActorHosting _actorHosting;
    private readonly IActorDirectory _directory;
    private readonly IActorDirectoryCache _directoryCache;
    private readonly LocalActorNodeIdentity _localNode;
    private readonly ILogger<StateStoreFeature> _logger;

    public StateStoreFeature(
        ActorHosting actorHosting,
        IActorDirectory directory,
        IActorDirectoryCache directoryCache,
        LocalActorNodeIdentity localNode,
        ILogger<StateStoreFeature> logger)
    {
        _actorHosting = actorHosting;
        _directory = directory;
        _directoryCache = directoryCache;
        _localNode = localNode;
        _logger = logger;
    }

    public static void Configure(HotfixFeatureContext context)
    {
        context.Services.AddLogging();
        context.Services.AddSingleton<MatchmakingNotifier>();
        context.Services.AddSingleton<RoomNotifier>();
        context.HandleCommand<EnsureUserActorRequest, EnsureActorReply>(nameof(EnsureUserActorAsync));
        context.HandleCommand<EnsureLeaderboardActorRequest, EnsureActorReply>(nameof(EnsureLeaderboardActorAsync));
    }

    public async ValueTask<EnsureActorReply> EnsureUserActorAsync(
        HotfixFeatureCommandCall<EnsureUserActorRequest> call)
    {
        if (string.IsNullOrWhiteSpace(call.Request.UserId))
        {
            return new EnsureActorReply { Succeeded = false, Message = "UserId is required." };
        }

        return await EnsureActorAsync<UserActor>(
            ActorId.From(call.Request.UserId),
            $"user actor {call.Request.UserId}",
            call.CancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<EnsureActorReply> EnsureLeaderboardActorAsync(
        HotfixFeatureCommandCall<EnsureLeaderboardActorRequest> call)
    {
        if (string.IsNullOrWhiteSpace(call.Request.LeaderboardId))
        {
            return new EnsureActorReply { Succeeded = false, Message = "LeaderboardId is required." };
        }

        return await EnsureActorAsync<LeaderboardActor>(
            ActorId.From(call.Request.LeaderboardId),
            $"leaderboard actor {call.Request.LeaderboardId}",
            call.CancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<EnsureActorReply> EnsureActorAsync<TActor>(
        ActorId actorId,
        string description,
        CancellationToken cancellationToken)
        where TActor : class, IActor
    {
        var registerStatus = await _directory
            .RegisterAsync(actorId, _localNode.NodeId, cancellationToken)
            .ConfigureAwait(false);
        var registeredHere = registerStatus == ActorDirectoryRegisterStatus.Registered;
        if (registerStatus == ActorDirectoryRegisterStatus.Conflict)
        {
            _directoryCache.Remove(actorId);
            return new EnsureActorReply
            {
                Succeeded = false,
                Message = $"{description} is owned by another node."
            };
        }

        try
        {
            await _actorHosting
                .EnsureAsync<TActor>(actorId, cancellationToken)
                .ConfigureAwait(false);

            _directoryCache.Set(actorId, _localNode.NodeId);
            _logger.LogDebug("Created state-store {Description} on node {NodeId}.", description, _localNode.NodeId.Value);
            return new EnsureActorReply { Succeeded = true, Message = "Actor ready." };
        }
        catch
        {
            if (registeredHere)
            {
                await _directory.UnregisterAsync(actorId, _localNode.NodeId, cancellationToken).ConfigureAwait(false);
            }

            _directoryCache.Remove(actorId);
            throw;
        }
    }
}
