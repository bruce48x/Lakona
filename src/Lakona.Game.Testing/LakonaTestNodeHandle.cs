using Lakona.Game.Cluster;
using Microsoft.Extensions.Hosting;

namespace Lakona.Game.Testing;

/// <summary>Represents one process-like Lakona node hosted by a test cluster.</summary>
public sealed class LakonaTestNodeHandle
{
    private int active = 1;

    internal LakonaTestNodeHandle(
        LakonaTestNodeSpecification specification,
        IHost host,
        NodeReference reference,
        int endpointPort)
    {
        Specification = specification;
        Host = host;
        Reference = reference;
        EndpointPort = endpointPort;
    }

    internal LakonaTestNodeSpecification Specification { get; }

    internal IHost Host { get; }

    internal int EndpointPort { get; }

    public string NodeId => Specification.NodeId;

    public IReadOnlyList<string> Roles => Specification.Roles;

    public IServiceProvider Services => Host.Services;

    public NodeReference Reference { get; }

    public bool IsActive => Volatile.Read(ref active) != 0;

    internal bool TryDeactivate() => Interlocked.Exchange(ref active, 0) != 0;
}
