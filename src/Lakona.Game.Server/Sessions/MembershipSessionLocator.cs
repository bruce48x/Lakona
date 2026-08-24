using System.Text;
using Lakona.Game.Cluster;

namespace Lakona.Game.Server.Sessions;

public static class MembershipSessionLocator
{
    private const string Prefix = "l1.";

    public static string Encode(NodeReference gateway)
    {
        ArgumentNullException.ThrowIfNull(gateway);
        return Prefix
            + Base64Url(gateway.Cluster.Value.ToByteArray()) + "."
            + Base64Url(Encoding.UTF8.GetBytes(gateway.Node.Value)) + "."
            + Base64Url(gateway.Incarnation.Value.ToByteArray()) + "."
            + Base64Url(Guid.NewGuid().ToByteArray());
    }

    public static bool TryDecode(string sessionId, out NodeReference? gateway)
    {
        gateway = null;
        if (string.IsNullOrWhiteSpace(sessionId) || !sessionId.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var parts = sessionId.Split('.');
        if (parts.Length != 5 || parts[0] != "l1")
        {
            return false;
        }

        try
        {
            var clusterBytes = FromBase64Url(parts[1]);
            var nodeBytes = FromBase64Url(parts[2]);
            var incarnationBytes = FromBase64Url(parts[3]);
            var nonce = FromBase64Url(parts[4]);
            if (clusterBytes.Length != 16 || incarnationBytes.Length != 16 || nonce.Length != 16)
            {
                return false;
            }

            gateway = new NodeReference(
                new ClusterIncarnationId(new Guid(clusterBytes)),
                new NodeId(new UTF8Encoding(false, true).GetString(nodeBytes)),
                new NodeIncarnationId(new Guid(incarnationBytes)));
            return true;
        }
        catch (Exception exception) when (
            exception is FormatException or ArgumentException or DecoderFallbackException)
        {
            return false;
        }
    }

    internal static bool TryResolve(
        GameSessionKey session,
        IClusterMembership membership,
        out RouteLocation? target)
    {
        ArgumentNullException.ThrowIfNull(membership);
        target = null;
        if (!TryDecode(session.SessionId, out var gateway))
        {
            return false;
        }

        var snapshot = membership.Current;
        if (!snapshot.TryGetMember(gateway!, out var member)
            || member is null
            || member.State != ClusterMemberState.Active)
        {
            return false;
        }

        target = new RouteLocation(
            ClientNotificationRouteKey.FromSession(session),
            gateway!,
            snapshot.View,
            member.ClusterEndpoint);
        return true;
    }

    internal static ClientNotificationStatus ClassifyMissing(string sessionId, IClusterMembership membership)
    {
        if (!TryDecode(sessionId, out var gateway)) return ClientNotificationStatus.RouteNotFound;
        var snapshot = membership.Current;
        return gateway!.Cluster == snapshot.Cluster && !snapshot.TryGetMember(gateway, out _)
            ? ClientNotificationStatus.StateLost
            : ClientNotificationStatus.RouteNotFound;
    }

    private static string Base64Url(byte[] value) => Convert.ToBase64String(value)
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');

    private static byte[] FromBase64Url(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - padded.Length % 4) % 4);
        return Convert.FromBase64String(padded);
    }
}

public sealed class MembershipGameSessionIdFactory : IGameSessionIdFactory
{
    private readonly IClusterMembership membership;
    private readonly NodeId localNode;

    public MembershipGameSessionIdFactory(IClusterMembership membership, NodeId localNode)
    {
        this.membership = membership ?? throw new ArgumentNullException(nameof(membership));
        this.localNode = localNode;
    }

    public string Create()
    {
        var snapshot = membership.Current;
        var matches = snapshot.Members.Where(member =>
            member.Reference.Node == localNode
            && member.State == ClusterMemberState.Active).ToArray();
        if (matches.Length != 1)
        {
            throw new InvalidOperationException(
                "A session can be created only for one exact Active local gateway incarnation.");
        }

        return MembershipSessionLocator.Encode(matches[0].Reference);
    }
}
