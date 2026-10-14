using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Testing.Fixtures.App;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
        AddCounterRequest request)
    {
        self.Value += request.Delta;
        return new ValueTask<CounterReply>(new CounterReply { Value = self.Value });
    }

    public async ValueTask<CounterReply> WaitAndAddAsync(
        CounterActor self,
        WaitCounterRequest request)
    {
        await control.WaitAsync(CancellationToken.None);
        self.Value++;
        return new CounterReply { Value = self.Value };
    }

    public async ValueTask<CounterReply> WaitIgnoringCancellationAndAddAsync(
        CounterActor self,
        WaitCounterRequest request)
    {
        await control.WaitIgnoringCancellationAsync();
        self.Value++;
        control.MarkCompleted();
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

    public static ValueTask<CounterReply> WaitIgnoringCancellationAndAddAsync(
        ActorAccess actors,
        CounterId id,
        CancellationToken cancellationToken = default) =>
        actors.Route<CounterActor>(id).CallAsync(
            static behavior => behavior.WaitIgnoringCancellationAndAddAsync,
            new WaitCounterRequest(),
            cancellationToken);
}
