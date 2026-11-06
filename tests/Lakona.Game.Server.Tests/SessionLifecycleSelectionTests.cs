using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.Dispatch;
using Lakona.Game.Server.Hotfix.Loading;
using Lakona.Game.Server.Hotfix.Scanning;
using Lakona.Game.Server.Sessions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
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
    public async Task Failed_first_ack_preserves_unbound_session_data_and_lifecycle_for_another_endpoint()
    {
        var ct = TestContext.Current.CancellationToken;
        var notifier = new FirstEstablishmentGate();
        HotfixManager manager = null!;
        var services = new ServiceCollection();
        services.AddLakonaGameServer();
        services.UseReadySingleNodeMembership();
        services.AddSingleton<IGameSessionEstablishedNotifier>(notifier);
        services.AddSingleton<IHotfixRuntimeAccessor>(_ => manager);
        await using var root = services.BuildServiceProvider();
        await using var ownedManager = manager = new HotfixManager(
            new CurrentDirectoryHotfixAssemblySource(".", "unused.dll"), rootServices: root, participants: []);
        var probe = new Probe("v1");
        Assert.True((await Publish(manager, probe, ct)).Succeeded);
        var server = root.GetRequiredService<ILakonaGameServer>();
        var registry = root.GetRequiredService<IGameSessionRegistry>();
        var tickets = root.GetRequiredService<IGameSessionResumeTicketStore>();
        var policies = root.GetRequiredService<GameConnectionDeliveryPolicyRegistry>();
        policies.Set("first", false, "plain-endpoint", "json");
        policies.Set("retry", true, "reliable-endpoint", "memorypack");
        var connections = root.GetRequiredService<GameHandshakeConnectionStateRegistry>();
        using var handshakeSlots = new SemaphoreSlim(0, 1);
        await using var firstConnection = connections.RegisterPending("first", TimeSpan.FromMinutes(1), handshakeSlots, NullLogger.Instance);
        Assert.True(firstConnection.MarkComplete());
        var session = await server.StartSessionAsync<Control>("owner", ct);
        var room = GameSessionItemValue.FromString("room-1");
        await server.SetSessionItemAsync(session, "room", room, ct);

        var firstBind = server.BindSessionAsync(session, "first", ct).AsTask();
        var first = await notifier.First.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
        notifier.Acknowledge.SetException(new TimeoutException("Establishment ACK missing."));
        await Assert.ThrowsAsync<TimeoutException>(() => firstBind);
        Assert.True(firstConnection.SessionCancellation.IsCancellationRequested);
        Assert.Null(await tickets.ResolveAsync(first.ResumeTicket, "plain-endpoint", ct));
        Assert.Null(await registry.GetConnectionIdAsync(session, ct));
        Assert.Null(await registry.GetCurrentSessionAsync("first", ct));

        await server.BindSessionAsync(session, "retry", ct);
        Assert.Equal(2, notifier.Notifications.Count);
        var second = notifier.Notifications.Last();
        Assert.Equal(session.SessionId, second.SessionId);
        Assert.NotEqual(first.ResumeTicket, second.ResumeTicket);
        Assert.Equal(session, await tickets.ResolveAsync(second.ResumeTicket, "reliable-endpoint", ct));
        Assert.Equal(session, await registry.GetCurrentSessionAsync("retry", ct));
        Assert.True(await registry.GetReliablePushPolicyAsync(session, ct));
        Assert.Equal("memorypack", await registry.GetNotificationSerializerProfileAsync(session, ct));
        Assert.Equal(room, await server.GetSessionItemAsync(session, "room", ct));
        var handler = root.GetServices<IGameSessionLifecycleHandler>().OfType<GameSessionHotfixLifecycleHandler>().Single();
        await handler.OnSessionDisconnectedAsync(new(session, "retry"), ct);
        Assert.Equal(["v1:control:disconnected"], probe.Events);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_termination_preserves_pending_expiration_binding(bool keepForResume)
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
        var probe = new Probe("v1");
        Assert.True((await Publish(manager, probe, token)).Succeeded);
        var server = root.GetRequiredService<ILakonaGameServer>();
        var session = await server.StartSessionAsync<Control>("owner", token);
        var registry = root.GetRequiredService<IGameSessionRegistry>();
        await registry.BindSessionAsync(session, "old", token);
        await registry.MarkConnectionDisconnectedAsync("old", token);
        clock.Now += TimeSpan.FromHours(1);
        var tickets = new BlockingRevocationStore(root.GetRequiredService<IGameSessionResumeTicketStore>());
        var cleanup = ActivatorUtilities.CreateInstance<GameSessionCleanupHostedService>(root, tickets);
        var pending = cleanup.CleanupOnceAsync(token).AsTask();
        try
        {
            await tickets.Entered.Task.WaitAsync(token);
            Assert.Empty(probe.Events);
            await Assert.ThrowsAsync<InvalidOperationException>(() => server.TerminateSessionAsync(
                session, Lakona.Game.Abstractions.SessionTerminationReason.Policy,
                options: new SessionTerminationOptions { KeepTerminalStateForResume = keepForResume },
                cancellationToken: token).AsTask());
            Assert.False((await Publish(manager, new Probe("missing"), token, includeControl: false)).Succeeded);
            Assert.Equal("v1", manager.Current.Version);
        }
        finally
        {
            tickets.Release.TrySetResult();
            await pending;
        }

        await cleanup.CleanupOnceAsync(token);
        Assert.Equal(["v1:control:expired"], probe.Events);
        Assert.True((await Publish(manager, new Probe("v2"), token, includeControl: false)).Succeeded);
    }

    private sealed class BlockingRevocationStore(IGameSessionResumeTicketStore inner) : IGameSessionResumeTicketStore
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<string> IssueAsync(GameSessionKey session, string endpointScope, CancellationToken cancellationToken = default)
            => inner.IssueAsync(session, endpointScope, cancellationToken);

        public ValueTask<GameSessionKey?> ResolveAsync(string ticket, string endpointScope, CancellationToken cancellationToken = default)
            => inner.ResolveAsync(ticket, endpointScope, cancellationToken);

        public async ValueTask RevokeAsync(GameSessionKey session, CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            await inner.RevokeAsync(session, cancellationToken);
        }
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
