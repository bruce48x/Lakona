using Xunit;

namespace Lakona.Game.Cluster.Tests;

internal static class MembershipIntegrationTestEnvironment
{
    private const string RequiredEnvironmentVariable = "LAKONA_REQUIRE_MEMBERSHIP_PROVIDER_TESTS";

    public static string RequireConnectionString(string variable)
        => ResolveConnectionString(
            variable,
            Environment.GetEnvironmentVariable(variable),
            string.Equals(
                Environment.GetEnvironmentVariable(RequiredEnvironmentVariable),
                "true",
                StringComparison.OrdinalIgnoreCase));

    internal static string ResolveConnectionString(string variable, string? value, bool isRequired)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        if (isRequired)
        {
            throw new InvalidOperationException(
                $"{RequiredEnvironmentVariable}=true requires {variable}; skipping a mandatory Membership provider contract is not allowed.");
        }

        Assert.Skip($"Set {variable} to run this Membership provider contract.");
        return null!;
    }
}
