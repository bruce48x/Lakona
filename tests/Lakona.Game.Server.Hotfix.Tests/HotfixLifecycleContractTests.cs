using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.Scanning;
using Lakona.Game.Server.Hotfix.Dispatch;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lakona.Game.Server.Hotfix.Tests;

public sealed class HotfixLifecycleContractTests
{
    [Fact]
    public async Task Direct_calls_share_one_generation_instance_and_wait_for_lease_before_disposal()
    {
        var scan = HotfixBehaviorScanner.Scan(typeof(Hooks).Assembly, [typeof(Hooks)],
            requiredServiceContracts: [typeof(IDerived), typeof(IOther)]);
        Assert.True(scan.Succeeded, string.Join(Environment.NewLine, scan.Diagnostics));
        Assert.Empty(scan.Services);
        Assert.Equal(3, scan.Lifecycles.Count);
        using var provider = new ServiceCollection().BuildServiceProvider();
        var table = new HotfixDispatchTable(1, [], [], [], [], [], lifecycles: scan.Lifecycles);
        table.ValidateModuleActivation(provider);
        var snapshot = new HotfixRuntimeSnapshot(new HotfixServiceInvoker(table), provider, table,
            provider, null, null, null, null, ownsRuntimeResources: true, onRetired: null);
        using var lease = snapshot.AcquireLease();
        var first = lease.GetLifecycle<IDerived>();
        var second = lease.GetLifecycle<IOther>();
        Assert.Same(first, second);
        Assert.Same(first, lease.GetLifecycle<IBase>());
        Assert.Equal("base", await first.Run(default));
        Assert.Equal("other", await second.Run(default));
        var instance = Assert.IsType<Hooks>(first);
        snapshot.Retire();
        Assert.False(instance.Disposed);
        lease.Dispose();
        await snapshot.RetirementCompletion;
        Assert.True(instance.Disposed);
        Assert.Throws<ObjectDisposedException>(() => lease.GetLifecycle<IDerived>());
    }

    [Fact]
    public void Marker_without_an_implemented_contract_is_rejected()
    {
        var scan = HotfixBehaviorScanner.Scan(typeof(NoContract).Assembly, [typeof(NoContract)]);
        Assert.False(scan.Succeeded);
        Assert.Contains(scan.Diagnostics, diagnostic => diagnostic.Contains("must implement a lifecycle interface"));
    }

    [Fact]
    public void Duplicate_implementations_are_rejected_even_when_contract_is_not_required()
    {
        var scan = HotfixBehaviorScanner.Scan(typeof(Hooks).Assembly, [typeof(Hooks), typeof(OtherHooks)]);
        Assert.False(scan.Succeeded);
        Assert.Contains(scan.Diagnostics, diagnostic => diagnostic.Contains("Duplicate hotfix lifecycle implementation"));
    }

    public interface IBase
    {
        ValueTask<string> Run(HotfixLifecycleCall<object> call);
    }
    public interface IDerived : IBase;
    public interface IOther
    {
        ValueTask<string> Run(HotfixLifecycleCall<object> call);
    }
    [HotfixLifecycle] public sealed class Hooks : IDerived, IOther, IDisposable
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
        ValueTask<string> IBase.Run(HotfixLifecycleCall<object> call) => new("base");
        ValueTask<string> IOther.Run(HotfixLifecycleCall<object> call) => new("other");
    }
    [HotfixLifecycle] public sealed class OtherHooks : IOther
    {
        public ValueTask<string> Run(HotfixLifecycleCall<object> call) => new("other");
    }
    [HotfixLifecycle] public sealed class NoContract;
}
