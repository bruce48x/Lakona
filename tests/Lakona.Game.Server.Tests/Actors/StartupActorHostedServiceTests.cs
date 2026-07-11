using Lakona.Game.Cluster;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Hosting;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Lakona.Game.Server.Tests.Actors;

public sealed class StartupActorHostedServiceTests
{
    [Fact]
    public async Task StartAsync_creates_one_capable_replica_before_publishing_descriptor()
    {
        var provider = CreateProvider(["matchmaking"]);
        await using (provider)
        {
            var hosted = provider.GetServices<IHostedService>().Single(service => service.GetType().Name == "StartupActorHostedService");

            await hosted.StartAsync(TestContext.Current.CancellationToken);

            var runtime = provider.GetRequiredService<IActorRuntime>();
            var actorId = Assert.Single(runtime.GetActiveActorIds(typeof(MatchmakingActor)));
            Assert.Equal("matchmaking/@startup/node-a", actorId.Value);
            var descriptor = Assert.Single(provider.GetRequiredService<StartupActorDescriptorCatalog>().Snapshot());
            Assert.Equal("matchmaking", descriptor.Actor);
            Assert.Equal("build-1", descriptor.BuildTag);
        }
    }

    [Fact]
    public async Task StartAsync_skips_replica_when_node_is_not_capable()
    {
        var provider = CreateProvider([]);
        await using (provider)
        {
            var hosted = provider.GetServices<IHostedService>().Single(service => service.GetType().Name == "StartupActorHostedService");

            await hosted.StartAsync(TestContext.Current.CancellationToken);

            Assert.Empty(provider.GetRequiredService<IActorRuntime>().GetActiveActorIds(typeof(MatchmakingActor)));
            Assert.Empty(provider.GetRequiredService<StartupActorDescriptorCatalog>().Snapshot());
        }
    }

    private static ServiceProvider CreateProvider(IReadOnlyList<string> actorHosts)
    {
        var services = new ServiceCollection();
        services.AddSingleton(new LakonaGameRuntimeOptions
        {
            Node = new LakonaGameNodeOptions { Id = "node-a" },
            ActorHosts = actorHosts
        });
        services.AddLakonaGameServer();
        services.RemoveAll<IClusterNodeRegistrationRefresher>();
        services.AddSingleton<IClusterNodeRegistrationRefresher, NoopRefresher>();
        var snapshot = new HotfixRuntimeSnapshot(
            new NoopInvoker(),
            new EmptyProvider(),
            [ActorStartupDeclaration.Create<MatchmakingActor, string>(static context => context.Candidates[0])],
            "build-1");
        services.AddSingleton<IHotfixRuntimeAccessor>(new FixedAccessor(snapshot));
        return services.BuildServiceProvider();
    }

    [ActorName("matchmaking")]
    private sealed class MatchmakingActor : IActor { }
    private sealed class NoopRefresher : IClusterNodeRegistrationRefresher { public ValueTask RefreshAsync(CancellationToken cancellationToken = default) => default; }
    private sealed class FixedAccessor(HotfixRuntimeSnapshot snapshot) : IHotfixRuntimeAccessor { public HotfixRuntimeSnapshot Current => snapshot; }
    private sealed class EmptyProvider : IServiceProvider { public object? GetService(Type serviceType) => null; }
    private sealed class NoopInvoker : IHotfixServiceInvoker
    {
        public ValueTask InvokeAsync<TContract, TArg>(int methodId, TArg arg, CancellationToken cancellationToken = default) => default;
        public ValueTask<TResult> InvokeAsync<TContract, TArg, TResult>(int methodId, TArg arg, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask InvokeAsync<TContract, TArg>(string methodName, TArg arg, CancellationToken cancellationToken = default) => default;
        public ValueTask<TResult> InvokeAsync<TContract, TArg, TResult>(string methodName, TArg arg, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
