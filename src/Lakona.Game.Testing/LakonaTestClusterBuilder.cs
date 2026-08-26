using System.Reflection;
using Lakona.Game.Cluster;
using Lakona.Game.Server;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lakona.Game.Testing;

/// <summary>Builds an in-process cluster of real Lakona server hosts.</summary>
public sealed class LakonaTestClusterBuilder
{
    private readonly Dictionary<string, LakonaTestNodeSpecification> nodes =
        new(StringComparer.Ordinal);
    private readonly List<Action<LakonaTestNodeBuilder>> configureNodes = [];

    /// <summary>Adds one initial node to the cluster.</summary>
    public LakonaTestClusterBuilder AddNode(string nodeId, params string[] roles)
    {
        var specification = LakonaTestNodeSpecification.Create(nodeId, roles);
        if (!nodes.TryAdd(specification.NodeId, specification))
        {
            throw new InvalidOperationException(
                $"Lakona TestCluster already contains node '{specification.NodeId}'.");
        }

        return this;
    }

    /// <summary>Configures every initial node and every node added after startup.</summary>
    public LakonaTestClusterBuilder ConfigureNodes(Action<LakonaTestNodeBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configureNodes.Add(configure);
        return this;
    }

    /// <summary>Creates the cluster without starting its nodes.</summary>
    public LakonaTestCluster Build()
    {
        if (nodes.Count == 0)
        {
            throw new InvalidOperationException(
                "Lakona TestCluster requires at least one initial node.");
        }

        var configured = nodes.Values
            .Select(Configure)
            .ToDictionary(static node => node.NodeId, StringComparer.Ordinal);
        return new LakonaTestCluster(configured, configureNodes.ToArray());
    }

    private LakonaTestNodeSpecification Configure(LakonaTestNodeSpecification source)
    {
        var specification = source.Clone();
        var builder = new LakonaTestNodeBuilder(specification);
        foreach (var configure in configureNodes)
        {
            configure(builder);
        }

        return specification;
    }
}

/// <summary>Configures one node selected by <see cref="LakonaTestClusterBuilder.ConfigureNodes"/>.</summary>
public sealed class LakonaTestNodeBuilder
{
    private readonly LakonaTestNodeSpecification specification;

    internal LakonaTestNodeBuilder(LakonaTestNodeSpecification specification) =>
        this.specification = specification;

    public string NodeId => specification.NodeId;

    public IReadOnlyList<string> Roles => specification.Roles;

    public bool HasRole(string role)
    {
        var normalized = NormalizeRole(role);
        return Roles.Contains(normalized, StringComparer.Ordinal);
    }

    public LakonaTestNodeBuilder ConfigureAppConfiguration(
        Action<IConfigurationBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        specification.ConfigurationActions.Add(configure);
        return this;
    }

    public LakonaTestNodeBuilder ConfigureServices(
        Action<IServiceCollection, IConfiguration> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        specification.ServiceActions.Add(configure);
        return this;
    }

    /// <summary>Loads one Hotfix assembly as this node's Actor application.</summary>
    public LakonaTestNodeBuilder UseHotfixAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        if (specification.HotfixAssembly is not null)
        {
            throw new InvalidOperationException(
                $"Lakona TestCluster node '{NodeId}' already has a Hotfix assembly.");
        }

        specification.HotfixAssembly = assembly;
        return this;
    }

    private static string NormalizeRole(string role)
        => new NodeRoleAttribute(role).Role;

    internal static string NormalizeRoleName(string role) => NormalizeRole(role);
}

internal sealed class LakonaTestNodeSpecification
{
    private LakonaTestNodeSpecification(string nodeId, IReadOnlyList<string> roles)
    {
        NodeId = nodeId;
        Roles = roles;
    }

    internal string NodeId { get; }

    internal IReadOnlyList<string> Roles { get; }

    internal List<Action<IConfigurationBuilder>> ConfigurationActions { get; } = [];

    internal List<Action<IServiceCollection, IConfiguration>> ServiceActions { get; } = [];

    internal Assembly? HotfixAssembly { get; set; }

    internal static LakonaTestNodeSpecification Create(
        string nodeId,
        IEnumerable<string>? roles)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        var normalizedNodeId = nodeId.Trim();
        _ = new NodeId(normalizedNodeId);
        var normalizedRoles = (roles ?? [])
            .Select(LakonaTestNodeBuilder.NormalizeRoleName)
            .ToArray();
        if (normalizedRoles.Distinct(StringComparer.Ordinal).Count() != normalizedRoles.Length)
        {
            throw new ArgumentException(
                $"Node '{normalizedNodeId}' contains duplicate roles.",
                nameof(roles));
        }

        return new LakonaTestNodeSpecification(normalizedNodeId, normalizedRoles);
    }

    internal LakonaTestNodeSpecification Clone()
    {
        var clone = new LakonaTestNodeSpecification(NodeId, Roles.ToArray());
        clone.ConfigurationActions.AddRange(ConfigurationActions);
        clone.ServiceActions.AddRange(ServiceActions);
        clone.HotfixAssembly = HotfixAssembly;
        return clone;
    }
}
