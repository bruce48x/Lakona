using Microsoft.CodeAnalysis;
using Xunit;

namespace Lakona.Rpc.Analyzers.Tests;

public sealed class LakonaRpcSourceGeneratorTests
{
    [Fact]
    public void SourceGenerator_ExplicitClientAndServerGeneration_ProducesCompilableGlue()
    {
        var compilation = AnalyzerTestHelpers.CreateCompilation(ContractWithCallbackSource);
        var runResult = AnalyzerTestHelpers.RunGenerator(
            compilation,
            new Dictionary<string, string>
            {
                ["build_property.LakonaRpcGenerateClient"] = "true",
                ["build_property.LakonaRpcGenerateServer"] = "true",
                ["build_property.LakonaRpcGeneratedNamespace"] = "Client.Generated",
                ["build_property.LakonaRpcServerGeneratedNamespace"] = "Server.Generated"
            },
            out var outputCompilation);

        Assert.Empty(runResult.Diagnostics);
        Assert.Empty(AnalyzerTestHelpers.ErrorDiagnostics(outputCompilation));

        var generatedHintNames = runResult.Results
            .Single()
            .GeneratedSources
            .Select(static source => source.HintName)
            .ToArray();

        Assert.Contains("PingServiceClient.g.cs", generatedHintNames);
        Assert.Contains("PingNotificationsBinder.g.cs", generatedHintNames);
        Assert.Contains("RpcApi.g.cs", generatedHintNames);
        Assert.Contains("PingServiceBinder.g.cs", generatedHintNames);
        Assert.Contains("PingNotificationsProxy.g.cs", generatedHintNames);
        Assert.Contains("AllServicesBinder.g.cs", generatedHintNames);

        var pingServiceBinder = runResult.Results.Single().GeneratedSources.Single(static source => source.HintName == "PingServiceBinder.g.cs").SourceText.ToString();
        Assert.Contains("serviceName: \"Game.Contracts.IPingService\"", pingServiceBinder);
        Assert.Contains("methodName: \"PingAsync\"", pingServiceBinder);

        var allServicesBinder = runResult.Results.Single().GeneratedSources.Single(static source => source.HintName == "AllServicesBinder.g.cs").SourceText.ToString();
        Assert.Contains("[assembly: RpcGeneratedServicesBinder(typeof(Server.Generated.AllServicesBinder))]", allServicesBinder);

        var rpcApi = runResult.Results.Single().GeneratedSources.Single(static source => source.HintName == "RpcApi.g.cs").SourceText.ToString();
        Assert.Contains("public event Action<RpcUnhandledNotificationContext>? UnhandledNotificationReceived", rpcApi);
        Assert.Contains("public event Action<RpcNotificationHandlerExceptionContext>? NotificationHandlerException", rpcApi);
    }

    [Fact]
    public void SourceGenerator_ReferencedContractAssembly_IsDiscoveredForClientGeneration()
    {
        var contractCompilation = AnalyzerTestHelpers.CreateCompilation(ReferencedContractSource, "Contracts");
        var contractReference = AnalyzerTestHelpers.EmitReference(contractCompilation);
        var appCompilation = AnalyzerTestHelpers.CreateCompilation(
            "public sealed class App { }",
            additionalReferences: new[] { contractReference });

        var runResult = AnalyzerTestHelpers.RunGenerator(
            appCompilation,
            new Dictionary<string, string>
            {
                ["build_property.LakonaRpcGenerateClient"] = "true"
            },
            out var outputCompilation);

        Assert.Empty(runResult.Diagnostics);
        Assert.Empty(AnalyzerTestHelpers.ErrorDiagnostics(outputCompilation));

        var generatedSource = string.Join(
            "\n",
            runResult.Results.Single().GeneratedSources.Select(static source => source.SourceText.ToString()));
        Assert.Contains("Referenced.Generated", generatedSource);
        Assert.Contains("ExternalServiceClient", generatedSource);
    }

    [Fact]
    public void SourceGenerator_UnityAssemblyCSharp_AutoGeneratesClientAndGameWrapper()
    {
        var compilation = AnalyzerTestHelpers.CreateCompilation(
            SimpleClientContractSource,
            assemblyName: "Assembly-CSharp",
            includeServerRuntimeReference: false);

        var runResult = AnalyzerTestHelpers.RunGenerator(compilation, null, out var outputCompilation);

        Assert.Empty(runResult.Diagnostics);
        Assert.Empty(AnalyzerTestHelpers.ErrorDiagnostics(outputCompilation));

        var generatedHintNames = runResult.Results.Single().GeneratedSources.Select(static source => source.HintName).ToArray();
        Assert.Contains("RpcApi.g.cs", generatedHintNames);
        Assert.Contains("PingServiceClient.g.cs", generatedHintNames);
        Assert.Contains("LakonaGameClient.g.cs", generatedHintNames);
        var generatedSource = string.Join(
            "\n",
            runResult.Results.Single().GeneratedSources.Select(static source => source.SourceText.ToString()));
        Assert.Contains("namespace Client.Generated", generatedSource);
    }

    [Fact]
    public void SourceGenerator_UnityCustomAssembly_AutoGeneratesClientAndGameWrapper()
    {
        var compilation = AnalyzerTestHelpers.CreateCompilation(
            SimpleClientContractSource,
            assemblyName: "Game.Client",
            additionalReferences: new[] { AnalyzerTestHelpers.CreateUnityEngineReference() },
            includeServerRuntimeReference: false);

        var runResult = AnalyzerTestHelpers.RunGenerator(compilation, null, out var outputCompilation);

        Assert.Empty(runResult.Diagnostics);
        Assert.Empty(AnalyzerTestHelpers.ErrorDiagnostics(outputCompilation));

        var generatedHintNames = runResult.Results.Single().GeneratedSources.Select(static source => source.HintName).ToArray();
        Assert.Contains("RpcApi.g.cs", generatedHintNames);
        Assert.Contains("PingServiceClient.g.cs", generatedHintNames);
        Assert.Contains("LakonaGameClient.g.cs", generatedHintNames);
    }

    [Fact]
    public void SourceGenerator_UnityAssemblyCSharp_ExplicitFalseDisablesGameWrapper()
    {
        var compilation = AnalyzerTestHelpers.CreateCompilation(
            SimpleClientContractSource,
            assemblyName: "Assembly-CSharp",
            includeServerRuntimeReference: false);

        var runResult = AnalyzerTestHelpers.RunGenerator(
            compilation,
            new Dictionary<string, string>
            {
                ["build_property.LakonaGameGenerateClient"] = "false"
            },
            out var outputCompilation);

        Assert.Empty(runResult.Diagnostics);
        Assert.Empty(AnalyzerTestHelpers.ErrorDiagnostics(outputCompilation));

        var generatedHintNames = runResult.Results.Single().GeneratedSources.Select(static source => source.HintName).ToArray();
        Assert.Contains("RpcApi.g.cs", generatedHintNames);
        Assert.Contains("PingServiceClient.g.cs", generatedHintNames);
        Assert.DoesNotContain("LakonaGameClient.g.cs", generatedHintNames);
    }

    [Fact]
    public void SourceGenerator_NotificationPush_AllowsVoidAndValueTaskReturns()
    {
        var compilation = AnalyzerTestHelpers.CreateCompilation(ContractWithAsyncCallbackSource);
        var runResult = AnalyzerTestHelpers.RunGenerator(
            compilation,
            new Dictionary<string, string>
            {
                ["build_property.LakonaRpcGenerateClient"] = "true",
                ["build_property.LakonaRpcGenerateServer"] = "true"
            },
            out var outputCompilation);

        Assert.Empty(runResult.Diagnostics);
        Assert.Empty(AnalyzerTestHelpers.ErrorDiagnostics(outputCompilation));

        var callbackBinder = runResult.Results
            .Single()
            .GeneratedSources
            .Single(static source => source.HintName == "PingNotificationsBinder.g.cs")
            .SourceText
            .ToString();

        Assert.Contains("receiver.OnNotify(arg);", callbackBinder);
        Assert.Contains("return receiver.OnNotifyAsync(arg);", callbackBinder);

        var proxy = runResult.Results
            .Single()
            .GeneratedSources
            .Single(static source => source.HintName == "PingNotificationsProxy.g.cs")
            .SourceText
            .ToString();

        Assert.Contains("IRpcNotificationDispatchTarget", proxy);
        Assert.Contains("ValueTask IRpcNotificationDispatchTarget.DispatchNotificationAsync(", proxy);
        Assert.DoesNotContain("_ = _session.SendNotificationAsync", proxy);
    }

    [Fact]
    public void SourceGenerator_ServiceApiNames_OverrideConventionNames()
    {
        var compilation = AnalyzerTestHelpers.CreateCompilation(ContractWithExplicitApiNamesSource);
        var runResult = AnalyzerTestHelpers.RunGenerator(
            compilation,
            new Dictionary<string, string>
            {
                ["build_property.LakonaRpcGenerateClient"] = "true"
            },
            out var outputCompilation);

        Assert.Empty(runResult.Diagnostics);
        Assert.Empty(AnalyzerTestHelpers.ErrorDiagnostics(outputCompilation));

        var rpcApi = runResult.Results
            .Single()
            .GeneratedSources
            .Single(static source => source.HintName == "RpcApi.g.cs")
            .SourceText
            .ToString();

        Assert.Contains("public GameplayRpcGroup Gameplay { get; }", rpcApi);
        Assert.Contains("public global::Example.Contracts.IInventoryService Bag { get; }", rpcApi);
        Assert.DoesNotContain("public ExampleRpcGroup Example { get; }", rpcApi);
        Assert.DoesNotContain("public global::Example.Contracts.IInventoryService Inventory { get; }", rpcApi);
    }

    [Fact]
    public void SourceGenerator_DuplicateServiceApiNames_ReportDiagnostic()
    {
        var compilation = AnalyzerTestHelpers.CreateCompilation(ContractWithDuplicateApiNamesSource);
        var runResult = AnalyzerTestHelpers.RunGenerator(
            compilation,
            new Dictionary<string, string>
            {
                ["build_property.LakonaRpcGenerateClient"] = "true"
            },
            out _);

        var diagnostic = Assert.Single(runResult.Diagnostics);
        Assert.Equal("ULRPCGEN001", diagnostic.Id);
        Assert.Contains("Duplicate generated API service name 'World.Player'", diagnostic.GetMessage());
    }

    [Fact]
    public void SourceGenerator_GameClientWrapper_IsOnlyGeneratedWhenEnabled()
    {
        var rpcOnlyRun = AnalyzerTestHelpers.RunGenerator(
            AnalyzerTestHelpers.CreateCompilation(ContractWithCallbackSource),
            new Dictionary<string, string>
            {
                ["build_property.LakonaRpcGenerateClient"] = "true"
            },
            out _);

        Assert.DoesNotContain(
            rpcOnlyRun.Results.Single().GeneratedSources,
            static source => source.HintName == "LakonaGameClient.g.cs");

        var gameRun = AnalyzerTestHelpers.RunGenerator(
            AnalyzerTestHelpers.CreateCompilation(ContractWithCallbackSource),
            new Dictionary<string, string>
            {
                ["build_property.LakonaGameGenerateClient"] = "true",
                ["build_property.LakonaRpcGeneratedNamespace"] = "Client.Generated"
            },
            out var outputCompilation);

        Assert.Empty(gameRun.Diagnostics);
        Assert.Empty(AnalyzerTestHelpers.ErrorDiagnostics(outputCompilation));
        Assert.Contains(
            gameRun.Results.Single().GeneratedSources,
            static source => source.HintName == "LakonaGameClient.g.cs");
    }

    [Fact]
    public void SourceGenerator_GameClientWrapper_ForwardsApiAndAutoBindsCallbacks()
    {
        var runResult = AnalyzerTestHelpers.RunGenerator(
            AnalyzerTestHelpers.CreateCompilation(ContractWithCallbackSource),
            new Dictionary<string, string>
            {
                ["build_property.LakonaGameGenerateClient"] = "true",
                ["build_property.LakonaRpcGeneratedNamespace"] = "Client.Generated"
            },
            out var outputCompilation);

        Assert.Empty(runResult.Diagnostics);
        Assert.Empty(AnalyzerTestHelpers.ErrorDiagnostics(outputCompilation));

        var wrapper = GetGeneratedSource(runResult, "LakonaGameClient.g.cs");
        Assert.Contains("public sealed class LakonaGameClient : IAsyncDisposable", wrapper);
        Assert.Contains("public global::Client.Generated.RpcApi Api", wrapper);
        Assert.Contains("new global::Client.Generated.RpcClient(_options.CreateConnectionGeneration()", wrapper);
        Assert.DoesNotContain("LakonaGameClient(RpcClientOptions", wrapper, StringComparison.Ordinal);
        Assert.DoesNotContain("_options.RpcOptions", wrapper, StringComparison.Ordinal);
        Assert.Contains("public ValueTask StartSessionAsync(string sessionId, long sessionGeneration, CancellationToken cancellationToken = default)", wrapper);
        Assert.Contains("return _core.StartSessionAsync(sessionId, sessionGeneration, cancellationToken);", wrapper);
        Assert.Contains("LakonaGameClient is not connected. Call ConnectAsync first.", wrapper);
        Assert.Contains("LakonaGameClient is single-use and has already started connecting.", wrapper);
        Assert.Contains("public event Action<Exception?>? Disconnected", wrapper);
        Assert.Contains("if (receiver is global::Game.Contracts.IPingNotifications pingNotifications)", wrapper);
        Assert.Contains("bindings.Add(pingNotifications);", wrapper);
        Assert.Contains("ProtocolVersion = 1", wrapper);
        Assert.Contains("ResumeTicket = _core.ResumeTicket", wrapper);
        Assert.Contains("GameSessionNotificationRpcIds.EstablishedNotificationId", wrapper);
        Assert.Contains("ApplyGameSessionEstablishedAsync", wrapper);
        Assert.DoesNotContain("ClientRuntime", wrapper);
        Assert.DoesNotContain("Platform", wrapper);
        Assert.DoesNotContain("GameVersion", wrapper);
        Assert.Contains("using Lakona.Game.Abstractions;", wrapper);
        Assert.Contains("using Lakona.Game.Abstractions.Sessions;", wrapper);
        Assert.Contains("client.Runtime.RegisterRawNotificationHandler(", wrapper);
        Assert.Contains("GameSessionNotificationRpcIds.ServiceId", wrapper);
        Assert.Contains("GameSessionNotificationRpcIds.TerminatedNotificationId", wrapper);
        Assert.Contains("LakonaInternalCodec.DecodeSessionTerminationNotice(payload)", wrapper);
        Assert.Contains("_core.BindReliablePush(client.Runtime);", wrapper);
        Assert.Contains("_core.ReplaceHeartbeatAsync(client.Runtime)", wrapper, StringComparison.Ordinal);
        Assert.Contains("_core.MarkRecovered();", wrapper, StringComparison.Ordinal);
        Assert.Contains("private Task? _recoveryTask;", wrapper, StringComparison.Ordinal);
        Assert.Contains("await recoveryTask.ConfigureAwait(false);", wrapper, StringComparison.Ordinal);
        Assert.DoesNotContain("_core.StartHeartbeat(_rpcClient.Runtime, _options);", wrapper, StringComparison.Ordinal);
        Assert.DoesNotContain("HeartbeatEnabled", wrapper, StringComparison.Ordinal);
        Assert.DoesNotContain("HeartbeatInterval", wrapper, StringComparison.Ordinal);
        Assert.DoesNotContain("HeartbeatTimeout", wrapper, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Reflection", wrapper);
        AssertInOrder(wrapper, "_core.MarkConnecting();", "ConnectGenerationAsync(false");
        AssertInOrder(wrapper, "await client.ConnectAsync", "await _core.HandshakeAsync");
        AssertInOrder(wrapper, "await _core.HandshakeAsync", "_core.BindReliablePush");
        AssertInOrder(wrapper, "_core.BindReliablePush", "client.Runtime.RegisterRawNotificationHandler");
        AssertInOrder(wrapper, "client.Runtime.RegisterRawNotificationHandler", "_core.ReplaceHeartbeatAsync");
        AssertInOrder(wrapper, "_core.ReplaceHeartbeatAsync", "_core.MarkReady();");
        AssertInOrder(wrapper, "_core.MarkReady();", "_apiReady = true;");
        AssertInOrder(
            wrapper.Substring(wrapper.IndexOf("private void HandleDisconnected", StringComparison.Ordinal)),
            "_apiReady = false;",
            "_core.MarkReconnecting();");
    }

    [Fact]
    public void SourceGenerator_FrameworkSessionCallbackProxy_UsesInternalCodecForTerminationNotice()
    {
        var runResult = AnalyzerTestHelpers.RunGenerator(
            AnalyzerTestHelpers.CreateCompilation(FrameworkSessionCallbackContractSource),
            new Dictionary<string, string>
            {
                ["build_property.LakonaRpcGenerateServer"] = "true",
                ["build_property.LakonaRpcServerGeneratedNamespace"] = "Server.Generated"
            },
            out var outputCompilation);

        Assert.Empty(runResult.Diagnostics);
        Assert.Empty(AnalyzerTestHelpers.ErrorDiagnostics(outputCompilation));

        var proxy = GetGeneratedSource(runResult, "LakonaGameSessionCallbackProxy.g.cs");
        Assert.Contains("using Lakona.Game.Abstractions;", proxy);
        Assert.Contains("using Lakona.Game.Abstractions.Sessions;", proxy);
        Assert.Contains(
            "var payload = LakonaInternalCodec.EncodeSessionTerminationNotice(notice);",
            proxy);
        Assert.Contains("_session.SendRawNotificationAsync(", proxy);
        Assert.Contains("GameSessionNotificationRpcIds.ServiceId", proxy);
        Assert.Contains("GameSessionNotificationRpcIds.TerminatedNotificationId", proxy);
        Assert.DoesNotContain("SendNotificationAsync<global::Lakona.Game.Abstractions.SessionTerminationNotice>", proxy);
    }

    [Fact]
    public void SourceGenerator_FrameworkSessionCallbackProxy_DoesNotUseInternalCodecForNonExactTerminationSignature()
    {
        var runResult = AnalyzerTestHelpers.RunGenerator(
            AnalyzerTestHelpers.CreateCompilation(FrameworkSessionCallbackWithoutCancellationTokenSource),
            new Dictionary<string, string>
            {
                ["build_property.LakonaRpcGenerateServer"] = "true",
                ["build_property.LakonaRpcServerGeneratedNamespace"] = "Server.Generated"
            },
            out var outputCompilation);

        Assert.Empty(runResult.Diagnostics);
        Assert.Empty(AnalyzerTestHelpers.ErrorDiagnostics(outputCompilation));

        var proxy = GetGeneratedSource(runResult, "LakonaGameSessionCallbackProxy.g.cs");
        Assert.Contains(
            "return _session.SendNotificationAsync<global::Lakona.Game.Abstractions.SessionTerminationNotice>(ServiceId, 9, notice);",
            proxy);
        Assert.DoesNotContain("SendRawNotificationAsync", proxy);
        Assert.DoesNotContain("LakonaInternalCodec", proxy);
        Assert.DoesNotContain("GameSessionNotificationRpcIds", proxy);
    }

    [Fact]
    public void SourceGenerator_FrameworkSessionCallbackProxy_DoesNotUseInternalCodecForRequiredCancellationToken()
    {
        var runResult = AnalyzerTestHelpers.RunGenerator(
            AnalyzerTestHelpers.CreateCompilation(FrameworkSessionCallbackRequiredCancellationTokenSource),
            new Dictionary<string, string>
            {
                ["build_property.LakonaRpcGenerateServer"] = "true",
                ["build_property.LakonaRpcServerGeneratedNamespace"] = "Server.Generated"
            },
            out var outputCompilation);

        Assert.Empty(runResult.Diagnostics);
        Assert.Empty(AnalyzerTestHelpers.ErrorDiagnostics(outputCompilation));

        var proxy = GetGeneratedSource(runResult, "LakonaGameSessionCallbackProxy.g.cs");
        Assert.Contains(
            "return _session.SendNotificationAsync<global::Lakona.Game.Abstractions.SessionTerminationNotice>(ServiceId, 9, notice, cancellationToken);",
            proxy);
        Assert.DoesNotContain("SendRawNotificationAsync", proxy);
        Assert.DoesNotContain("LakonaInternalCodec", proxy);
        Assert.DoesNotContain("GameSessionNotificationRpcIds", proxy);
    }

    [Fact]
    public void SourceGenerator_GameClientWrapper_CompilesWithoutNotificationContracts()
    {
        var runResult = AnalyzerTestHelpers.RunGenerator(
            AnalyzerTestHelpers.CreateCompilation(SimpleClientContractSource),
            new Dictionary<string, string>
            {
                ["build_property.LakonaRpcGeneratedNamespace"] = "Client.Generated",
                ["build_property.LakonaGameGenerateClient"] = "true"
            },
            out var outputCompilation);

        Assert.Empty(runResult.Diagnostics);
        Assert.Empty(AnalyzerTestHelpers.ErrorDiagnostics(outputCompilation));

        var wrapper = GetGeneratedSource(runResult, "LakonaGameClient.g.cs");
        Assert.Contains("new global::Client.Generated.RpcClient(_options.CreateConnectionGeneration())", wrapper);
        Assert.DoesNotContain("LakonaGameClient(RpcClientOptions", wrapper, StringComparison.Ordinal);
        Assert.DoesNotContain("_options.RpcOptions", wrapper, StringComparison.Ordinal);
        Assert.Contains("ProtocolVersion = 1", wrapper);
        Assert.DoesNotContain("ClientRuntime", wrapper);
        Assert.DoesNotContain("Platform", wrapper);
        Assert.DoesNotContain("GameVersion", wrapper);
        Assert.Contains("ValidateCallbackReceivers(callbackReceivers);", wrapper);
        Assert.Contains("throw new ArgumentNullException(nameof(callbackReceivers), \"Callback receiver cannot be null.\");", wrapper);
        Assert.DoesNotContain("RpcNotificationBindings", wrapper);
    }

    [Fact]
    public void SourceGenerator_GameClientWrapper_DisabledGameClient_DoesNotSuppressClientAutoDetection()
    {
        var compilation = AnalyzerTestHelpers.CreateCompilation(
            SimpleClientContractSource,
            includeServerRuntimeReference: false);
        Assert.NotNull(compilation.GetTypeByMetadataName("Lakona.Rpc.Client.RpcClientRuntime"));
        Assert.Null(compilation.GetTypeByMetadataName("Lakona.Rpc.Server.RpcServiceRegistry"));

        var runResult = AnalyzerTestHelpers.RunGenerator(
            compilation,
            new Dictionary<string, string>
            {
                ["build_property.LakonaGameGenerateClient"] = "false"
            },
            out var outputCompilation);

        Assert.Empty(runResult.Diagnostics);
        Assert.Empty(AnalyzerTestHelpers.ErrorDiagnostics(outputCompilation));

        var generatedHintNames = runResult.Results.Single().GeneratedSources.Select(static source => source.HintName).ToArray();
        Assert.Contains("RpcApi.g.cs", generatedHintNames);
        Assert.Contains("PingServiceClient.g.cs", generatedHintNames);
        Assert.DoesNotContain("LakonaGameClient.g.cs", generatedHintNames);
    }

    [Fact]
    public void SourceGenerator_GameClientWrapper_UnityMarker_EnablesWrapperWithoutMetadata()
    {
        var source = """
            using System.Threading.Tasks;
            using Lakona.Rpc.Core;

            [assembly: LakonaRpcGenerateClient("Client.Generated")]
            [assembly: LakonaGameGenerateClient]

            namespace Game.Contracts
            {
                public sealed class PingRequest { }
                public sealed class PingReply { }

                [RpcService(1)]
                public interface IPingService
                {
                    [RpcMethod(1)]
                    ValueTask<PingReply> PingAsync(PingRequest request);
                }
            }
            """;

        var runResult = AnalyzerTestHelpers.RunGenerator(
            AnalyzerTestHelpers.CreateCompilation(source, assemblyName: "Assembly-CSharp"),
            null,
            out var outputCompilation);

        Assert.Empty(runResult.Diagnostics);
        Assert.Empty(AnalyzerTestHelpers.ErrorDiagnostics(outputCompilation));

        var wrapper = GetGeneratedSource(runResult, "LakonaGameClient.g.cs");
        Assert.Contains("ProtocolVersion = 1", wrapper);
        Assert.DoesNotContain("ClientRuntime", wrapper);
        Assert.DoesNotContain("Platform", wrapper);
        Assert.DoesNotContain("GameVersion", wrapper);
    }

    [Fact]
    public void SourceGenerator_GameClientWrapper_PropertyPrecedence_CanDisableMarker()
    {
        var source = """
            using System.Threading.Tasks;
            using Lakona.Rpc.Core;

            [assembly: LakonaRpcGenerateClient("Client.Generated")]
            [assembly: LakonaGameGenerateClient]

            namespace Game.Contracts
            {
                public sealed class PingRequest { }
                public sealed class PingReply { }

                [RpcService(1)]
                public interface IPingService
                {
                    [RpcMethod(1)]
                    ValueTask<PingReply> PingAsync(PingRequest request);
                }
            }
            """;

        var disabledRun = AnalyzerTestHelpers.RunGenerator(
            AnalyzerTestHelpers.CreateCompilation(source, assemblyName: "Assembly-CSharp"),
            new Dictionary<string, string>
            {
                ["build_property.LakonaGameGenerateClient"] = "false"
            },
            out _);

        Assert.DoesNotContain(
            disabledRun.Results.Single().GeneratedSources,
            static source => source.HintName == "LakonaGameClient.g.cs");
    }

    private static string GetGeneratedSource(GeneratorDriverRunResult runResult, string hintName) =>
        runResult.Results
            .Single()
            .GeneratedSources
            .Single(source => source.HintName == hintName)
            .SourceText
            .ToString();

    private static void AssertInOrder(string source, string before, string after)
    {
        var beforeIndex = source.IndexOf(before, StringComparison.Ordinal);
        var afterIndex = source.IndexOf(after, StringComparison.Ordinal);
        Assert.True(beforeIndex >= 0, $"Expected to find '{before}'.");
        Assert.True(afterIndex >= 0, $"Expected to find '{after}'.");
        Assert.True(beforeIndex < afterIndex, $"Expected '{before}' before '{after}'.");
    }

    private const string ContractWithCallbackSource = """
        using System.Threading.Tasks;
        using Lakona.Rpc.Core;

        namespace Game.Contracts
        {
            public sealed class PingRequest
            {
                public string Message { get; set; } = string.Empty;
            }

            public sealed class PingReply
            {
                public string Message { get; set; } = string.Empty;
            }

            public sealed class NotifyRequest
            {
                public string Message { get; set; } = string.Empty;
            }

            [RpcService(1, NotificationContract = typeof(IPingNotifications))]
            public interface IPingService
            {
                [RpcMethod(1)]
                ValueTask<PingReply> PingAsync(PingRequest request);
            }

            [RpcNotificationContract(typeof(IPingService))]
            public interface IPingNotifications
            {
                [RpcNotification(1)]
                void OnNotify(NotifyRequest request);
            }
        }
        """;

    private const string ReferencedContractSource = """
        using System.Threading.Tasks;
        using Lakona.Rpc.Core;

        namespace Referenced.Generated
        {
            public sealed class ExternalRequest { }
            public sealed class ExternalReply { }

            [RpcService(23)]
            public interface IExternalService
            {
                [RpcMethod(1)]
                ValueTask<ExternalReply> CallAsync(ExternalRequest request);
            }
        }
        """;

    private const string SimpleClientContractSource = """
        using System.Threading.Tasks;
        using Lakona.Rpc.Core;

        namespace UnityContracts
        {
            public sealed class PingRequest { }
            public sealed class PingReply { }

            [RpcService(7)]
            public interface IPingService
            {
                [RpcMethod(1)]
                ValueTask<PingReply> PingAsync(PingRequest request);
            }
        }
        """;

    private const string ContractWithAsyncCallbackSource = """
        using System.Threading.Tasks;
        using Lakona.Rpc.Core;

        namespace Game.Contracts
        {
            public sealed class PingRequest { }
            public sealed class PingReply { }
            public sealed class NotifyRequest { }

            [RpcService(1, NotificationContract = typeof(IPingNotifications))]
            public interface IPingService
            {
                [RpcMethod(1)]
                ValueTask<PingReply> PingAsync(PingRequest request);
            }

            [RpcNotificationContract(typeof(IPingService))]
            public interface IPingNotifications
            {
                [RpcNotification(1)]
                void OnNotify(NotifyRequest request);

                [RpcNotification(2)]
                ValueTask OnNotifyAsync(NotifyRequest request);
            }
        }
        """;

    private const string ContractWithExplicitApiNamesSource = """
        using System.Threading.Tasks;
        using Lakona.Rpc.Core;

        namespace Example.Contracts
        {
            public sealed class InventoryRequest { }
            public sealed class InventoryReply { }

            [RpcService(1, ApiGroup = "Gameplay", ApiName = "Bag")]
            public interface IInventoryService
            {
                [RpcMethod(1)]
                ValueTask<InventoryReply> GetAsync(InventoryRequest request);
            }
        }
        """;

    private const string ContractWithDuplicateApiNamesSource = """
        using System.Threading.Tasks;
        using Lakona.Rpc.Core;

        namespace Example.Contracts
        {
            public sealed class PlayerRequest { }
            public sealed class PlayerReply { }

            [RpcService(1, ApiGroup = "World", ApiName = "Player")]
            public interface IPlayerService
            {
                [RpcMethod(1)]
                ValueTask<PlayerReply> GetAsync(PlayerRequest request);
            }

            [RpcService(2, ApiGroup = "World", ApiName = "Player")]
            public interface IAvatarService
            {
                [RpcMethod(1)]
                ValueTask<PlayerReply> GetAsync(PlayerRequest request);
            }
        }
        """;

    private const string FrameworkSessionCallbackContractSource = """
        using System.Threading;
        using System.Threading.Tasks;
        using Lakona.Rpc.Core;

        namespace Lakona.Game.Abstractions
        {
            public sealed class FrameworkRequest { }
            public sealed class FrameworkReply { }

            [RpcService(5, NotificationContract = typeof(ILakonaGameSessionCallback))]
            public interface IFrameworkSessionService
            {
                [RpcMethod(1)]
                ValueTask<FrameworkReply> PingAsync(FrameworkRequest request);
            }

            [RpcNotificationContract(typeof(IFrameworkSessionService))]
            public interface ILakonaGameSessionCallback
            {
                [RpcNotification(9)]
                ValueTask OnSessionTerminatedAsync(
                    SessionTerminationNotice notice,
                    CancellationToken cancellationToken = default);
            }
        }
        """;

    private const string FrameworkSessionCallbackWithoutCancellationTokenSource = """
        using System.Threading.Tasks;
        using Lakona.Rpc.Core;

        namespace Lakona.Game.Abstractions
        {
            public sealed class FrameworkRequest { }
            public sealed class FrameworkReply { }

            [RpcService(5, NotificationContract = typeof(ILakonaGameSessionCallback))]
            public interface IFrameworkSessionService
            {
                [RpcMethod(1)]
                ValueTask<FrameworkReply> PingAsync(FrameworkRequest request);
            }

            [RpcNotificationContract(typeof(IFrameworkSessionService))]
            public interface ILakonaGameSessionCallback
            {
                [RpcNotification(9)]
                ValueTask OnSessionTerminatedAsync(SessionTerminationNotice notice);
            }
        }
        """;

    private const string FrameworkSessionCallbackRequiredCancellationTokenSource = """
        using System.Threading;
        using System.Threading.Tasks;
        using Lakona.Rpc.Core;

        namespace Lakona.Game.Abstractions
        {
            public sealed class FrameworkRequest { }
            public sealed class FrameworkReply { }

            [RpcService(5, NotificationContract = typeof(ILakonaGameSessionCallback))]
            public interface IFrameworkSessionService
            {
                [RpcMethod(1)]
                ValueTask<FrameworkReply> PingAsync(FrameworkRequest request);
            }

            [RpcNotificationContract(typeof(IFrameworkSessionService))]
            public interface ILakonaGameSessionCallback
            {
                [RpcNotification(9)]
                ValueTask OnSessionTerminatedAsync(
                    SessionTerminationNotice notice,
                    CancellationToken cancellationToken);
            }
        }
        """;
}
