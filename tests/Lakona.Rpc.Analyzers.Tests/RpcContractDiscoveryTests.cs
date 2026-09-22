using Microsoft.CodeAnalysis;
using Xunit;

namespace Lakona.Rpc.Analyzers.Tests;

public sealed class RpcContractDiscoveryTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    public void Contracts_at_each_nesting_depth_generate_compilable_client_and_server(int depth, bool referenced)
    {
        var compilation = CreateCompilation(WrapContracts(Contracts, depth), referenced);
        var result = AnalyzerTestHelpers.RunGenerator(compilation, Options(), out var output);

        Assert.Empty(result.Diagnostics);
        Assert.Empty(AnalyzerTestHelpers.ErrorDiagnostics(output));
        Assert.Equal(
            new[] { "AllServicesBinder.g.cs", "EchoClient.g.cs", "EchoNotificationsBinder.g.cs",
                "EchoNotificationsProxy.g.cs", "EchoBinder.g.cs", "RpcApi.g.cs" }.OrderBy(name => name),
            result.Results.Single().GeneratedSources.Select(source => source.HintName).OrderBy(name => name));
        using var assembly = new MemoryStream();
        var emit = output.Emit(assembly);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Distinct_nested_services_with_the_same_id_still_report_a_duplicate(bool referenced)
    {
        var source = WrapContracts(Contracts + """

            [RpcService(77)]
            public interface IOther
            {
                [RpcMethod(1)]
                ValueTask<Reply> EchoAsync(Request request);
            }
            """, 2);
        var compilation = CreateCompilation(source, referenced);
        var result = AnalyzerTestHelpers.RunGenerator(compilation, Options(), out _);

        var diagnostic = Assert.Single(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Equal("LAKONA40001", diagnostic.Id);
        Assert.Contains("Duplicate ServiceId 77", diagnostic.GetMessage());
        Assert.Contains("'IEcho'", diagnostic.GetMessage());
        Assert.Contains("'IOther'", diagnostic.GetMessage());
        Assert.Empty(result.Results.Single().GeneratedSources);
    }

    private static Microsoft.CodeAnalysis.CSharp.CSharpCompilation CreateCompilation(string source, bool referenced)
    {
        var contracts = AnalyzerTestHelpers.CreateCompilation(source, "DiscoveryContracts");
        return referenced
            ? AnalyzerTestHelpers.CreateCompilation("public sealed class App { }",
                additionalReferences: new[] { AnalyzerTestHelpers.EmitReference(contracts) })
            : contracts;
    }

    private static Dictionary<string, string> Options() => new()
    {
        ["build_property.LakonaRpcGenerateClient"] = "true",
        ["build_property.LakonaRpcGenerateServer"] = "true",
        ["build_property.LakonaRpcGeneratedNamespace"] = "Client.Generated",
        ["build_property.LakonaRpcServerGeneratedNamespace"] = "Server.Generated"
    };

    private static string WrapContracts(string contracts, int depth)
    {
        for (var i = 0; i < depth; i++)
            contracts = $"public class Container{i} {{ {contracts} }}";
        return "using Lakona.Rpc.Core; using System.Threading.Tasks; namespace Discovery.Contracts { public sealed class Request { } public sealed class Reply { } "
            + contracts + " }";
    }

    private const string Contracts = """
        [RpcService(77, NotificationContract = typeof(IEchoNotifications))]
        public interface IEcho
        {
            [RpcMethod(1)]
            ValueTask<Reply> EchoAsync(Request request);
        }

        [RpcNotificationContract]
        public interface IEchoNotifications
        {
            [RpcNotification(1)]
            ValueTask OnEchoAsync(Reply message);
        }
        """;
}
