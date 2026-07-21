using System.Text;
using Lakona.Game.Cluster;
using Lakona.Game.Server.Configuration;
using Server.App.State.Contracts;

namespace Server.App;

public sealed class AgarBattleEndpointAdvertisement :
    INodeAdvertisementProvider,
    INodeAdvertisementResolver<GatewayEndpointDescriptor>
{
    public const string Kind = "agar.battle-endpoint";
    public const string Format = "absolute-uri-v1";
    private readonly LakonaGameRuntimeOptions runtime;
    private readonly IClusterMembership membership;

    public AgarBattleEndpointAdvertisement(
        LakonaGameRuntimeOptions runtime,
        IClusterMembership membership)
    {
        this.runtime = runtime;
        this.membership = membership;
    }

    public IReadOnlyList<NodeAdvertisement> Describe()
    {
        var endpoint = runtime.Endpoints.FirstOrDefault(IsBattleEndpoint);
        return endpoint is null
            ? []
            : [new NodeAdvertisement(
                Kind,
                Format,
                Encoding.UTF8.GetBytes(endpoint.ToAdvertisedEndpoint()))];
    }

    public bool TryResolve(
        NodeReference owner,
        out GatewayEndpointDescriptor? endpoint)
    {
        endpoint = null;
        var snapshot = membership.Current;
        if (!snapshot.TryGetMember(owner, out var member)
            || member is null
            || member.State != ClusterMemberState.Ready)
        {
            return false;
        }

        var advertisement = member.Advertisements.FirstOrDefault(item =>
            string.Equals(item.Kind, Kind, StringComparison.Ordinal)
            && string.Equals(item.Format, Format, StringComparison.Ordinal));
        if (advertisement is null
            || !Uri.TryCreate(Encoding.UTF8.GetString(advertisement.Payload.Span), UriKind.Absolute, out var uri))
        {
            return false;
        }

        endpoint = new GatewayEndpointDescriptor
        {
            InstanceId = owner.Node.Value,
            Transport = uri.Scheme,
            Host = uri.Host,
            Port = uri.Port,
            Path = uri.AbsolutePath == "/" ? string.Empty : uri.AbsolutePath
        };
        return true;
    }

    private static bool IsBattleEndpoint(LakonaGameEndpointOptions endpoint) =>
        string.Equals(endpoint.Transport, "kcp", StringComparison.OrdinalIgnoreCase)
        && (endpoint.RpcServices.Count == 0
            || endpoint.RpcServices.Contains("battle", StringComparer.OrdinalIgnoreCase)
            || endpoint.RpcServices.Contains("battle-runtime", StringComparer.OrdinalIgnoreCase));
}
