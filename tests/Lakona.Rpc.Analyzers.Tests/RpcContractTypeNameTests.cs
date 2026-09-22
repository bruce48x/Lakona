using Xunit;

namespace Lakona.Rpc.Analyzers.Tests;

public sealed class RpcContractTypeNameTests
{
    [Theory]
    [InlineData("string")]
    [InlineData("System.String")]
    [InlineData("int")]
    [InlineData("bool")]
    [InlineData("int?")]
    [InlineData("string[]")]
    [InlineData("System.Collections.Generic.List<string>")]
    [InlineData("System.Collections.Generic.Dictionary<string, Payload[]>")]
    public void Parameter_result_and_notification_types_generate_compilable_glue(string type)
    {
        var source = $$"""
            using Lakona.Rpc.Core;
            using System.Threading.Tasks;

            namespace Contracts
            {
                public sealed class Payload { }

                [RpcService(77, NotificationContract = typeof(IEchoNotifications))]
                public interface IEcho
                {
                    [RpcMethod(1)]
                    ValueTask<{{type}}> EchoAsync({{type}} value);
                }

                [RpcNotificationContract]
                public interface IEchoNotifications
                {
                    [RpcNotification(1)]
                    ValueTask OnEchoAsync({{type}} value);
                }
            }

            // Generated code must retain qualification inside generic arguments too.
            namespace Client.Generated { public sealed class Contracts { } }
            namespace Server.Generated { public sealed class Contracts { } }
            """;
        var compilation = AnalyzerTestHelpers.CreateCompilation(source);
        var result = AnalyzerTestHelpers.RunGenerator(compilation, new Dictionary<string, string>
        {
            ["build_property.LakonaRpcGenerateClient"] = "true",
            ["build_property.LakonaRpcGenerateServer"] = "true",
            ["build_property.LakonaRpcGeneratedNamespace"] = "Client.Generated",
            ["build_property.LakonaRpcServerGeneratedNamespace"] = "Server.Generated"
        }, out var output);

        Assert.Empty(result.Diagnostics);
        Assert.Empty(AnalyzerTestHelpers.ErrorDiagnostics(output));
        var sources = result.Results.Single().GeneratedSources;
        foreach (var name in new[] { "EchoClient.g.cs", "EchoBinder.g.cs",
                     "EchoNotificationsBinder.g.cs", "EchoNotificationsProxy.g.cs" })
            Assert.Single(sources, source => source.HintName == name);
        using var assembly = new MemoryStream();
        var emit = output.Emit(assembly);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
    }
}
