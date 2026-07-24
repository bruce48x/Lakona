using System.Reflection;
using Lakona.Game.Server.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Lakona.Game.Server.Tests.Hosting;

public sealed class LakonaGameServerConsoleLifetimeTests
{
    [Fact]
    public void CreateApplicationBuilder_SuppressesConsoleLifetimeStatusMessages()
    {
        var createBuilder = typeof(LakonaGameServerBootstrapper).GetMethod(
            "CreateApplicationBuilder",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(createBuilder);

        var builder = Assert.IsType<HostApplicationBuilder>(
            createBuilder.Invoke(null, [Array.Empty<string>()]));
        using var provider = builder.Services.BuildServiceProvider();
        var options = provider
            .GetRequiredService<IOptions<ConsoleLifetimeOptions>>()
            .Value;

        Assert.True(options.SuppressStatusMessages);
    }
}
