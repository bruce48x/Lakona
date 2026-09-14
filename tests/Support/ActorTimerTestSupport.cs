using System.Reflection;
using System.Runtime.ExceptionServices;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Timers;
using Microsoft.Extensions.DependencyInjection;

namespace Lakona.Game.Server.TestingSupport;

// Test adapter for descriptor, reload, and scheduler tests. All creation goes through
// the Actor backend; synthetic owners isolate scheduling from mailbox lifecycle tests.
public readonly record struct TestTimerEntry<TArgs>(string CallbackFullName, string MethodName, ulong MethodId);

public static class TestTimer
{
    private static readonly ServiceProvider Services = new ServiceCollection().AddLakonaGameServerActors().BuildServiceProvider();

    internal static ActorTimerOwner CreateOwner() => CreateActor().Context.TimerOwner!;

    internal static Actor CreateActor()
    {
        var actor = new TimerActor();
        var turn = new SemaphoreSlim(1);
        var owner = new ActorTimerOwner(async work =>
        {
            await turn.WaitAsync(work.CancellationToken);
            try
            {
                work.CancellationToken.ThrowIfCancellationRequested();
                await work.Callback(actor, work.State, work.CancellationToken);
            }
            finally { turn.Release(); }
        }, static () => true);
        actor.ActivateAsync(new ActorContext(ActorId.From(Guid.NewGuid().ToString()), Services,
            Services.GetRequiredService<IActorRuntime>(), null, owner), CancellationToken.None).GetAwaiter().GetResult();
        return actor;
    }

    internal static LakonaTimerDescriptor WithOwner(LakonaTimerDescriptor descriptor, ActorTimerOwner? owner = null) => new(
        descriptor.TimerId, descriptor.CallbackAssemblyName, descriptor.CallbackFullName,
        descriptor.MethodName, descriptor.MethodId, descriptor.ArgsAssemblyName, descriptor.ArgsFullName,
        descriptor.SerializerId, descriptor.JsonPayload, descriptor.NextDueAtUtc, descriptor.Period, descriptor.Generation)
        { Owner = descriptor.Owner ?? owner ?? CreateOwner() };

    public static ValueTask<TimerId> CreateOnceTimerAsync<TArgs>(TestTimerEntry<TArgs> entry,
        TimeSpan dueTime, TArgs args, CancellationToken cancellationToken = default) =>
        Create(entry, dueTime, null, args, cancellationToken);

    public static ValueTask<TimerId> CreateOnceTimerAsync<TBehavior, TArgs>(
        Func<TBehavior, ActorTimerCallback<Actor, TArgs>> selector,
        TimeSpan dueTime, TArgs args, CancellationToken cancellationToken = default) where TBehavior : class =>
        new ValueTask<TimerId>(LakonaTimerExecutionScope.GetActiveContext().Backend.CreateTimer(CreateActor(), selector, dueTime, null, args, cancellationToken));

    public static ValueTask<TimerId> CreatePeriodicTimerAsync<TArgs>(TestTimerEntry<TArgs> entry,
        TimeSpan dueTime, TimeSpan period, TArgs args, CancellationToken cancellationToken = default) =>
        Create(entry, dueTime, period, args, cancellationToken);

    private static ValueTask<TimerId> Create<TArgs>(TestTimerEntry<TArgs> entry,
        TimeSpan dueTime, TimeSpan? period, TArgs args, CancellationToken cancellationToken)
    {
        var context = LakonaTimerExecutionScope.GetActiveContext();
        if (context.Backend is not LakonaTimerBackend && context.Backend.GetType().DeclaringType != typeof(LakonaTimerBackend))
            return new ValueTask<TimerId>(context.Backend.CreateTimer(CreateActor(),
                static (DummyBehavior<TArgs> behavior) => behavior.TickAsync, dueTime, period, args, cancellationToken));
        var table = context.RuntimeContext!.Snapshot.DispatchTable!;
        new LakonaTimerCallbackResolver().Validate(context.RuntimeContext,
            new HotfixTimerEntry<TArgs>(entry.CallbackFullName, entry.MethodName, entry.MethodId));
        table.TryResolveTimerMethod(entry.MethodId, out var descriptor);
        try
        {
            return (ValueTask<TimerId>)typeof(TestTimer).GetMethod(nameof(CreateSelected), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(descriptor.CallbackType, typeof(TArgs))
                .Invoke(null, [descriptor.Method, dueTime, period, args, cancellationToken])!;
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private static ValueTask<TimerId> CreateSelected<TBehavior, TArgs>(MethodInfo method,
        TimeSpan dueTime, TimeSpan? period, TArgs args, CancellationToken cancellationToken) where TBehavior : class =>
        new ValueTask<TimerId>(LakonaTimerExecutionScope.GetActiveContext().Backend.CreateTimer(CreateActor(),
            (TBehavior behavior) => (ActorTimerCallback<Actor, TArgs>)method.CreateDelegate(typeof(ActorTimerCallback<Actor, TArgs>), behavior),
            dueTime, period, args, cancellationToken));

    private sealed class TimerActor : Actor<string>;
    private sealed class DummyBehavior<TArgs>
    {
        public ValueTask TickAsync(Actor actor, TimerTick<TArgs> tick) => default;
    }
}
