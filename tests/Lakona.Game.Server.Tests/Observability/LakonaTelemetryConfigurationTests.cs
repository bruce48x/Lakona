using Lakona.Game.Server.Configuration;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Lakona.Game.Server.Tests.Observability;

public sealed class LakonaTelemetryConfigurationTests
{
    [Fact]
    public void RuntimeOptions_DefaultManagementAdminIsDisabledAndLoopbackOnly()
    {
        var options = LakonaGameRuntimeOptions.FromConfiguration(BuildConfiguration([]));

        Assert.False(options.Management.Admin.Enabled);
        Assert.True(options.Management.Admin.RequireLoopback);
    }

    [Fact]
    public void RuntimeOptions_BindsManagementAdminPolicy()
    {
        var options = LakonaGameRuntimeOptions.FromConfiguration(BuildConfiguration(
            new Dictionary<string, string?>
            {
                ["Lakona:Management:Admin:Enabled"] = "true",
                ["Lakona:Management:Admin:RequireLoopback"] = "false"
            }));

        Assert.True(options.Management.Admin.Enabled);
        Assert.False(options.Management.Admin.RequireLoopback);
    }

    [Fact]
    public void RuntimeOptions_RejectsRemovedPrivateObservabilityConfiguration()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Lakona:Observability:Metrics:Prometheus:Enabled"] = "true"
        });

        var exception = Assert.Throws<InvalidOperationException>(
            () => LakonaGameRuntimeOptions.FromConfiguration(configuration));

        Assert.Contains("Lakona:Observability was removed", exception.Message, StringComparison.Ordinal);
        Assert.Contains("OpenTelemetry", exception.Message, StringComparison.Ordinal);
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }
}
