using Xunit;

namespace Lakona.Game.Cluster.Tests;

internal static class MembershipIntegrationTestEnvironment
{
    public static string RequireConnectionString(string variable)
        => ResolveConnectionString(
            variable,
            Environment.GetEnvironmentVariable(variable),
            string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase));

    internal static string ResolveConnectionString(string variable, string? value, bool isCi)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        if (isCi)
        {
            throw new InvalidOperationException(
                $"CI must provide {variable}; skipping a mandatory Membership provider contract is not allowed.");
        }

        Assert.Skip($"Set {variable} to run this Membership provider contract.");
        return null!;
    }
}
