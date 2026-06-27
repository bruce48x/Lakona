using Microsoft.Extensions.Configuration;
using Lakona.Game.Server.Configuration;
using Xunit;

namespace Lakona.Game.Server.Tests;

public sealed class LakonaGameServerHostingOptionsTests
{
    [Fact]
    public void FromConfiguration_reads_actor_and_session_cleanup_options()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Lakona:Actors:MailboxCapacity"] = "64",
                ["Lakona:Actors:CallTimeoutSeconds"] = "5",
                ["Lakona:Actors:SlowMessageThresholdSeconds"] = "1",
                ["Lakona:Sessions:Cleanup:Enabled"] = "false",
                ["Lakona:Sessions:Cleanup:IntervalSeconds"] = "7",
                ["Lakona:Sessions:Cleanup:DisconnectedRetentionSeconds"] = "11"
            })
            .Build();

        var options = LakonaGameHostingOptions.FromConfiguration(configuration);

        Assert.Equal(64, options.Actors.MailboxCapacity);
        Assert.Equal(TimeSpan.FromSeconds(5), options.Actors.CallTimeout);
        Assert.Equal(TimeSpan.FromSeconds(1), options.Actors.SlowMessageThreshold);
        Assert.False(options.Sessions.Cleanup.Enabled);
        Assert.Equal(TimeSpan.FromSeconds(7), options.Sessions.Cleanup.Interval);
        Assert.Equal(TimeSpan.FromSeconds(11), options.Sessions.Cleanup.DisconnectedSessionRetention);
    }

    [Fact]
    public void FromConfiguration_uses_generated_server_defaults_when_values_are_missing()
    {
        var options = LakonaGameHostingOptions.FromConfiguration(new ConfigurationBuilder().Build());

        Assert.Equal(4096, options.Actors.MailboxCapacity);
        Assert.Equal(TimeSpan.FromSeconds(30), options.Actors.CallTimeout);
        Assert.Null(options.Actors.SlowMessageThreshold);
        Assert.True(options.Sessions.Cleanup.Enabled);
        Assert.Equal(TimeSpan.FromSeconds(30), options.Sessions.Cleanup.Interval);
        Assert.Equal(TimeSpan.FromMinutes(2), options.Sessions.Cleanup.DisconnectedSessionRetention);
    }

    [Fact]
    public void FromConfiguration_ignores_legacy_lakona_game_root()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Lakona.Game:Actors:MailboxCapacity"] = "64",
                ["Lakona.Game:Sessions:Cleanup:Enabled"] = "false"
            })
            .Build();

        var options = LakonaGameHostingOptions.FromConfiguration(configuration);

        Assert.Equal(4096, options.Actors.MailboxCapacity);
        Assert.True(options.Sessions.Cleanup.Enabled);
    }

    [Fact]
    public void Runtime_options_bind_cluster_directory_options()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Lakona:Node:Id"] = "data-1",
                ["Lakona:Cluster:Endpoint"] = "tcp://127.0.0.1:21001",
                ["Lakona:Cluster:Serializer"] = "memorypack",
                ["Lakona:Cluster:Directory:Provider"] = "postgres",
                ["Lakona:Cluster:Directory:ConnectionStringName"] = "LakonaClusterPostgres",
                ["Lakona:Cluster:Directory:NodeTable"] = "lakona_cluster_nodes",
                ["Lakona:Cluster:Directory:EnsureSchemaOnStartup"] = "false"
            })
            .Build();

        var options = LakonaGameRuntimeOptions.FromConfiguration(configuration);

        Assert.Equal("postgres", options.Cluster!.Directory.Provider);
        Assert.Equal("LakonaClusterPostgres", options.Cluster.Directory.ConnectionStringName);
        Assert.Equal("lakona_cluster_nodes", options.Cluster.Directory.NodeTable);
        Assert.False(options.Cluster.Directory.EnsureSchemaOnStartup);
    }
}
