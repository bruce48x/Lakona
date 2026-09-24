using System.Runtime.Loader;
using Lakona.Game.Server.Hotfix.Loading;
using Lakona.Game.Server.Hotfix.Scanning;
using Lakona.Game.Server.Http;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Lakona.Game.Server.Hotfix.Tests;

public sealed class HotfixHttpTypeIdentityTests
{
    [Theory]
    [InlineData("LakonaHttpServiceAttribute")]
    [InlineData("LakonaHttpEndpointAttribute")]
    [InlineData("LakonaHttpCall")]
    [InlineData("LakonaHttpResponse")]
    public void Scanner_does_not_bind_same_named_types_from_another_assembly(string typeName)
    {
        var declaration = typeName switch
        {
            "LakonaHttpServiceAttribute" =>
                "public sealed class LakonaHttpServiceAttribute(string name) : System.Attribute { }",
            "LakonaHttpEndpointAttribute" =>
                "public sealed class LakonaHttpEndpointAttribute(string method, string route) : System.Attribute { }",
            _ => $"public sealed class {typeName} {{ }}"
        };
        using var fixture = new CompiledHttpFixture($$"""
            using System.Threading.Tasks;
            using Lakona.Game.Server.Http;
            namespace Lakona.Game.Server.Http { {{declaration}} }
            [LakonaHttpService("identity")]
            public sealed class HttpService
            {
                [LakonaHttpEndpoint("GET", "/identity")]
                public ValueTask<LakonaHttpResponse> Handle(LakonaHttpCall call) => default;
            }
            """);
        var context = new HotfixAssemblyLoadContext(fixture.Path, []);
        try
        {
            var scan = HotfixBehaviorScanner.Scan(context.LoadMainAssemblyFromBytes(fixture.Path));
            Assert.Empty(scan.HttpEndpoints);
            if (typeName == "LakonaHttpServiceAttribute")
                Assert.True(scan.Succeeded, string.Join(Environment.NewLine, scan.Diagnostics));
            else
                Assert.False(scan.Succeeded);
        }
        finally
        {
            context.Unload();
        }
    }

    [Fact]
    public async Task Reload_shares_http_types_and_dispatches_from_collectible_generation()
    {
        using var fixture = new CompiledHttpFixture("""
            using System.Threading.Tasks;
            using Lakona.Game.Server.Http;
            [LakonaHttpService("identity")]
            public sealed class HttpService
            {
                [LakonaHttpEndpoint("GET", "/identity")]
                public ValueTask<LakonaHttpResponse> Handle(LakonaHttpCall call) =>
                    new ValueTask<LakonaHttpResponse>(LakonaHttpResponse.Text(call.Request.TraceIdentifier));
            }
            """);
        await using var manager = new HotfixManager(new CurrentDirectoryHotfixAssemblySource(
            System.IO.Path.GetDirectoryName(fixture.Path)!, System.IO.Path.GetFileName(fixture.Path)));
        var result = await manager.ReloadAsync(TestContext.Current.CancellationToken);
        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        using var lease = ((IHotfixRuntimeAccessor)manager).AcquireCurrent();
        var runtime = lease.Snapshot;
        var serviceType = runtime.MainAssembly!.GetType("HttpService")!;
        Assert.True(AssemblyLoadContext.GetLoadContext(serviceType.Assembly)!.IsCollectible);
        var method = serviceType.GetMethod("Handle")!;
        Assert.Equal(typeof(LakonaHttpCall), Assert.Single(method.GetParameters()).ParameterType);
        Assert.Equal(typeof(ValueTask<LakonaHttpResponse>), method.ReturnType);
        Assert.Single(runtime.HttpEndpoints);
        var request = new LakonaHttpRequest(
            ReadOnlyMemory<byte>.Empty,
            new Dictionary<string, string[]>(),
            new Dictionary<string, string[]>(),
            new Dictionary<string, string>(),
            AuthenticatedName: null, RemoteEndpoint: null, TraceIdentifier: "shared-http");
        var response = await runtime.Invoker.InvokeHttpAsync<LakonaHttpCall, LakonaHttpResponse>(
            0, new LakonaHttpCall(request, runtime.Services, TestContext.Current.CancellationToken),
            TestContext.Current.CancellationToken);
        Assert.Equal(200, response.StatusCode);
        Assert.Equal("shared-http", System.Text.Encoding.UTF8.GetString(response.Body.Span));
    }

    private sealed class CompiledHttpFixture : IDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "lakona-http-identity-" + Guid.NewGuid().ToString("N"));

        public CompiledHttpFixture(string source)
        {
            var compilation = CSharpCompilation.Create(
                "HttpIdentity_" + Guid.NewGuid().ToString("N"),
                [CSharpSyntaxTree.ParseText(source)],
                HotfixTestMetadataReferences.CreateDefaultReferences(typeof(HotfixManager)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            using var bytes = new MemoryStream();
            var emit = compilation.Emit(bytes);
            Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
            Directory.CreateDirectory(_directory);
            Path = System.IO.Path.Combine(_directory, "Hotfix.dll");
            File.WriteAllBytes(Path, bytes.ToArray());
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(_directory, recursive: true);
    }
}
