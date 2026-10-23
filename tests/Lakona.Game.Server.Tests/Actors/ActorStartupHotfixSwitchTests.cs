using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.Dispatch;
using MemoryPack;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lakona.Game.Server.Tests.Actors;

public sealed class ActorStartupHotfixSwitchTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Completed_start_survives_hook_removal(bool withArguments)
    {
        var accessor = new SwitchingAccessor();
        var control = new StartupControl();
        await using var services = CreateServices(accessor, control);
        await using var table = CreateTable(services);
        await using var empty = new HotfixDispatchTable(2, [], []);
        accessor.CurrentSnapshot = Snapshot(table, services);
        var placement = services.GetRequiredService<IActorPlacementService>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var id = ActorId.From("switch/start");
        var pending = withArguments
            ? placement.PlaceAsync<SwitchActor, string>(id, "start", ActorPlacementCreateMode.Ensure,
                ActorStartArguments.From(new SwitchStartArgs { Value = 42 }), timeout.Token).AsTask()
            : placement.PlaceAsync<SwitchActor, string>(id, "start", ActorPlacementCreateMode.Ensure, timeout.Token).AsTask();
        try
        {
            await control.Entered.Task.WaitAsync(timeout.Token);
            Assert.False(pending.IsCompleted);
            accessor.CurrentSnapshot = Snapshot(empty, services);
        }
        finally { control.Release.TrySetResult(); }
        await pending.WaitAsync(timeout.Token);
        Assert.Equal(withArguments ? 42 : 7, control.Value);
        Assert.Single(services.GetRequiredService<IActorRuntime>().GetActiveActorIds(typeof(SwitchActor)));
    }

    [Fact]
    public async Task Missing_hook_at_dispatch_fails_even_if_next_snapshot_adds_hook()
    {
        var accessor = new SwitchingAccessor();
        var control = new StartupControl();
        await using var services = CreateServices(accessor, control);
        await using var table = CreateTable(services);
        await using var empty = new HotfixDispatchTable(2, [], []);
        accessor.CurrentSnapshot = Snapshot(empty, services);
        accessor.NextSnapshot = Snapshot(table, services);
        await Assert.ThrowsAsync<InvalidOperationException>(() => services.GetRequiredService<IActorPlacementService>()
            .PlaceAsync<SwitchActor, string>(ActorId.From("switch/missing"), "missing", ActorPlacementCreateMode.Ensure,
                ActorStartArguments.From(new SwitchStartArgs { Value = 42 }), TestContext.Current.CancellationToken).AsTask());
        Assert.False(control.Entered.Task.IsCompleted);
        Assert.Empty(services.GetRequiredService<IActorRuntime>().GetActiveActorIds(typeof(SwitchActor)));
    }

    private static ServiceProvider CreateServices(SwitchingAccessor accessor, StartupControl control) =>
        new ServiceCollection().AddLakonaGameServerActors()
            .AddSingleton(control).AddSingleton<IHotfixRuntimeAccessor>(accessor)
            .AddSingleton<HotfixActorLifecycleInvoker>()
            .AddSingleton<IActorLifecycleDispatcher, HotfixActorLifecycleDispatcher>().BuildServiceProvider();

    private static HotfixDispatchTable CreateTable(IServiceProvider services)
    {
        var table = new HotfixDispatchTable(1, [], [], [], [new HotfixActorLifecycleDescriptor(
            typeof(SwitchBehavior), typeof(SwitchActor), typeof(SwitchBehavior).GetMethod(nameof(SwitchBehavior.StartAsync)), null)]);
        table.ValidateModuleActivation(services);
        return table;
    }

    private static HotfixRuntimeSnapshot Snapshot(HotfixDispatchTable table, IServiceProvider services) =>
        new(new HotfixServiceInvoker(table), services, table, services, null, null, "test", null, false, null);

    private sealed class SwitchingAccessor : IHotfixRuntimeAccessor
    {
        public HotfixRuntimeSnapshot CurrentSnapshot = null!;
        public HotfixRuntimeSnapshot? NextSnapshot;
        public HotfixRuntimeSnapshot Current
        {
            get
            {
                var result = CurrentSnapshot;
                if (NextSnapshot is { } next) { CurrentSnapshot = next; NextSnapshot = null; }
                return result;
            }
        }
    }

    public sealed class StartupControl
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Value;
    }

    public sealed class SwitchBehavior(StartupControl control)
    {
        public async ValueTask StartAsync(SwitchActor actor, ActorStartCall<SwitchStartArgs> call)
        {
            control.Entered.TrySetResult();
            await control.Release.Task.WaitAsync(call.CancellationToken);
            control.Value = call.TryGetArguments(out var args) ? args.Value : 7;
        }
    }

    [ActorLocalOnly]
    public sealed class SwitchActor : Actor<string>;
}

[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class SwitchStartArgs
{
    [MemoryPackOrder(0)] public int Value { get; set; }
}
