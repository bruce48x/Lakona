using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Cluster;
using Lakona.Game.Server.Features;
using MemoryPack;
using Microsoft.Extensions.DependencyInjection;

namespace Server.Hotfix.Services;

internal static class StateStoreUserActorPlacement
{
    public const string FeatureName = "state-store";

    public const int CreateUserActorCommandId = 201;
}

[MemoryPackable(GenerateType.VersionTolerant)]
[FeatureCommand(StateStoreUserActorPlacement.CreateUserActorCommandId)]
public partial class CreateUserActorRequest
{
    [MemoryPackOrder(0)]
    public string UserId { get; set; } = "";
}

public sealed class StateStoreUserActorPlacementClient
{
    private readonly IServiceProvider _services;

    public StateStoreUserActorPlacementClient(IServiceProvider services)
    {
        _services = services;
    }

    public async ValueTask SendCreateUserActorAsync(ClusterNodeDescriptor owner, string userId)
    {
        var client = _services.GetRequiredService<IFeatureCommandClient>();
        var reply = await client.SendToNodeAsync<CreateUserActorRequest, CreateActorReply>(
            owner,
            StateStoreUserActorPlacement.FeatureName,
            new CreateUserActorRequest { UserId = userId }).ConfigureAwait(false);
        if (!reply.Succeeded)
        {
            throw new InvalidOperationException(
                $"State-store node {owner.Node.Value} rejected user actor creation for '{userId}'. {reply.Message}");
        }
    }
}

[MemoryPackable(GenerateType.VersionTolerant)]
public partial class CreateActorReply
{
    [MemoryPackOrder(0)]
    public bool Succeeded { get; set; }

    [MemoryPackOrder(1)]
    public string Message { get; set; } = "";
}
