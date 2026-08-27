namespace Lakona.Game.Cluster.Membership;

public sealed class ClusterBuildTag
{
    public ClusterBuildTag(string value)
    {
        if (string.IsNullOrEmpty(value)
            || value.Length > 64
            || value.Any(static character => !IsAsciiLetterOrDigit(character)))
        {
            throw new ArgumentException(
                "LakonaBuildTag must contain 1 to 64 ASCII letters or digits.",
                nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    private static bool IsAsciiLetterOrDigit(char value) =>
        value is >= '0' and <= '9'
        or >= 'A' and <= 'Z'
        or >= 'a' and <= 'z';
}
