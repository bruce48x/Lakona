namespace Lakona.Game.Cluster.Membership;

public sealed class MembershipSchemaException : InvalidOperationException
{
    public MembershipSchemaException(string message)
        : base(message)
    {
    }

    public MembershipSchemaException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
