using Microsoft.Extensions.DependencyInjection;
using Lakona.Game.Cluster;

namespace Lakona.Game.Server.Hotfix.Abstractions;

public sealed class HotfixFeatureContext
{
    private readonly Dictionary<string, string> _metadata = new(StringComparer.Ordinal);
    private readonly List<HotfixLocalActorDeclaration> _localActors = [];
    private readonly List<HotfixActorTickDeclaration> _actorTicks = [];
    private readonly List<HotfixFeatureCommandDeclaration> _commands = [];

    public IReadOnlyList<HotfixLocalActorDeclaration> LocalActors => _localActors;

    public IReadOnlyList<HotfixActorTickDeclaration> ActorTicks => _actorTicks;

    public IReadOnlyList<HotfixFeatureCommandDeclaration> Commands => _commands;

    public IServiceCollection Services { get; } = new ServiceCollection();

    public bool Discoverable { get; set; } = true;

    public IDictionary<string, string> Metadata => _metadata;

    public void EnsureLocalActor<TActor>(string actorId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        _localActors.Add(new HotfixLocalActorDeclaration(typeof(TActor), actorId));
    }

    public void HandleCommand<TRequest, TReply>(string methodName = "HandleAsync")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(methodName);
        var attribute = typeof(TRequest).GetCustomAttributes(typeof(FeatureCommandAttribute), inherit: false)
            .Cast<FeatureCommandAttribute>()
            .SingleOrDefault()
            ?? throw new InvalidOperationException(
                $"Feature command request type '{typeof(TRequest).FullName}' must declare FeatureCommandAttribute.");

        var commandId = FeatureCommandId.From(attribute.Id).Value;

        _commands.Add(new HotfixFeatureCommandDeclaration(
            typeof(TRequest),
            typeof(TReply),
            commandId,
            methodName));
    }

    public void ScheduleActorTick<TActor>(
        string actorId,
        TimeSpan interval,
        TickBacklogPolicy backlogPolicy,
        string methodName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        AddTick(typeof(TActor), HotfixActorTickMode.FixedActor, actorId, interval, backlogPolicy, methodName);
    }

    public void ScheduleActiveActorTicks<TActor>(
        TimeSpan interval,
        TickBacklogPolicy backlogPolicy,
        string methodName)
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
