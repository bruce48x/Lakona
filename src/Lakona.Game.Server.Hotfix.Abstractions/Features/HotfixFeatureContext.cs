using Microsoft.Extensions.DependencyInjection;
using Lakona.Game.Cluster;

namespace Lakona.Game.Server.Hotfix.Abstractions;

/// <summary>
/// Provides configuration state to a hotfix feature <c>Configure</c> method.
/// </summary>
public sealed class HotfixFeatureContext
{
    private readonly Dictionary<string, string> _metadata = new(StringComparer.Ordinal);
    private readonly List<HotfixFeatureCommandDeclaration> _commands = [];

    /// <summary>
    /// Gets feature command declarations registered by this feature.
    /// </summary>
    public IReadOnlyList<HotfixFeatureCommandDeclaration> Commands => _commands;

    /// <summary>
    /// Gets services registered by this hotfix feature.
    /// </summary>
    public IServiceCollection Services { get; } = new ServiceCollection();

    /// <summary>
    /// Gets or sets whether this feature is advertised through cluster discovery.
    /// </summary>
    public bool Discoverable { get; set; } = true;

    /// <summary>
    /// Gets low-cardinality feature metadata advertised with the feature.
    /// </summary>
    /// <remarks>
    /// Metadata must describe stable node capability and must not contain
    /// per-player, per-room, or other high-cardinality values.
    /// </remarks>
    public IDictionary<string, string> Metadata => _metadata;

    /// <summary>
    /// Registers a typed feature command handled by this hotfix feature.
    /// </summary>
    /// <typeparam name="TRequest">The command request type marked with <see cref="FeatureCommandAttribute"/>.</typeparam>
    /// <typeparam name="TReply">The command reply type.</typeparam>
    /// <param name="methodName">The command handler method name. Use <c>nameof(...)</c> for non-default names.</param>
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
