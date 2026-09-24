using Lakona.Game.Server.Configuration;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Lakona.Game.Server.Tests.Configuration;

public sealed class ConfigurationInputContractTests
{
    [Theory]
    [InlineData("[\"battle\"]", "battle")]
    [InlineData("[]", "")]
    public void Json_roles_replace_lower_priority_indexed_roles(string json, string expected)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Lakona:Node:Roles:0"] = "gateway",
                ["Lakona:Node:Roles:1"] = "data"
            })
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Lakona:Node:Roles"] = json })
            .Build();

        Assert.Equal(expected, string.Join(',', LakonaGameRuntimeOptions.FromConfiguration(configuration).Node.Roles));
    }

    [Theory]
    [InlineData("Lakona:Timers:MaxActiveTimers", "not-a-number")]
    [InlineData("Lakona:Notifications:MaximumPendingPerProcess", "1e6")]
    [InlineData("Lakona:Endpoints:0:ReliablePush", "tru")]
    [InlineData("Lakona:Management:Admin:Enabled", "yes")]
    [InlineData("Lakona:Sessions:ResumeWindowSeconds", "NaN")]
    [InlineData("Lakona:Timers:MaxActiveTimers", "")]
    public void Explicit_invalid_scalars_fail_with_the_configuration_path(string path, string value)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [path] = value }).Build();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            LakonaGameRuntimeOptions.FromConfiguration(configuration));

        Assert.Contains(path, exception.Message);
    }
}
