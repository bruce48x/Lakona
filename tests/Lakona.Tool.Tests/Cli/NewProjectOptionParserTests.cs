using Xunit;
using Lakona.Tool.Cli.Options;
using System.Globalization;
using ClientEngine = Lakona.ProjectSystem.LakonaClientEngine;
using ClientEngineVersion = Lakona.ProjectSystem.LakonaClientEngineVersion;
using DeploymentProfile = Lakona.ProjectSystem.LakonaDeploymentProfile;
using NuGetForUnitySource = Lakona.ProjectSystem.LakonaNuGetForUnitySource;
using SerializerKind = Lakona.ProjectSystem.LakonaSerializer;
using TransportKind = Lakona.ProjectSystem.LakonaTransport;

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
                "--client-engine", "unity",
                "--client-engine-version", "6.3",
                "--transport", "websocket",
                "--serializer", "json",
                "--nugetforunity-source", "embedded",
                "--deploy-profile", "compose"
            ]);

        Assert.Equal("Arena", options.ProjectName);
        Assert.Equal("out", options.OutputPath);
        Assert.Equal(ClientEngine.Unity, options.ClientEngine);
        Assert.Equal(ClientEngineVersion.Unity63, options.ClientEngineVersion);
        Assert.Equal(TransportKind.WebSocket, options.Transport);
        Assert.Equal(SerializerKind.Json, options.Serializer);
        Assert.Equal(NuGetForUnitySource.Embedded, options.NuGetForUnitySource);
        Assert.Equal(DeploymentProfile.Compose, options.DeploymentProfile);
        Assert.True(options.HasExplicit(NewProjectOptionPresence.Name));
        Assert.True(options.HasExplicit(NewProjectOptionPresence.OutputPath));
        Assert.True(options.HasExplicit(NewProjectOptionPresence.ClientEngine));
        Assert.True(options.HasExplicit(NewProjectOptionPresence.ClientEngineVersion));
        Assert.True(options.HasExplicit(NewProjectOptionPresence.Transport));
        Assert.True(options.HasExplicit(NewProjectOptionPresence.Serializer));
        Assert.True(options.HasExplicit(NewProjectOptionPresence.NuGetForUnitySource));
        Assert.True(options.HasExplicit(NewProjectOptionPresence.DeployProfile));
    }

    [Fact]
    public void Parse_RejectsRemovedPersistenceOption()
    {
        var exception = Assert.Throws<CliUsageException>(() =>
            NewProjectOptionParser.Parse(
                ["--persistence", "postgres"],
                ToolText.ForCulture(CultureInfo.InvariantCulture)));

        Assert.Contains("--persistence", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Unsupported option", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("2022", "Unity2022")]
    [InlineData("6.0", "Unity60")]
    [InlineData("6.3", "Unity63")]
    public void Parse_AcceptsSupportedUnityVersions(
        string value,
        string expectedName)
    {
        var options = NewProjectOptionParser.Parse(
            ["--client-engine", "unity", "--client-engine-version", value]);

        Assert.Equal(Enum.Parse<ClientEngineVersion>(expectedName), options.ClientEngineVersion);
    }

    [Theory]
    [InlineData("tuanjie", "1.6.7", "Tuanjie167")]
    [InlineData("godot", "4.6", "Godot46")]
    public void Parse_AcceptsCurrentVersionForSingleVersionEngines(
        string engine,
        string value,
        string expectedName)
    {
        var options = NewProjectOptionParser.Parse(
            ["--client-engine", engine, "--client-engine-version", value]);

        Assert.Equal(Enum.Parse<ClientEngineVersion>(expectedName), options.ClientEngineVersion);
    }

    [Theory]
    [InlineData("unity", "4.6", "2022|6.0|6.3")]
    [InlineData("tuanjie", "6.0", "1.6.7")]
    [InlineData("godot", "6.3", "4.6")]
    public void Parse_RejectsVersionNotSupportedBySelectedEngine(
        string engine,
        string version,
        string expectedVersions)
    {
        var exception = Assert.Throws<CliUsageException>(() =>
            NewProjectOptionParser.Parse(
                ["--client-engine", engine, "--client-engine-version", version],
                ToolText.ForCulture(CultureInfo.InvariantCulture)));

        Assert.Contains(engine, exception.Message, StringComparison.Ordinal);
        Assert.Contains(expectedVersions, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_RejectsClientEngineVersionForConsole()
    {
        var exception = Assert.Throws<CliUsageException>(() =>
            NewProjectOptionParser.Parse(
                ["--client-engine", "console", "--client-engine-version", "2022"],
                ToolText.ForCulture(CultureInfo.InvariantCulture)));

        Assert.Contains("does not apply", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_RejectsRemovedUnityCnClientEngine()
    {
        var exception = Assert.Throws<CliUsageException>(() =>
            NewProjectOptionParser.Parse(["--client-engine", "unity-cn"], ToolText.ForCulture(CultureInfo.InvariantCulture)));

        Assert.Contains("unity-cn", exception.Message, StringComparison.Ordinal);
        Assert.Contains("--client-engine", exception.Message, StringComparison.Ordinal);
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

    [Fact]
    public void Parse_AcceptsConsoleClientEngine()
    {
        var options = NewProjectOptionParser.Parse(
            [
                "--name", "Arena",
                "--client-engine", "console",
                "--transport", "kcp",
                "--serializer", "memorypack"
            ]);

        Assert.Equal(ClientEngine.Console, options.ClientEngine);
    }

    [Fact]
    public void Parse_RejectsMisspelledConsoleClientEngineWithUnsupportedValueDiagnostic()
    {
        var exception = Assert.Throws<CliUsageException>(() =>
            NewProjectOptionParser.Parse(["--client-engine", "consol"], ToolText.ForCulture(CultureInfo.InvariantCulture)));

        Assert.Contains("consol", exception.Message, StringComparison.Ordinal);
        Assert.Contains("--client-engine", exception.Message, StringComparison.Ordinal);
        Assert.Contains("console", exception.Message, StringComparison.Ordinal);
    }
}
