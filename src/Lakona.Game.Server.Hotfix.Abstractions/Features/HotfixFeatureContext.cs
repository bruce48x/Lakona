using Microsoft.Extensions.DependencyInjection;
using Lakona.Game.Cluster;

namespace Lakona.Game.Server.Hotfix.Abstractions;

public sealed class HotfixFeatureContext
{
    private readonly Dictionary<string, string> _metadata = new(StringComparer.Ordinal);
    private readonly List<HotfixLocalActorDeclaration> _localActors = [];
    private readonly List<HotfixFeatureCommandDeclaration> _commands = [];

    public IReadOnlyList<HotfixLocalActorDeclaration> LocalActors => _localActors;

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
}
