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

    [Fact]
    public async Task StartAsync_publishes_startup_actor_descriptors_after_creation()
    {
        var refresher = new RecordingRefresher();
        var provider = CreateProvider(["matchmaking"], refresher);
        await using (provider)
        {
            refresher.Catalog = provider.GetRequiredService<StartupActorDescriptorCatalog>();

            await provider.GetRequiredService<StartupActorHostedService>()
                .StartAsync(TestContext.Current.CancellationToken);

            Assert.Equal("matchmaking", Assert.Single(Assert.Single(refresher.Published)).Actor);
        }
    }

    [Fact]
    public async Task PrepareAsync_withdraws_old_build_descriptor_before_runtime_swap()
    {
        var refresher = new RecordingRefresher();
        var provider = CreateProvider(["matchmaking"], refresher);
        await using (provider)
        {
            refresher.Catalog = provider.GetRequiredService<StartupActorDescriptorCatalog>();
            var hosted = provider.GetRequiredService<StartupActorHostedService>();
            await hosted.StartAsync(TestContext.Current.CancellationToken);
            var candidate = Snapshot("build-2");

            await using var transaction = await hosted.PrepareAsync(
                provider.GetRequiredService<IHotfixRuntimeAccessor>().Current,
                candidate,
                TestContext.Current.CancellationToken);

            Assert.Empty(refresher.Published.Last());
            await transaction.ActivateAsync(TestContext.Current.CancellationToken);
            Assert.Equal("build-2", Assert.Single(refresher.Published.Last()).BuildTag);
        }
    }

    [Fact]
    public async Task Rollback_refresh_failure_marks_node_unavailable_and_is_reported()
    {
        var refresher = new RecordingRefresher { FailOnRefresh = 4 };
        var provider = CreateProvider(["matchmaking"], refresher);
        await using (provider)
        {
            refresher.Catalog = provider.GetRequiredService<StartupActorDescriptorCatalog>();
            var hosted = provider.GetRequiredService<StartupActorHostedService>();
            await hosted.StartAsync(TestContext.Current.CancellationToken);
            await using var transaction = await hosted.PrepareAsync(
                provider.GetRequiredService<IHotfixRuntimeAccessor>().Current,
                Snapshot("build-2"),
                TestContext.Current.CancellationToken);
            await transaction.ActivateAsync(TestContext.Current.CancellationToken);

            await Assert.ThrowsAsync<AggregateException>(async () =>
                await transaction.RollbackAsync(TestContext.Current.CancellationToken));

            Assert.True(refresher.WasMarkedUnavailable);
            Assert.Empty(provider.GetRequiredService<StartupActorDescriptorCatalog>().Snapshot());
        }
    }

    private static ServiceProvider CreateProvider(
        IReadOnlyList<string> actorHosts,
        IClusterNodeDescriptorRefresher? refresher = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(new LakonaGameRuntimeOptions
        {
            Node = new LakonaGameNodeOptions { Id = "node-a" },
            ActorHosts = actorHosts
        });
        services.AddLakonaGameServer();
        services.UseReadySingleNodeMembership("node-a");
        services.RemoveAll<IDistributedWorkAdmissionGate>();
        services.RemoveAll<IClusterNodeDescriptorRefresher>();
        services.AddSingleton<IClusterNodeDescriptorRefresher>(refresher ?? new NoopRefresher());
        var snapshot = Snapshot("build-1");
        services.AddSingleton<IHotfixRuntimeAccessor>(new FixedAccessor(snapshot));
        return services.BuildServiceProvider();
    }

    private static HotfixRuntimeSnapshot Snapshot(string sourceVersion) => new(
            new NoopInvoker(),
            new EmptyProvider(),
            [ActorStartupDeclaration.Create<MatchmakingActor, string>(static context => context.Candidates[0])],
            sourceVersion);

    [ActorName("matchmaking")]
    private sealed class MatchmakingActor : IActor { }
    private sealed class NoopRefresher : IClusterNodeDescriptorRefresher
    {
        public ValueTask RefreshAsync(CancellationToken cancellationToken = default) => default;
        public ValueTask MarkUnavailableAsync() => default;
    }
    private sealed class RecordingRefresher : IClusterNodeDescriptorRefresher
    {
        private int _refreshCount;
        public StartupActorDescriptorCatalog? Catalog { get; set; }
        public int? FailOnRefresh { get; init; }
        public bool WasMarkedUnavailable { get; private set; }
        public List<IReadOnlyList<StartupActorDescriptor>> Published { get; } = [];
        public ValueTask RefreshAsync(CancellationToken cancellationToken = default)
        {
            _refreshCount++;
            if (_refreshCount == FailOnRefresh) throw new InvalidOperationException("refresh failed");
            Published.Add(Catalog?.Snapshot() ?? []);
            return default;
        }
        public ValueTask MarkUnavailableAsync()
        {
            WasMarkedUnavailable = true;
            return default;
        }
    }
    private sealed class FixedAccessor(HotfixRuntimeSnapshot snapshot) : IHotfixRuntimeAccessor { public HotfixRuntimeSnapshot Current => snapshot; }
    private sealed class EmptyProvider : IServiceProvider { public object? GetService(Type serviceType) => null; }
    private sealed class NoopInvoker : IHotfixServiceInvoker
    {
        public ValueTask<TResult> InvokeHttpAsync<TArg, TResult>(int endpointSlot, TArg arg, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask InvokeAsync<TContract, TArg>(int methodId, TArg arg, CancellationToken cancellationToken = default) => default;
        public ValueTask<TResult> InvokeAsync<TContract, TArg, TResult>(int methodId, TArg arg, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
