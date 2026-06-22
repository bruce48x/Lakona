using Microsoft.Extensions.DependencyInjection;

namespace Lakona.Game.Server.Hotfix.Abstractions;

public sealed class HotfixFeatureContext
{
    private readonly List<HotfixActorTickDeclaration> _actorTicks = [];

    public IReadOnlyList<HotfixActorTickDeclaration> ActorTicks => _actorTicks;

    public IServiceCollection Services { get; } = new ServiceCollection();

    public void ScheduleActorTick<TActor>(
        string actorId,
        TimeSpan interval,
        TickBacklogPolicy backlogPolicy,
        string methodName = "TickAsync")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        AddTick(typeof(TActor), HotfixActorTickMode.FixedActor, actorId, interval, backlogPolicy, methodName);
    }

    public void ScheduleActiveActorTicks<TActor>(
        TimeSpan interval,
        TickBacklogPolicy backlogPolicy,
        string methodName = "TickAsync")
    {
        AddTick(typeof(TActor), HotfixActorTickMode.ActiveActors, "", interval, backlogPolicy, methodName);
    }

    private void AddTick(
        Type actorType,
        HotfixActorTickMode mode,
        string actorId,
        TimeSpan interval,
        TickBacklogPolicy backlogPolicy,
        string methodName)
    {
        ArgumentNullException.ThrowIfNull(actorType);
        ArgumentException.ThrowIfNullOrWhiteSpace(methodName);
        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval), "Tick interval must be greater than zero.");
        }

        _actorTicks.Add(new HotfixActorTickDeclaration(
            mode,
            actorType,
            actorId,
            methodName,
            interval,
            backlogPolicy));
    }
}
