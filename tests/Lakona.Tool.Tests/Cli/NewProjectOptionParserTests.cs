using Xunit;
using Lakona.Tool.Cli.Options;
using Lakona.Tool.Domain;
using System.Globalization;

namespace Lakona.Tool.Tests.Cli;

public sealed class NewProjectOptionParserTests
{
    [Fact]
    public void Parse_ReturnsTypedOptions()
    {
        var options = NewProjectOptionParser.Parse(
            [
                "--name", "Arena",
                "--output", "out",
                "--client-engine", "unity-cn",
                "--transport", "websocket",
                "--serializer", "json",
                "--persistence", "mysql",
                "--nugetforunity-source", "embedded",
                "--deploy-profile", "compose"
            ]);

        Assert.Equal("Arena", options.ProjectName);
        Assert.Equal("out", options.OutputPath);
        Assert.Equal(ClientEngine.UnityCn, options.ClientEngine);
        Assert.Equal(TransportKind.WebSocket, options.Transport);
        Assert.Equal(SerializerKind.Json, options.Serializer);
        Assert.Equal(PersistenceKind.MySql, options.Persistence);
        Assert.Equal(NuGetForUnitySource.Embedded, options.NuGetForUnitySource);
        Assert.Equal(DeploymentProfile.Compose, options.DeploymentProfile);
        Assert.True(options.HasExplicit(NewProjectOptionPresence.Name));
        Assert.True(options.HasExplicit(NewProjectOptionPresence.OutputPath));
        Assert.True(options.HasExplicit(NewProjectOptionPresence.ClientEngine));
        Assert.True(options.HasExplicit(NewProjectOptionPresence.Transport));
        Assert.True(options.HasExplicit(NewProjectOptionPresence.Serializer));
        Assert.True(options.HasExplicit(NewProjectOptionPresence.Persistence));
        Assert.True(options.HasExplicit(NewProjectOptionPresence.NuGetForUnitySource));
        Assert.True(options.HasExplicit(NewProjectOptionPresence.DeployProfile));
    }

    [Fact]
    public void Parse_RejectsNetworkProfileAsUnsupportedOption()
    {
        var optionName = string.Concat("--network", "-profile");
        var exception = Assert.Throws<CliUsageException>(() =>
            NewProjectOptionParser.Parse([optionName, "cluster"], ToolText.ForCulture(CultureInfo.InvariantCulture)));

        Assert.Contains(optionName, exception.Message, StringComparison.Ordinal);
        Assert.Contains("Unsupported option", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
