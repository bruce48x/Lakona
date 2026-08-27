using Xunit;

namespace Lakona.Game.Cluster.Tests;

public sealed class MembershipIntegrationTestEnvironmentTests
{
    [Fact]
    public void MissingConnectionStringFailsInCiInsteadOfSkipping()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            MembershipIntegrationTestEnvironment.ResolveConnectionString(
                "LAKONA_TEST_PROVIDER_CONNECTION",
                null,
                isCi: true));

        Assert.Contains("skipping", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not allowed", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConfiguredConnectionStringIsReturnedInCi()
    {
        const string connection = "provider-connection";

        Assert.Equal(
            connection,
            MembershipIntegrationTestEnvironment.ResolveConnectionString(
                "LAKONA_TEST_PROVIDER_CONNECTION",
                connection,
                isCi: true));
    }
}
