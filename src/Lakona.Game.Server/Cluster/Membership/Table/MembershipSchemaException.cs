namespace Lakona.Game.Cluster.Membership;

public sealed class MembershipSchemaException(string message, Exception innerException)
    : InvalidOperationException(message, innerException);
