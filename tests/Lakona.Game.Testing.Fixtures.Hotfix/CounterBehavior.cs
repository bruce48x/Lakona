using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Testing.Fixtures.App;

namespace Lakona.Game.Testing.Fixtures.Hotfix;

[HotfixBehaviorOf(typeof(CounterActor))]
public sealed partial class CounterBehavior
{
    private readonly CounterControl control;

    public CounterBehavior(CounterControl control)
    {
        this.control = control;
    }

    public ValueTask<CounterReply> AddAsync(
        CounterActor self,
        AddCounterRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        self.Value += request.Delta;
        return new ValueTask<CounterReply>(new CounterReply { Value = self.Value });
    }

    public async ValueTask<CounterReply> WaitAndAddAsync(
        CounterActor self,
        WaitCounterRequest request,
        CancellationToken cancellationToken = default)
    {
        await control.WaitAsync(cancellationToken);
        self.Value++;
        return new CounterReply { Value = self.Value };
    }
}

[HotfixStartup]
public static class CounterHotfixStartup
{
    [HotfixConfigureActors]
    public static void ConfigureActors(ActorHostBuilder actors)
    {
        actors.RegisterPlacement<CounterActor, CounterId>(static context =>
            context.Candidates.OrderBy(static candidate => candidate.NodeId, StringComparer.Ordinal).First());
    }
}

public sealed class CounterControl
{
    private readonly TaskCompletionSource entered = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource released = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Entered => entered.Task;

    public void Release() => released.TrySetResult();

    internal async Task WaitAsync(CancellationToken cancellationToken)
    {
        entered.TrySetResult();
        await released.Task.WaitAsync(cancellationToken);
    }
}

public static class CounterCalls
{
    public static ValueTask<CounterReply> AddAsync(
        ActorAccess actors,
        CounterId id,
        int delta,
        CancellationToken cancellationToken = default) =>
        actors.Route<CounterActor>(id).CallAsync(
            static behavior => behavior.AddAsync,
            new AddCounterRequest { Delta = delta },
            cancellationToken);

    public static ValueTask<CounterReply> WaitAndAddAsync(
        ActorAccess actors,
        CounterId id,
        CancellationToken cancellationToken = default) =>
        actors.Route<CounterActor>(id).CallAsync(
            static behavior => behavior.WaitAndAddAsync,
            new WaitCounterRequest(),
            cancellationToken);
}
