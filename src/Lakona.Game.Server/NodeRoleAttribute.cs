namespace Lakona.Game.Server;

/// <summary>
/// Assigns one stable application type to the node role which may execute it.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class NodeRoleAttribute : Attribute
{
    /// <summary>
    /// Initializes a node-role declaration.
    /// </summary>
    public NodeRoleAttribute(string role)
    {
        Role = NodeRoleName.Normalize(role);
    }

    /// <summary>
    /// Gets the normalized role name.
    /// </summary>
    public string Role { get; }
}

internal static class NodeRoleName
{
    internal static string Normalize(string role)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        var normalized = role.Trim().ToLowerInvariant();
        if (normalized.Length > 64
            || !char.IsAsciiLetter(normalized[0])
            || normalized.Any(static character =>
                !(char.IsAsciiLetterOrDigit(character) || character == '-')))
        {
            throw new ArgumentException(
                "Node roles must start with a letter and contain only lowercase letters, digits, or '-'.",
                nameof(role));
        }

        return normalized;
    }

    internal static string GetRequiredRole(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        var attribute = type.GetCustomAttributes(typeof(NodeRoleAttribute), inherit: false)
            .Cast<NodeRoleAttribute>()
            .SingleOrDefault();
        return attribute?.Role
            ?? throw new InvalidOperationException(
                $"Stable application type '{type.FullName}' must declare exactly one {nameof(NodeRoleAttribute)}.");
    }
}
