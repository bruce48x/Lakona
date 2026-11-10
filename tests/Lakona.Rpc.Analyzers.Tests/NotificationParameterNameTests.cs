using System.Runtime.Loader;
using Lakona.Rpc.Server;
using Xunit;

namespace Lakona.Rpc.Analyzers.Tests;

public sealed class NotificationParameterNameTests
{
    [Theory]
    [InlineData("@class")]
    [InlineData("@await")]
    public void Shared_parameter_model_preserves_escaped_service_payload_names(string name)
    {
        var source = $$"""
            using System.Threading.Tasks;
            using Lakona.Rpc.Core;
            public sealed class Message { }
            [RpcService(42)]
            public interface IService
            {
                [RpcMethod(1)] ValueTask<Message> Echo(Message {{name}});
            }
            """;
        var result = AnalyzerTestHelpers.RunGenerator(
            AnalyzerTestHelpers.CreateCompilation(source), null, out var output);
        Assert.Empty(result.Diagnostics);
        Assert.Empty(AnalyzerTestHelpers.ErrorDiagnostics(output));
        using var image = new MemoryStream();
        var emitted = output.Emit(image);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
    }

    public static IEnumerable<object?[]> Names()
    {
        foreach (var returns in new[] { "void", "ValueTask" })
        foreach (var token in new[] { null, "cancellationToken", "ct", "cancel", "@event", "@await", "_notifications", "ServiceId" })
            yield return new object?[] { returns, token, "message" };
        foreach (var returns in new[] { "void", "ValueTask" })
        foreach (var payload in new[] { "@class", "@await", "cancellationToken", "_notifications", "ServiceId" })
            yield return new object?[] { returns, "ct", payload };
    }

    [Theory]
    [MemberData(nameof(Names))]
    public async Task Notification_parameter_names_compile_and_forward_to_transport(string returns, string? token, string payload)
    {
        var tokenParameter = token is null ? "" : $", CancellationToken {token}";
        var arguments = token is null ? "new Message()" : "new Message(), token";
        var invocation = $"new Server.Generated.NoticesProxy(channel).Notify({arguments})";
        var body = returns == "void" ? invocation + "; return default;" : "return " + invocation + ";";
        var source = $$"""
            using System.Threading;
            using System.Threading.Tasks;
            using Lakona.Rpc.Core;
            namespace Contracts
            {
                public sealed class Message { public int Value { get; set; } = 7; }
                [RpcService(42, NotificationContract = typeof(INotices))]
                public interface IService
                {
                    [RpcMethod(1)] ValueTask<Message> Echo(Message request);
                }
                [RpcNotificationContract]
                public interface INotices
                {
                    [RpcNotification(2)] {{returns}} Notify(Message {{payload}}{{tokenParameter}});
                }
                public static class Probe
                {
                    public static ValueTask Invoke(Lakona.Rpc.Server.RpcNotificationChannel channel, CancellationToken token)
                    {
                        {{body}}
                    }
                }
            }
            """;
        var result = AnalyzerTestHelpers.RunGenerator(
            AnalyzerTestHelpers.CreateCompilation(source), null, out var output);

        Assert.Empty(result.Diagnostics);
        Assert.Empty(AnalyzerTestHelpers.ErrorDiagnostics(output));
        using var image = new MemoryStream();
        var emitted = output.Emit(image);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        var context = new AssemblyLoadContext("notification-test", isCollectible: true);
        try
        {
            image.Position = 0;
            var assembly = context.LoadFromStream(image);
            var invoke = assembly.GetType("Contracts.Probe")!.GetMethod("Invoke")!
                .CreateDelegate<Func<RpcNotificationChannel, CancellationToken, ValueTask>>();
            await NotificationProxyTestHost.VerifyAsync(invoke, token is not null);
        }
        finally { context.Unload(); }
    }
}
