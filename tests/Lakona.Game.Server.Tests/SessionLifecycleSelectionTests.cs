using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.Dispatch;
using Lakona.Game.Server.Hotfix.Loading;
using Lakona.Game.Server.Hotfix.Scanning;
using Lakona.Game.Server.Sessions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lakona.Game.Server.Tests;

public sealed class SessionLifecycleSelectionTests
{
    [Fact]
    public async Task Sessions_select_independent_handlers_and_existing_sessions_use_new_generation()
    {
        var token = TestContext.Current.CancellationToken;
        HotfixManager manager = null!;
        var services = new ServiceCollection();
        services.AddLakonaGameServer();
        services.UseReadySingleNodeMembership();
        services.AddSingleton<IHotfixRuntimeAccessor>(_ => manager);
        await using var root = services.BuildServiceProvider();
        await using var ownedManager = manager = new HotfixManager(
            new CurrentDirectoryHotfixAssemblySource(".", "unused.dll"), rootServices: root, participants: []);
        var first = new Probe("v1");
        Assert.True((await Publish(manager, first, token)).Succeeded);
        var server = root.GetRequiredService<ILakonaGameServer>();
        var control = await server.StartSessionAsync<Control>("same-owner", token);
        var realtime = await server.StartSessionAsync<Realtime>("same-owner", token);
        var sibling = await server.StartSessionAsync<Control>("same-owner", token);
        var plain = await server.StartSessionAsync("same-owner", token);
        var handler = root.GetServices<IGameSessionLifecycleHandler>().OfType<GameSessionHotfixLifecycleHandler>().Single();
        await handler.OnSessionDisconnectedAsync(new(control, "c1"), token);
        await handler.OnSessionDisconnectedAsync(new(realtime, "r1"), token);
        await handler.OnSessionDisconnectedAsync(new(plain, "plain"), token);
        Assert.Equal(["v1:control:disconnected", "v1:realtime:disconnected"], first.Events);

        var second = new Probe("v2");
        Assert.True((await Publish(manager, second, token)).Succeeded);
        Assert.True(first.Disposed);
        await handler.OnSessionResumedAsync(new(control, "c2"), token);
        await handler.OnSessionExpiredAsync(new(realtime, "r2"), token);
        Assert.Equal(["v2:control:resumed", "v2:realtime:expired"], second.Events);
        Assert.Equal(2, first.Events.Count);

        var rejected = await Publish(manager, new Probe("missing"), token, includeControl: false);
        Assert.False(rejected.Succeeded);
        Assert.Equal("v2", manager.Current.Version);
        Assert.Contains(rejected.Diagnostics, text => text.Contains("Cannot remove or rename session lifecycle"));
        await root.GetRequiredService<IGameSessionRegistry>().RemoveSessionAsync(control, token);
        Assert.False((await Publish(manager, new Probe("still-used"), token, includeControl: false)).Succeeded);
        await server.TerminateSessionAsync(sibling, Lakona.Game.Abstractions.SessionTerminationReason.Policy, cancellationToken: token);
        Assert.True((await Publish(manager, new Probe("v3"), token, includeControl: false)).Succeeded);
        await Assert.ThrowsAsync<InvalidOperationException>(() => server.StartSessionAsync<Control>("new-owner", token).AsTask());
    }

    [Fact]
    public async Task Expiration_keeps_binding_until_callback_finishes_then_allows_removal()
    {
        var token = TestContext.Current.CancellationToken;
        HotfixManager manager = null!;
        var clock = new Clock();
        var services = new ServiceCollection();
        services.AddLakonaGameServer();
        services.UseReadySingleNodeMembership();
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton<IHotfixRuntimeAccessor>(_ => manager);
        await using var root = services.BuildServiceProvider();
        await using var ownedManager = manager = new HotfixManager(
            new CurrentDirectoryHotfixAssemblySource(".", "unused.dll"), rootServices: root, participants: []);
        var probe = new Probe("v1") { BlockExpiration = true };
        Assert.True((await Publish(manager, probe, token)).Succeeded);
        var session = await root.GetRequiredService<ILakonaGameServer>().StartSessionAsync<Control>("owner", token);
        var registry = root.GetRequiredService<IGameSessionRegistry>();
        await registry.BindSessionAsync(session, "old", token);
        await registry.MarkConnectionDisconnectedAsync("old", token);
        clock.Now += TimeSpan.FromHours(1);
        var cleanup = ActivatorUtilities.CreateInstance<GameSessionCleanupHostedService>(root);
        var pending = cleanup.CleanupOnceAsync(token).AsTask();
        await probe.Entered.Task.WaitAsync(token);
        Assert.False((await Publish(manager, new Probe("missing"), token, includeControl: false)).Succeeded);
        probe.Release.SetResult();
        await pending;
        Assert.True((await Publish(manager, new Probe("v2"), token, includeControl: false)).Succeeded);
        Assert.Equal(["v1:control:expired"], probe.Events);
    }

    private static ValueTask<HotfixReloadResult> Publish(HotfixManager manager, Probe probe, CancellationToken token, bool includeControl = true)
    {
        Type[] types = includeControl ? [typeof(Control), typeof(Realtime)] : [typeof(Realtime)];
        var scan = HotfixBehaviorScanner.Scan(typeof(Control).Assembly, types);
        Assert.True(scan.Succeeded, string.Join("; ", scan.Diagnostics));
        var provider = new ServiceCollection().AddSingleton(probe).BuildServiceProvider();
        var table = new HotfixDispatchTable(1, [], [], [], [], [], lifecycles: scan.Lifecycles);
        table.ValidateModuleActivation(provider);
        var runtime = new HotfixRuntimeSnapshot(new HotfixServiceInvoker(table), provider, table,
            provider, null, null, probe.Version, null, ownsRuntimeResources: true, onRetired: null);
        return manager.PublishCandidateAsync(runtime,
            new HotfixSnapshot(probe.Version, null, DateTimeOffset.UtcNow, 1, [], HotfixReloadStatus.Succeeded, null, null), token);
    }

    public sealed class Probe(string version)
    {
        public string Version { get; } = version;
        public List<string> Events { get; } = [];
        public bool Disposed;
        public bool BlockExpiration;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    [HotfixLifecycle]
    public sealed class Control(Probe probe) : IGameSessionLifecycle, IDisposable
    {
        public ValueTask SessionDisconnectedAsync(HotfixLifecycleCall<GameSessionDisconnectedRequest> call) { probe.Events.Add(probe.Version + ":control:disconnected"); return default; }
        public ValueTask SessionResumedAsync(HotfixLifecycleCall<GameSessionResumedRequest> call) { probe.Events.Add(probe.Version + ":control:resumed"); return default; }
        public async ValueTask SessionExpiredAsync(HotfixLifecycleCall<GameSessionExpiredRequest> call)
        {
            probe.Events.Add(probe.Version + ":control:expired");
            if (probe.BlockExpiration) { probe.Entered.SetResult(); await probe.Release.Task; }
        }
        public void Dispose() => probe.Disposed = true;
    }

    [HotfixLifecycle]
    public sealed class Realtime(Probe probe) : IGameSessionLifecycle
    {
        public ValueTask SessionDisconnectedAsync(HotfixLifecycleCall<GameSessionDisconnectedRequest> call) { probe.Events.Add(probe.Version + ":realtime:disconnected"); return default; }
        public ValueTask SessionResumedAsync(HotfixLifecycleCall<GameSessionResumedRequest> call) { probe.Events.Add(probe.Version + ":realtime:resumed"); return default; }
        public ValueTask SessionExpiredAsync(HotfixLifecycleCall<GameSessionExpiredRequest> call) { probe.Events.Add(probe.Version + ":realtime:expired"); return default; }
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
