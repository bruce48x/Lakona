using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Testing.Fixtures.App;

namespace Lakona.Game.Testing.Fixtures.Hotfix;

[HotfixBehaviorOf(typeof(CounterActor))]
public sealed partial class CounterBehavior
{
    public ValueTask<CounterReply> AddAsync(
        CounterActor self,
        AddCounterRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        self.Value += request.Delta;
        return new ValueTask<CounterReply>(new CounterReply { Value = self.Value });
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
}
