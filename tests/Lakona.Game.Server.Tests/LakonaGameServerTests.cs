using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Lakona.Game.Abstractions;
using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Hosting;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.Loading;
using Lakona.Game.Server.ReliablePush;
using Lakona.Game.Server.Sessions;
using Lakona.Rpc.Serializer.Json;
using Lakona.Rpc.Server;
using Xunit;

namespace Lakona.Game.Server.Tests;

public sealed class LakonaGameServerTests
{
    [Fact]
    public void AddServices_CanUseHostConfiguration()
    {
        var hostBuilder = Host.CreateApplicationBuilder([]);
        hostBuilder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Marker"] = "configured"
        });
        var serverBuilder = new LakonaGameServerBuilder(hostBuilder);

        serverBuilder.AddServices((services, configuration) =>
            services.AddSingleton(new ConfiguredValue(configuration["Marker"] ?? "")));
        serverBuilder.ApplyToHostBuilder();

        using var provider = hostBuilder.Services.BuildServiceProvider();
        var value = provider.GetRequiredService<ConfiguredValue>();

        Assert.Equal("configured", value.Value);
    }

    [Fact]
    public void ClusterEndpointConfigurationRegistersClusterRpcServer()
    {
        var services = new ServiceCollection();
        var runtime = new LakonaGameRuntimeOptions
        {
            Node = new LakonaGameNodeOptions { Id = "data-1" },
            Cluster = new LakonaGameClusterOptions
            {
                Endpoint = "tcp://127.0.0.1:21001"
            }
        };
        services.AddSingleton(runtime);
        services.AddSingleton(runtime.ToClusterOptions());
        services.AddSingleton<INodeDirectory, InMemoryNodeDirectory>();

        services.AddLakonaGameClusterEndpoint();

        var configurator = Assert.Single(services, service =>
            service.ServiceType == typeof(IRpcServerConfigurator));
        var instance = Assert.IsType<LakonaClusterRpcServerConfigurator>(
            configurator.ImplementationInstance);
        Assert.Equal("cluster", instance.Transport);
    }

    [Fact]
    public async Task ClusterEndpointRpcServerAcceptsFeatureMessageTransport()
    {
        var port = GetFreePort();
        var runtime = new LakonaGameRuntimeOptions
        {
            Node = new LakonaGameNodeOptions { Id = "data-1" },
            Cluster = new LakonaGameClusterOptions
            {
                Endpoint = $"tcp://127.0.0.1:{port}"
            }
        };
        var handler = new RecordingFeatureMessageHandler();
        var services = new ServiceCollection();
        services.AddSingleton<IFeatureMessageHandler>(handler);
        using var provider = services.BuildServiceProvider();
        var rpcBuilder = RpcServerHostBuilder.Create();
        var configurator = new LakonaClusterRpcServerConfigurator(runtime);
        configurator.Configure(new LakonaGameServerRpcContext(
            "cluster",
            new LakonaGameEndpointOptions { Transport = "cluster" },
            rpcBuilder,
            provider,
            [],
            TestContext.Current.CancellationToken));
        using var stopServer = new CancellationTokenSource();
        var serverTask = rpcBuilder.RunAsync(stopServer.Token).AsTask();
        await Task.Delay(100, TestContext.Current.CancellationToken);

        await using var clientFactory = new ClusterClientFactory(
            new TcpClusterTransportFactory(),
            new JsonRpcSerializer());
        var transport = new RpcFeatureMessageTransport(clientFactory);
        var reply = await transport.SendAsync(
            new ClusterNodeDescriptor(
                new NodeId("data-1"),
                NodeState.Ready,
                new Dictionary<string, NodeEndpoint>(StringComparer.Ordinal)
                {
                    ["cluster"] = new NodeEndpoint($"tcp://127.0.0.1:{port}")
                },
                [new NodeFeatureDescriptor("matchmaking")]),
            new FeatureMessageRequest(
                new FeatureName("matchmaking"),
                "join",
                new byte[] { 1, 2, 3 },
                DateTimeOffset.UtcNow.AddMinutes(1),
                new NodeId("gateway-1"),
                "corr-1"),
            TestContext.Current.CancellationToken);

        stopServer.Cancel();
        await Task.WhenAny(serverTask, Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));

        Assert.Equal(ClusterSendStatus.Accepted, reply.Status);
        Assert.Equal(new byte[] { 9 }, reply.Payload.ToArray());
        var request = Assert.Single(handler.Requests);
        Assert.Equal("matchmaking", request.Feature.Value);
        Assert.Equal("join", request.Kind);
        Assert.Equal(new byte[] { 1, 2, 3 }, request.Payload.ToArray());
        Assert.Equal(new NodeId("gateway-1"), request.SourceNode);
    }

    [Fact]
    public async Task InitialHotfixLoad_Throws_WhenReloadFails()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHotfixManager>(new FailingHotfixManager());
        await using var provider = services.BuildServiceProvider();

        var hotfix = provider.GetRequiredService<IHotfixManager>();
        var result = await hotfix.ReloadAsync(TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal(HotfixReloadStatus.Failed, result.Status);
        Assert.Contains("Server.Hotfix.dll", result.RequestedPath, StringComparison.Ordinal);
    }

    [Fact]
    public void Feature_discovery_does_not_load_hotfix_directory_assemblies()
    {
        var before = AssemblyLoadContext.Default.Assemblies
            .Select(assembly => assembly.GetName().Name)
            .ToHashSet(StringComparer.Ordinal);

        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        Lakona.Game.Server.Hosting.LakonaGameServer.DiscoverStableFeaturesForTesting(services, configuration, AppContext.BaseDirectory);

        var after = AssemblyLoadContext.Default.Assemblies
            .Select(assembly => assembly.GetName().Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain("Server.Hotfix", after.Except(before, StringComparer.Ordinal));
    }

    [Fact]
    public void Feature_discovery_does_not_load_existing_hotfix_directory_assemblies()
    {
        var root = Path.Combine(Path.GetTempPath(), "LakonaFeatureDiscoveryTests", Guid.NewGuid().ToString("N"));
        try
        {
            var hotfixDirectory = Path.Combine(root, "hotfix");
            Directory.CreateDirectory(hotfixDirectory);
            var hotfixPath = Path.Combine(hotfixDirectory, "Server.Hotfix.dll");

            var syntaxTree = CSharpSyntaxTree.ParseText("public sealed class Marker { }", cancellationToken: TestContext.Current.CancellationToken);
            var references = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!
                .ToString()!
                .Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path));
            var compilation = CSharpCompilation.Create(
                "Server.Hotfix",
                [syntaxTree],
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            using var stream = File.Create(hotfixPath);
            var emit = compilation.Emit(stream, cancellationToken: TestContext.Current.CancellationToken);
            Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));

            var before = AssemblyLoadContext.Default.Assemblies
                .Select(assembly => assembly.GetName().Name)
                .ToHashSet(StringComparer.Ordinal);

            var services = new ServiceCollection();
            var configuration = new ConfigurationBuilder().Build();

            Lakona.Game.Server.Hosting.LakonaGameServer.DiscoverStableFeaturesForTesting(services, configuration, root);

            var after = AssemblyLoadContext.Default.Assemblies
                .Select(assembly => assembly.GetName().Name)
                .ToHashSet(StringComparer.Ordinal);

            Assert.DoesNotContain("Server.Hotfix", after.Except(before, StringComparer.Ordinal));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void Default_hotfix_shared_assemblies_include_generated_project_boundaries()
    {
        var names = Lakona.Game.Server.Hosting.LakonaGameServer.GetDefaultHotfixSharedAssemblyNames();

        Assert.Contains("Shared", names);
        Assert.Contains("Server.App", names);
        Assert.Contains("State.Contracts", names);
    }

    [Fact]
    public async Task Default_hotfix_source_resolves_current_version_pointer()
    {
        var root = Path.Combine(Path.GetTempPath(), "LakonaDefaultHotfixSourceTests", Guid.NewGuid().ToString("N"));
        try
        {
            var hotfixRoot = Path.Combine(root, "hotfix");
            var versionRoot = Path.Combine(hotfixRoot, "versions", "v2");
            Directory.CreateDirectory(versionRoot);
            var assemblyPath = Path.Combine(versionRoot, "Server.Hotfix.dll");
            await File.WriteAllTextAsync(Path.Combine(hotfixRoot, "current.txt"), "v2", TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(assemblyPath, "dll", TestContext.Current.CancellationToken);

            var services = new ServiceCollection();
            Lakona.Game.Server.Hosting.LakonaGameServer.ConfigureDefaultHotfixForTesting(
                services,
                root,
                buildTag: "test");
            using var provider = services.BuildServiceProvider();

            var source = Assert.IsType<VersionPointerHotfixAssemblySource>(
                provider.GetRequiredService<IHotfixAssemblySource>());
            var resolved = await source.ResolveAsync(TestContext.Current.CancellationToken);

            Assert.Equal(assemblyPath, resolved.AssemblyPath);
            Assert.Equal("v2", resolved.Version);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task MainEntryStartsSessionBindsCallbackAndReturnsCallback()
    {
        var services = new ServiceCollection();
        services.AddLakonaGameServer();
        using var provider = services.BuildServiceProvider();
        var server = provider.GetRequiredService<ILakonaGameServer>();
        var callback = new TestCallback();

        var session = await server.StartSessionAsync(
            "player-a",
            "connection-a",
            callback,
            TestContext.Current.CancellationToken);

        var resolved = await server.GetCallbackAsync<TestCallback>(
            session,
            TestContext.Current.CancellationToken);

        Assert.Same(callback, resolved);
    }

    [Fact]
    public async Task BindCurrentSessionBindsSecondCallbackContractByConnectionId()
    {
        var services = new ServiceCollection();
        services.AddLakonaGameServer();
        using var provider = services.BuildServiceProvider();
        var server = provider.GetRequiredService<ILakonaGameServer>();
        var login = new TestCallback();
        var chat = new ChatCallback();

        var session = await server.StartSessionAsync(
            "player-a",
            "connection-a",
            login,
            TestContext.Current.CancellationToken);

        await server.BindCurrentSessionAsync(
            "connection-a",
            chat,
            TestContext.Current.CancellationToken);

        Assert.Same(login, await server.GetCallbackAsync<TestCallback>(session, TestContext.Current.CancellationToken));
        Assert.Same(chat, await server.GetCallbackAsync<ChatCallback>(session, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task BindCurrentSessionRejectsUnboundConnectionId()
    {
        var services = new ServiceCollection();
        services.AddLakonaGameServer();
        using var provider = services.BuildServiceProvider();
        var server = provider.GetRequiredService<ILakonaGameServer>();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => server
            .BindCurrentSessionAsync(
                "missing-connection",
                new ChatCallback(),
                TestContext.Current.CancellationToken)
            .AsTask());

        Assert.Contains("missing-connection", error.Message, StringComparison.Ordinal);
        Assert.Contains("active game session", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BindCurrentSessionReplacesOnlyRequestedCallbackContract()
    {
        var services = new ServiceCollection();
        services.AddLakonaGameServer();
        using var provider = services.BuildServiceProvider();
        var server = provider.GetRequiredService<ILakonaGameServer>();
        var login = new TestCallback();
        var firstChat = new ChatCallback();
        var secondChat = new ChatCallback();

        var session = await server.StartSessionAsync(
            "player-a",
            "connection-a",
            login,
            TestContext.Current.CancellationToken);
        await server.BindCurrentSessionAsync(
            "connection-a",
            firstChat,
            TestContext.Current.CancellationToken);
        await server.BindCurrentSessionAsync(
            "connection-a",
            secondChat,
            TestContext.Current.CancellationToken);

        Assert.Same(login, await server.GetCallbackAsync<TestCallback>(session, TestContext.Current.CancellationToken));
        Assert.Same(secondChat, await server.GetCallbackAsync<ChatCallback>(session, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task MainEntryPublishesReplaysAndAcknowledgesReliablePush()
    {
        var services = new ServiceCollection();
        services.AddLakonaGameServer();
        using var provider = services.BuildServiceProvider();
        var server = provider.GetRequiredService<ILakonaGameServer>();
        var session = new GameSessionKey("player-a", "session-a", 1);
        var delivered = new List<ReliablePushRecord>();

        await server.PublishReliablePushAsync(
            session,
            "matched",
            "payload",
            record =>
            {
                delivered.Add(record);
                return ValueTask.CompletedTask;
            },
            TestContext.Current.CancellationToken);

        var replayedBeforeAck = new List<ReliablePushRecord>();
        await server.ReplayReliablePushAsync(
            session,
            record =>
            {
                replayedBeforeAck.Add(record);
                return ValueTask.CompletedTask;
            },
            TestContext.Current.CancellationToken);

        var outcome = await server.AckReliablePushAsync(
            session,
            session,
            1,
            TestContext.Current.CancellationToken);
        var replayedAfterAck = new List<ReliablePushRecord>();
        await server.ReplayReliablePushAsync(
            session,
            record =>
            {
                replayedAfterAck.Add(record);
                return ValueTask.CompletedTask;
            },
            TestContext.Current.CancellationToken);

        Assert.Single(delivered);
        Assert.Single(replayedBeforeAck);
        Assert.Equal(ReliablePushAckStatus.Accepted, outcome.Status);
        Assert.Empty(replayedAfterAck);
    }

    [Fact]
    public async Task MainEntryPublishesTypedReliablePushThroughSessionCallback()
    {
        var services = new ServiceCollection();
        services.AddLakonaGameServer();
        using var provider = services.BuildServiceProvider();
        var server = provider.GetRequiredService<ILakonaGameServer>();
        var callback = new TestCallback();
        var session = await server.StartSessionAsync(
            "player-a",
            "connection-a",
            callback,
            TestContext.Current.CancellationToken);

        var sequence = await server.PublishReliablePushAsync<TestCallback, string>(
            session,
            "matched",
            "payload",
            static (target, reliableSequence, payload, _) =>
            {
                target.Delivered.Add((reliableSequence.Value, payload));
                return ValueTask.CompletedTask;
            },
            TestContext.Current.CancellationToken);
        await server.ReplayReliablePushAsync<TestCallback, string>(
            session,
            "matched",
            static (target, reliableSequence, payload, _) =>
            {
                target.Delivered.Add((reliableSequence.Value, payload));
                return ValueTask.CompletedTask;
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(1, sequence);
        Assert.Equal(new[] { (1L, "payload"), (1L, "payload") }, callback.Delivered);
    }

    [Fact]
    public void SessionTerminationNoticeCarriesFixedFrameworkReasonWithoutSessionIdentity()
    {
        var issuedAt = new DateTimeOffset(2026, 6, 4, 1, 2, 3, TimeSpan.Zero);

        var notice = new SessionTerminationNotice(
            SessionTerminationReason.ReplacedByNewLogin,
            "This account logged in elsewhere.",
            issuedAt);

        Assert.Equal(SessionTerminationReason.ReplacedByNewLogin, notice.Reason);
        Assert.Equal("This account logged in elsewhere.", notice.Message);
        Assert.Equal(issuedAt, notice.IssuedAt);
    }

    [Fact]
    public async Task TerminateSessionClosesConnectionAndPreservesResumeOutcome()
    {
        var services = new ServiceCollection();
        var closer = new RecordingConnectionCloser();
        services.AddSingleton<IGameSessionConnectionCloser>(closer);
        services.AddLakonaGameServer();
        using var provider = services.BuildServiceProvider();
        var server = provider.GetRequiredService<ILakonaGameServer>();
        var callback = new TerminationCallback();
        var session = await server.StartSessionAsync(
            "player-a",
            "connection-a",
            callback,
            TestContext.Current.CancellationToken);

        await server.TerminateSessionAsync(
            session,
            SessionTerminationReason.ReplacedByNewLogin,
            message: "Duplicate login.",
            cancellationToken: TestContext.Current.CancellationToken);
        var resume = await server.ResumeSessionAsync(
            new GameSessionResumeRequest(session),
            "connection-b",
            callback,
            TestContext.Current.CancellationToken);

        Assert.NotNull(callback.Notice);
        Assert.Equal(SessionTerminationReason.ReplacedByNewLogin, callback.Notice.Reason);
        Assert.Equal("Duplicate login.", callback.Notice.Message);
        var closed = Assert.Single(closer.Closed);
        Assert.Equal(session, closed.Session);
        Assert.Equal("connection-a", closed.ConnectionId);
        Assert.Same(callback.Notice, closed.Notice);
        Assert.Equal(SessionResumeStatus.Terminated, resume.Status);
        Assert.Same(callback.Notice, resume.Termination);
    }

    [Fact]
    public async Task TerminateSessionClosesConnectionWhenNotificationTimesOut()
    {
        var services = new ServiceCollection();
        var closer = new RecordingConnectionCloser();
        services.AddSingleton<IGameSessionConnectionCloser>(closer);
        services.AddLakonaGameServer();
        using var provider = services.BuildServiceProvider();
        var server = provider.GetRequiredService<ILakonaGameServer>();
        var callback = new HangingTerminationCallback();
        var session = await server.StartSessionAsync(
            "player-a",
            "connection-a",
            callback,
            TestContext.Current.CancellationToken);

        await server.TerminateSessionAsync(
            session,
            SessionTerminationReason.Policy,
            options: new SessionTerminationOptions
            {
                NotifyTimeout = TimeSpan.FromMilliseconds(10)
            },
            cancellationToken: TestContext.Current.CancellationToken);

        var closed = Assert.Single(closer.Closed);
        Assert.Equal(session, closed.Session);
        Assert.Equal("connection-a", closed.ConnectionId);
        Assert.NotNull(callback.Notice);
        Assert.Same(callback.Notice, closed.Notice);
    }

    [Fact]
    public async Task TerminateSessionPublishesLifecycleHookWithLiveCallbackAndContainsHandlerFailures()
    {
        var services = new ServiceCollection();
        var closer = new RecordingConnectionCloser();
        var throwingHandler = new ThrowingLifecycleHandler();
        var recordingHandler = new RecordingLifecycleHandler();
        services.AddSingleton<IGameSessionConnectionCloser>(closer);
        services.AddSingleton<IGameSessionLifecycleHandler>(throwingHandler);
        services.AddSingleton<IGameSessionLifecycleHandler>(recordingHandler);
        services.AddLakonaGameServer();
        using var provider = services.BuildServiceProvider();
        var server = provider.GetRequiredService<ILakonaGameServer>();
        var callback = new TerminationCallback();
        var session = await server.StartSessionAsync(
            "player-a",
            "connection-a",
            callback,
            TestContext.Current.CancellationToken);

        await server.TerminateSessionAsync(
            session,
            SessionTerminationReason.Policy,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(throwingHandler.WasCalled);
        Assert.Single(recordingHandler.Terminated);
        Assert.Equal(session, recordingHandler.Terminated[0].Session);
        Assert.NotNull(callback.Notice);
        Assert.Single(closer.Closed);
    }

    [Fact]
    public async Task TerminateSessionPublishesLifecycleHookWithoutLiveCallback()
    {
        var services = new ServiceCollection();
        var recordingHandler = new RecordingLifecycleHandler();
        services.AddSingleton<IGameSessionLifecycleHandler>(recordingHandler);
        services.AddLakonaGameServer();
        using var provider = services.BuildServiceProvider();
        var server = provider.GetRequiredService<ILakonaGameServer>();
        var session = await server.StartSessionAsync(
            "player-a",
            TestContext.Current.CancellationToken);

        await server.TerminateSessionAsync(
            session,
            SessionTerminationReason.Policy,
            cancellationToken: TestContext.Current.CancellationToken);

        var context = Assert.Single(recordingHandler.Terminated);
        Assert.Equal(session, context.Session);
        Assert.Equal(SessionTerminationReason.Policy, context.Notice.Reason);
    }

    private sealed class TestCallback
    {
        public List<(long Sequence, string Payload)> Delivered { get; } = new();
    }

    private sealed class ChatCallback
    {
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private sealed record ConfiguredValue(string Value);

    private sealed class RecordingFeatureMessageHandler : IFeatureMessageHandler
    {
        public List<FeatureMessageRequest> Requests { get; } = [];

        public ValueTask<FeatureMessageReply> HandleAsync(
            FeatureMessageRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            return new ValueTask<FeatureMessageReply>(
                new FeatureMessageReply(ClusterSendStatus.Accepted, new byte[] { 9 }));
        }
    }

    private sealed class TerminationCallback : ILakonaGameSessionCallback
    {
        public SessionTerminationNotice? Notice { get; private set; }

        public ValueTask OnSessionTerminatedAsync(
            SessionTerminationNotice notice,
            CancellationToken cancellationToken = default)
        {
            Notice = notice;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class HangingTerminationCallback : ILakonaGameSessionCallback
    {
        public SessionTerminationNotice? Notice { get; private set; }

        public ValueTask OnSessionTerminatedAsync(
            SessionTerminationNotice notice,
            CancellationToken cancellationToken = default)
        {
            Notice = notice;
            return new ValueTask(Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken));
        }
    }

    private sealed class RecordingConnectionCloser : IGameSessionConnectionCloser
    {
        public List<(GameSessionKey Session, string ConnectionId, SessionTerminationNotice Notice)> Closed { get; } = new();

        public ValueTask CloseConnectionAsync(
            GameSessionKey session,
            string connectionId,
            SessionTerminationNotice notice,
            CancellationToken cancellationToken = default)
        {
            Closed.Add((session, connectionId, notice));
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingLifecycleHandler : IGameSessionLifecycleHandler
    {
        public bool WasCalled { get; private set; }

        public ValueTask OnConnectionOpenedAsync(GameConnectionContext context, CancellationToken cancellationToken = default)
        {
            return default;
        }

        public ValueTask OnSessionBoundAsync(GameSessionBindingContext context, CancellationToken cancellationToken = default)
        {
            return default;
        }

        public ValueTask OnSessionDisconnectedAsync(GameSessionBindingContext context, CancellationToken cancellationToken = default)
        {
            return default;
        }

        public ValueTask OnSessionExpiredAsync(GameSessionBindingContext context, CancellationToken cancellationToken = default)
        {
            return default;
        }

        public ValueTask OnSessionTerminatedAsync(GameSessionTerminationContext context, CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            throw new InvalidOperationException("boom");
        }
    }

    private sealed class RecordingLifecycleHandler : IGameSessionLifecycleHandler
    {
        public List<GameSessionTerminationContext> Terminated { get; } = [];

        public ValueTask OnConnectionOpenedAsync(GameConnectionContext context, CancellationToken cancellationToken = default)
        {
            return default;
        }

        public ValueTask OnSessionBoundAsync(GameSessionBindingContext context, CancellationToken cancellationToken = default)
        {
            return default;
        }

        public ValueTask OnSessionDisconnectedAsync(GameSessionBindingContext context, CancellationToken cancellationToken = default)
        {
            return default;
        }

        public ValueTask OnSessionExpiredAsync(GameSessionBindingContext context, CancellationToken cancellationToken = default)
        {
            return default;
        }

        public ValueTask OnSessionTerminatedAsync(GameSessionTerminationContext context, CancellationToken cancellationToken = default)
        {
            Terminated.Add(context);
            return default;
        }
    }

    internal sealed class FailingHotfixManager : IHotfixManager
    {
        public event EventHandler<HotfixReloadResult>? Reloaded
        {
            add { }
            remove { }
        }

        public HotfixSnapshot Current => new(
            Version: null,
            SourceKind: null,
            SourcePath: "",
            LoadedAtUtc: null,
            DispatchTableVersion: 0,
            Methods: [],
            LastReloadStatus: HotfixReloadStatus.Failed,
            LastFailureMessage: null,
            LastFailureExceptionType: null);

        public ValueTask<HotfixReloadResult> ValidateAsync(CancellationToken cancellationToken = default)
        {
            return ReloadAsync(cancellationToken);
        }

        public ValueTask<HotfixReloadResult> ValidateAsync(
            Lakona.Game.Server.Hotfix.Loading.IHotfixAssemblySource source,
            CancellationToken cancellationToken = default)
        {
            return ValidateAsync(cancellationToken);
        }

        public ValueTask<HotfixReloadResult> ReloadAsync(CancellationToken cancellationToken = default)
        {
            var result = new HotfixReloadResult(
                Status: HotfixReloadStatus.Failed,
                Current: Current,
                RequestedVersion: "1",
                RequestedPath: @"C:\app\hotfix\Server.Hotfix.dll",
                Diagnostics: ["missing assembly"],
                ErrorMessage: "Reload failed");
            return ValueTask.FromResult(result);
        }
    }
}
