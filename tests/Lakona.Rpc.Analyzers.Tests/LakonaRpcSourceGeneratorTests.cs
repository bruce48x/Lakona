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
    public void SourceGenerator_UnityAssemblyRequiresClientGenerationMarker()
    {
        var unmarkedCompilation = AnalyzerTestHelpers.CreateCompilation(
            SimpleClientContractSource,
            assemblyName: "Assembly-CSharp");
        var unmarkedRun = AnalyzerTestHelpers.RunGenerator(unmarkedCompilation, null, out _);

        Assert.Empty(unmarkedRun.Results.Single().GeneratedSources);

        var markedCompilation = AnalyzerTestHelpers.CreateCompilation(
            """
            using Lakona.Rpc.Core;

            [assembly: LakonaRpcGenerateClient("Unity.Generated")]

            namespace UnityContracts
            {
                public sealed class PingRequest { }
                public sealed class PingReply { }

                [RpcService(7)]
                public interface IPingService
                {
                    [RpcMethod(1)]
                    System.Threading.Tasks.ValueTask<PingReply> PingAsync(PingRequest request);
                }
            }
            """,
            assemblyName: "Assembly-CSharp");
        var markedRun = AnalyzerTestHelpers.RunGenerator(markedCompilation, null, out var markedOutput);

        Assert.Empty(markedRun.Diagnostics);
        Assert.Empty(AnalyzerTestHelpers.ErrorDiagnostics(markedOutput));

        var generatedSource = string.Join(
            "\n",
            markedRun.Results.Single().GeneratedSources.Select(static source => source.SourceText.ToString()));
        Assert.Contains("namespace Unity.Generated", generatedSource);
        Assert.Contains("PingServiceClient", generatedSource);
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
                ["build_property.LakonaGameClientRuntime"] = "unity",
                ["build_property.LakonaGameClientPlatform"] = "unity",
                ["build_property.LakonaGameClientGameVersion"] = "agar"
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
                ["build_property.LakonaRpcGeneratedNamespace"] = "Rpc.Generated",
                ["build_property.LakonaGameClientRuntime"] = "unity",
                ["build_property.LakonaGameClientPlatform"] = "unity",
                ["build_property.LakonaGameClientGameVersion"] = "agar"
            },
            out var outputCompilation);

        Assert.Empty(runResult.Diagnostics);
        Assert.Empty(AnalyzerTestHelpers.ErrorDiagnostics(outputCompilation));

        var wrapper = GetGeneratedSource(runResult, "LakonaGameClient.g.cs");
        Assert.Contains("public sealed class LakonaGameClient : IAsyncDisposable", wrapper);
        Assert.Contains("public global::Rpc.Generated.RpcApi Api", wrapper);
        Assert.Contains("LakonaGameClient is not connected. Call ConnectAsync first.", wrapper);
        Assert.Contains("LakonaGameClient is single-use and has already started connecting.", wrapper);
        Assert.Contains("public event Action<Exception?>? Disconnected", wrapper);
        Assert.Contains("if (receiver is global::Game.Contracts.IPingNotifications pingNotifications)", wrapper);
        Assert.Contains("bindings.Add(pingNotifications);", wrapper);
        Assert.Contains("ClientRuntime = ResolveOption(_options.ClientRuntime, \"unity\")", wrapper);
        Assert.Contains("Platform = ResolveOption(_options.Platform, \"unity\")", wrapper);
        Assert.Contains("GameVersion = ResolveOption(_options.GameVersion, \"agar\")", wrapper);
        Assert.DoesNotContain("System.Reflection", wrapper);
        AssertInOrder(wrapper, "_core.MarkConnecting();", "await _rpcClient.ConnectAsync");
        AssertInOrder(wrapper, "await _rpcClient.ConnectAsync", "await _core.HandshakeAsync");
        AssertInOrder(wrapper, "await _core.HandshakeAsync", "_core.StartHeartbeat");
        AssertInOrder(wrapper, "_core.StartHeartbeat", "_core.MarkReady();");
        AssertInOrder(wrapper, "_core.MarkReady();", "_apiReady = true;");
        AssertInOrder(
            wrapper.Substring(wrapper.IndexOf("private void HandleDisconnected", StringComparison.Ordinal)),
            "_apiReady = false;",
            "_core.MarkReconnecting();");
    }

    [Fact]
    public void SourceGenerator_GameClientWrapper_CompilesWithoutNotificationContracts()
    {
        var runResult = AnalyzerTestHelpers.RunGenerator(
            AnalyzerTestHelpers.CreateCompilation(SimpleClientContractSource),
            new Dictionary<string, string>
            {
                ["build_property.LakonaGameGenerateClient"] = "true"
            },
            out var outputCompilation);

        Assert.Empty(runResult.Diagnostics);
        Assert.Empty(AnalyzerTestHelpers.ErrorDiagnostics(outputCompilation));

        var wrapper = GetGeneratedSource(runResult, "LakonaGameClient.g.cs");
        Assert.Contains("new global::Rpc.Generated.RpcClient(_options.RpcOptions)", wrapper);
        Assert.Contains("ValidateCallbackReceivers(callbackReceivers);", wrapper);
        Assert.Contains("throw new ArgumentNullException(nameof(callbackReceivers), \"Callback receiver cannot be null.\");", wrapper);
        Assert.DoesNotContain("RpcNotificationBindings", wrapper);
    }

    [Fact]
    public void SourceGenerator_GameClientWrapper_GameVersionFallback_UsesAssemblyNameOrGame()
    {
        var assemblyRun = AnalyzerTestHelpers.RunGenerator(
            AnalyzerTestHelpers.CreateCompilation(SimpleClientContractSource, assemblyName: "SpaceGame"),
            new Dictionary<string, string>
            {
                ["build_property.LakonaGameGenerateClient"] = "true"
            },
            out var assemblyOutput);

        Assert.Empty(assemblyRun.Diagnostics);
        Assert.Empty(AnalyzerTestHelpers.ErrorDiagnostics(assemblyOutput));

        var assemblyWrapper = GetGeneratedSource(assemblyRun, "LakonaGameClient.g.cs");
        Assert.Contains("GameVersion = ResolveOption(_options.GameVersion, \"SpaceGame\")", assemblyWrapper);

        var nullAssemblyRun = AnalyzerTestHelpers.RunGenerator(
            AnalyzerTestHelpers.CreateCompilation(SimpleClientContractSource, assemblyName: null),
            new Dictionary<string, string>
            {
                ["build_property.LakonaGameGenerateClient"] = "true"
            },
            out var nullAssemblyOutput);

        Assert.Empty(nullAssemblyRun.Diagnostics);
        Assert.Empty(AnalyzerTestHelpers.ErrorDiagnostics(nullAssemblyOutput));

        var nullAssemblyWrapper = GetGeneratedSource(nullAssemblyRun, "LakonaGameClient.g.cs");
        Assert.Contains("GameVersion = ResolveOption(_options.GameVersion, \"game\")", nullAssemblyWrapper);
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
    public void SourceGenerator_GameClientWrapper_UnityMarker_EnablesWrapperAndMetadata()
    {
        var source = """
            using System.Threading.Tasks;
            using Lakona.Rpc.Core;

            [assembly: LakonaRpcGenerateClient("Rpc.Generated")]
            [assembly: LakonaGameGenerateClient("unity", "unity", "agar")]

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
        Assert.Contains("ClientRuntime = ResolveOption(_options.ClientRuntime, \"unity\")", wrapper);
        Assert.Contains("Platform = ResolveOption(_options.Platform, \"unity\")", wrapper);
        Assert.Contains("GameVersion = ResolveOption(_options.GameVersion, \"agar\")", wrapper);
    }

    [Fact]
    public void SourceGenerator_GameClientWrapper_PropertyPrecedence_OverridesMarker()
    {
        var source = """
            using System.Threading.Tasks;
            using Lakona.Rpc.Core;

            [assembly: LakonaRpcGenerateClient("Rpc.Generated")]
            [assembly: LakonaGameGenerateClient("unity", "unity", "agar")]

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

        var overrideRun = AnalyzerTestHelpers.RunGenerator(
            AnalyzerTestHelpers.CreateCompilation(source, assemblyName: "Assembly-CSharp"),
            new Dictionary<string, string>
            {
                ["build_property.LakonaGameGenerateClient"] = "true",
                ["build_property.LakonaGameClientRuntime"] = "dotnet-client",
                ["build_property.LakonaGameClientPlatform"] = "windows",
                ["build_property.LakonaGameClientGameVersion"] = "local"
            },
            out var outputCompilation);

        Assert.Empty(overrideRun.Diagnostics);
        Assert.Empty(AnalyzerTestHelpers.ErrorDiagnostics(outputCompilation));

        var wrapper = GetGeneratedSource(overrideRun, "LakonaGameClient.g.cs");
        Assert.Contains("ClientRuntime = ResolveOption(_options.ClientRuntime, \"dotnet-client\")", wrapper);
        Assert.Contains("Platform = ResolveOption(_options.Platform, \"windows\")", wrapper);
        Assert.Contains("GameVersion = ResolveOption(_options.GameVersion, \"local\")", wrapper);
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
}
