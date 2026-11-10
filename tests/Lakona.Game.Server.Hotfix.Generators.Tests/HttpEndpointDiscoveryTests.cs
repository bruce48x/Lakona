using Lakona.Testing.Http;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Lakona.Game.Server.Hotfix.Generators.Tests;

public sealed class HttpEndpointDiscoveryTests
{
    [Theory]
    [MemberData(nameof(HttpEndpointDiscoveryCases.Cases), MemberType = typeof(HttpEndpointDiscoveryCases))]
    public void Compiler_enforces_endpoint_discovery_contract(string name, string members, bool hasEndpoint, string? error)
    {
        var result = GeneratorTestHost.Run(HttpEndpointDiscoveryCases.Source(members, hasEndpoint),
            new Dictionary<string, string> { ["build_property.LakonaProjectRole"] = "Hotfix" });
        Assert.True(!result.CompilationDiagnostics.Any(d => d.Severity == DiagnosticSeverity.Error),
            $"{name}: {string.Join(Environment.NewLine, result.CompilationDiagnostics)}");
        if (error is null) Assert.Empty(result.ErrorDiagnostics);
        else Assert.Contains(result.GeneratorDiagnostics, diagnostic => diagnostic.Id == error);
        if (!hasEndpoint)
            Assert.Contains(result.GeneratorDiagnostics, diagnostic => diagnostic.Id == "LAKONA20042");
    }
}
