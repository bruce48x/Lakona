using System.Runtime.Loader;
using Lakona.Game.Server.Hotfix.Dispatch;
using Lakona.Game.Server.Hotfix.Scanning;
using Lakona.Game.Server.Http;
using Lakona.Testing.Http;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lakona.Game.Server.Hotfix.Tests;

public sealed class HttpEndpointDiscoveryTests
{
    [Theory]
    [MemberData(nameof(HttpEndpointDiscoveryCases.Cases), MemberType = typeof(HttpEndpointDiscoveryCases))]
    public async Task Runtime_enforces_same_contract_and_preserves_manifest_and_dispatch(string name, string members, bool hasEndpoint, string? error)
    {
        // Compile without analyzers: runtime validation must independently reject
        // malformed modules, including ones built without the framework generator.
        var compilation = CSharpCompilation.Create("HttpDiscovery_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(HttpEndpointDiscoveryCases.Source(members, hasEndpoint), cancellationToken: TestContext.Current.CancellationToken)],
            HotfixTestMetadataReferences.CreateDefaultReferences(typeof(LakonaHttpCall)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var bytes = new MemoryStream();
        var emit = compilation.Emit(bytes, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
        bytes.Position = 0;
        var context = new AssemblyLoadContext("http-discovery", isCollectible: true);
        try
        {
            var assembly = context.LoadFromStream(bytes);
            var scan = HotfixBehaviorScanner.Scan(assembly);
            Assert.True((error is null) == scan.Succeeded, $"{name}: {string.Join(Environment.NewLine, scan.Diagnostics)}");
            if (!hasEndpoint)
            {
                Assert.Empty(scan.HttpEndpoints);
                Assert.Contains(scan.Diagnostics, message => message.Contains("at least one valid endpoint", StringComparison.Ordinal));
            }
            if (error is not null)
            {
                if (error == "LAKONA20043")
                    Assert.Contains(scan.Diagnostics, message => message.Contains("must declare [LakonaHttpEndpoint]", StringComparison.Ordinal)
                        || message.Contains("must be a public instance non-generic method", StringComparison.Ordinal));
                return;
            }

            var binding = Assert.Single(scan.HttpEndpoints);
            Assert.Equal("discovery", binding.Endpoint.Service);
            Assert.Equal("POST", binding.Endpoint.Method);
            Assert.Equal("/discovery", binding.Endpoint.RoutePattern);
            Assert.Equal("Handle", binding.Method.Name);
            await using var table = new HotfixDispatchTable(1, scan.Methods, scan.Services,
                scan.ActorMethods, scan.ActorLifecycles, scan.TimerMethods, scan.HttpEndpoints);
            await using var services = new ServiceCollection().BuildServiceProvider();
            table.ValidateModuleActivation(services);
            var request = new LakonaHttpRequest(ReadOnlyMemory<byte>.Empty,
                new Dictionary<string, string[]>(), new Dictionary<string, string[]>(), new Dictionary<string, string>(),
                AuthenticatedName: null, RemoteEndpoint: null, TraceIdentifier: "discovery-call");
            var call = new LakonaHttpCall(request, services, TestContext.Current.CancellationToken);
            var response = await new HotfixServiceInvoker(table).InvokeHttpAsync<LakonaHttpCall, LakonaHttpResponse>(
                0, call, TestContext.Current.CancellationToken);
            Assert.Equal(200, response.StatusCode);
            Assert.Equal("discovery-call", System.Text.Encoding.UTF8.GetString(response.Body.Span));
        }
        finally { context.Unload(); }
    }
}
