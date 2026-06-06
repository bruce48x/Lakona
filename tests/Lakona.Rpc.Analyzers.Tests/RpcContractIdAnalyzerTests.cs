using Xunit;

namespace Lakona.Rpc.Analyzers.Tests;

public sealed class RpcContractIdAnalyzerTests
{
    [Fact]
    public async Task ContractIdAnalyzer_ReportsInvalidAndDuplicateIdsInTheirOwnScopes()
    {
        var compilation = AnalyzerTestHelpers.CreateCompilation(
            """
            using Lakona.Rpc.Core;

            namespace Analyzer.Contracts
            {
                public sealed class Request { }
                public sealed class Reply { }

                [RpcService(1, NotificationContract = typeof(IFirstNotifications))]
                public interface IFirstService
                {
                    [RpcMethod(1)]
                    System.Threading.Tasks.ValueTask<Reply> OneAsync(Request request);

                    [RpcMethod(1)]
                    System.Threading.Tasks.ValueTask<Reply> DuplicateAsync(Request request);
                }

                [RpcService(1)]
                public interface ISecondService
                {
                    [RpcMethod(2)]
                    System.Threading.Tasks.ValueTask<Reply> TwoAsync(Request request);
                }

                [RpcService(0)]
                public interface IInvalidService
                {
                    [RpcMethod(1)]
                    System.Threading.Tasks.ValueTask<Reply> InvalidAsync(Request request);
                }

                [RpcNotificationContract(typeof(IFirstService))]
                public interface IFirstNotifications
                {
                    [RpcNotification(0)]
                    void InvalidNotification(Request request);

                    [RpcNotification(2)]
                    void NotificationOne(Request request);

                    [RpcNotification(2)]
                    void NotificationDuplicate(Request request);
                }
            }
            """);

        var diagnostics = await AnalyzerTestHelpers.RunContractIdAnalyzerAsync(compilation);
        var ids = diagnostics.Select(static diagnostic => diagnostic.Id).OrderBy(static id => id).ToArray();

        Assert.Contains(RpcContractIdAnalyzer.InvalidServiceIdDiagnosticId, ids);
        Assert.Contains(RpcContractIdAnalyzer.DuplicateServiceIdDiagnosticId, ids);
        Assert.Contains(RpcContractIdAnalyzer.DuplicateMethodIdDiagnosticId, ids);
        Assert.Contains(RpcContractIdAnalyzer.InvalidNotificationIdDiagnosticId, ids);
        Assert.Contains(RpcContractIdAnalyzer.DuplicateNotificationIdDiagnosticId, ids);

        Assert.Equal(2, diagnostics.Count(static diagnostic => diagnostic.Id == RpcContractIdAnalyzer.DuplicateServiceIdDiagnosticId));
        Assert.Equal(2, diagnostics.Count(static diagnostic => diagnostic.Id == RpcContractIdAnalyzer.DuplicateMethodIdDiagnosticId));
        Assert.Equal(2, diagnostics.Count(static diagnostic => diagnostic.Id == RpcContractIdAnalyzer.DuplicateNotificationIdDiagnosticId));
    }
}
