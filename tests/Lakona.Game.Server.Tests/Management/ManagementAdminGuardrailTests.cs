using Lakona.Game.Server.Guardrails;
using Lakona.Game.Server.Guardrails.Rules;
using Xunit;

namespace Lakona.Game.Server.Tests.Management;

public sealed class ManagementAdminGuardrailTests
{
    [Fact]
    public void Validate_rejects_loopback_only_admin_on_non_loopback_listener()
    {
        var result = Validate(TestRuntime("0.0.0.0", requireLoopback: true));

        var diagnostic = Assert.Single(result.Diagnostics, d => d.Code == "LAKONA130");
        Assert.Equal(LakonaGameDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("Lakona:Management:Admin:RequireLoopback", diagnostic.Message);
    }

    [Fact]
    public void Validate_accepts_explicit_trusted_network_admin_listener()
    {
        var result = Validate(TestRuntime("0.0.0.0", requireLoopback: false));

        Assert.Empty(result.Diagnostics);
    }

    private static LakonaGameValidationResult Validate(LakonaGameResolvedRuntime runtime)
    {
        return new LakonaGameRuntimeValidator([new ManagementAdminRule()]).Validate(runtime);
    }

    private static LakonaGameResolvedRuntime TestRuntime(string host, bool requireLoopback)
    {
        return new LakonaGameResolvedRuntime(
            NodeId: new("dev-1", LakonaGameValueSource.Configuration),
            Endpoints: [],
            Cluster: new(new Dictionary<string, string>()),
            ClusterEndpoint: null,
            Hotfix: new(new("Server.Hotfix.dll", LakonaGameValueSource.GeneratedConvention), new("Server.Hotfix.dll", LakonaGameValueSource.GeneratedConvention)),
            ReliablePush: new(new("InMemory", LakonaGameValueSource.Default), new(256, LakonaGameValueSource.Default), new(60, LakonaGameValueSource.Default), true),
            Heartbeat: new(new(TimeSpan.FromSeconds(15), LakonaGameValueSource.Default), new(TimeSpan.FromSeconds(45), LakonaGameValueSource.Default)),
            Management: new(
                new(true, LakonaGameValueSource.Configuration, "Lakona:Management:Admin:Enabled"),
                new(host, LakonaGameValueSource.Configuration, "Lakona:Management:Http:Host"),
                new(requireLoopback, LakonaGameValueSource.Configuration, "Lakona:Management:Admin:RequireLoopback")));
    }
}
