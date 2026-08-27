namespace Lakona.Game.Cluster.Membership;

internal sealed class MembershipSchemaException(string message, Exception innerException)
    : InvalidOperationException(message, innerException);
