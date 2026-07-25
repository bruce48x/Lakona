using Microsoft.Extensions.Configuration;
using Lakona.Game.Server.Configuration;
using Xunit;

namespace Lakona.Game.Server.Tests;

public sealed class LakonaGameServerHostingOptionsTests
{
    [Fact]
    public void FromConfiguration_reads_actor_session_resume_and_cleanup_options()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Lakona:Actors:MailboxCapacity"] = "64",
                ["Lakona:Actors:CallTimeoutSeconds"] = "5",
                ["Lakona:Actors:SlowMessageThresholdSeconds"] = "1",
                ["Lakona:Sessions:Cleanup:Enabled"] = "false",
                ["Lakona:Sessions:Cleanup:IntervalSeconds"] = "7",
                ["Lakona:Sessions:ResumeWindowSeconds"] = "11"
            })
            .Build();

        var options = LakonaGameHostingOptions.FromConfiguration(configuration);

        Assert.Equal(64, options.Actors.MailboxCapacity);
        Assert.Equal(TimeSpan.FromSeconds(5), options.Actors.CallTimeout);
        Assert.Equal(TimeSpan.FromSeconds(1), options.Actors.SlowMessageThreshold);
        Assert.False(options.Sessions.Cleanup.Enabled);
        Assert.Equal(TimeSpan.FromSeconds(7), options.Sessions.Cleanup.Interval);
        Assert.Equal(TimeSpan.FromSeconds(11), options.Sessions.ResumeWindow);
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
        Assert.Equal(TimeSpan.FromSeconds(60), options.Sessions.ResumeWindow);
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

}
