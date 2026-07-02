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
    private readonly LocalActorNodeIdentity _localNode;
    private readonly ILogger<StateStoreFeature> _logger;

    public StateStoreFeature(
        ActorHosting actorHosting,
        LocalActorNodeIdentity localNode,
        ILogger<StateStoreFeature> logger)
    {
        _actorHosting = actorHosting;
        _localNode = localNode;
        _logger = logger;
    }

    public static void Configure(HotfixFeatureContext context)
    {
        context.Services.AddLogging();
        context.Services.AddSingleton<MatchmakingNotifier>();
        context.Services.AddSingleton<RoomNotifier>();
        context.Services.AddSingleton<StateStoreUserActorPlacementClient>();
        context.HandleCommand<CreateUserActorRequest, CreateActorReply>(nameof(CreateUserActorAsync));
    }

    public async ValueTask<CreateActorReply> CreateUserActorAsync(
        HotfixFeatureCommandCall<CreateUserActorRequest> call)
    {
        if (string.IsNullOrWhiteSpace(call.Request.UserId))
        {
            return new CreateActorReply { Succeeded = false, Message = "UserId is required." };
        }

        return await CreateActorAsync<UserActor>(
            ActorId.From(call.Request.UserId),
            $"user actor {call.Request.UserId}",
            call.CancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<CreateActorReply> CreateActorAsync<TActor>(
        ActorId actorId,
        string description,
        CancellationToken cancellationToken)
        where TActor : class, IActor
    {
        try
        {
            await _actorHosting
                .EnsureAsync<TActor>(actorId, cancellationToken)
                .ConfigureAwait(false);

            _logger.LogDebug("Created state-store {Description} on node {NodeId}.", description, _localNode.NodeId.Value);
            return new CreateActorReply { Succeeded = true, Message = "Actor ready." };
        }
        catch (ActorHostedElsewhereException)
        {
            return new CreateActorReply
            {
                Succeeded = false,
                Message = $"{description} is owned by another node."
            };
        }
    }
}
