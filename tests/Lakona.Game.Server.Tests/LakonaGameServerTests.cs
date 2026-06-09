using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Lakona.Game.Abstractions;
using Lakona.Game.Server.Hosting;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.ReliablePush;
using Lakona.Game.Server.Sessions;
using Xunit;

namespace Lakona.Game.Server.Tests;

public sealed class LakonaGameServerTests
{
    [Fact]
    public async Task InitialHotfixLoad_Throws_WhenReloadFails()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHotfixManager>(new FailingHotfixManager());
        await using var provider = services.BuildServiceProvider();

        var hotfix = provider.GetRequiredService<IHotfixManager>();
        var result = await hotfix.ReloadAsync();

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

        Lakona.Game.Server.Hosting.LakonaGameServer.DiscoverStableFeaturesForTesting(services, configuration);

        var after = AssemblyLoadContext.Default.Assemblies
            .Select(assembly => assembly.GetName().Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain("Server.Hotfix", after.Except(before, StringComparer.Ordinal));
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
    public async Task MainEntryStartsSessionBindsEndpointAndReturnsCallback()
    {
        var services = new ServiceCollection();
        services.AddLakonaGameServer();
        using var provider = services.BuildServiceProvider();
        var server = provider.GetRequiredService<ILakonaGameServer>();
        var callback = new TestCallback();

        var session = await server.StartSessionAsync(
            "player-a",
            "control",
            "connection-a",
            callback,
            TestContext.Current.CancellationToken);

        var resolved = await server.GetCallbackAsync<TestCallback>(
            session,
            "control",
            TestContext.Current.CancellationToken);

        Assert.Same(callback, resolved);
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
    public async Task MainEntryPublishesTypedReliablePushThroughEndpointCallback()
    {
        var services = new ServiceCollection();
        services.AddLakonaGameServer();
        using var provider = services.BuildServiceProvider();
        var server = provider.GetRequiredService<ILakonaGameServer>();
        var callback = new TestCallback();
        var session = await server.StartSessionAsync(
            "player-a",
            GameEndpointName.Control,
            "connection-a",
            callback,
            TestContext.Current.CancellationToken);

        var sequence = await server.PublishReliablePushAsync<TestCallback, string>(
            session,
            GameEndpointName.Control,
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
            GameEndpointName.Control,
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
    public void SessionTerminationNoticeCarriesFixedFrameworkReason()
    {
        var session = new GameSessionKey("player-a", "session-a", 1);
        var issuedAt = new DateTimeOffset(2026, 6, 4, 1, 2, 3, TimeSpan.Zero);

        var notice = new SessionTerminationNotice(
            session,
            SessionTerminationReason.ReplacedByNewLogin,
            "This account logged in elsewhere.",
            issuedAt);

        Assert.Equal(session, notice.Session);
        Assert.Equal(SessionTerminationReason.ReplacedByNewLogin, notice.Reason);
        Assert.Equal("This account logged in elsewhere.", notice.Message);
        Assert.Equal(issuedAt, notice.IssuedAt);
    }

    [Fact]
    public async Task TerminateSessionUsesControlEndpointByDefaultAndPreservesResumeOutcome()
    {
        var services = new ServiceCollection();
        var closer = new RecordingEndpointCloser();
        services.AddSingleton<IGameSessionEndpointCloser>(closer);
        services.AddLakonaGameServer();
        using var provider = services.BuildServiceProvider();
        var server = provider.GetRequiredService<ILakonaGameServer>();
        var callback = new TerminationCallback();
        var session = await server.StartSessionAsync(
            "player-a",
            GameEndpointName.Control,
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
            GameEndpointName.Control,
            "connection-b",
            callback,
            TestContext.Current.CancellationToken);

        Assert.NotNull(callback.Notice);
        Assert.Equal(session, callback.Notice.Session);
        Assert.Equal(SessionTerminationReason.ReplacedByNewLogin, callback.Notice.Reason);
        Assert.Equal("Duplicate login.", callback.Notice.Message);
        var closed = Assert.Single(closer.Closed);
        Assert.Equal(new SessionEndpointKey(session, GameEndpointName.Control), closed.Endpoint);
        Assert.Equal("connection-a", closed.ConnectionId);
        Assert.Same(callback.Notice, closed.Notice);
        Assert.Equal(SessionResumeStatus.Terminated, resume.Status);
        Assert.Same(callback.Notice, resume.Termination);
    }

    [Fact]
    public async Task TerminateSessionClosesEndpointWhenNotificationTimesOut()
    {
        var services = new ServiceCollection();
        var closer = new RecordingEndpointCloser();
        services.AddSingleton<IGameSessionEndpointCloser>(closer);
        services.AddLakonaGameServer();
        using var provider = services.BuildServiceProvider();
        var server = provider.GetRequiredService<ILakonaGameServer>();
        var callback = new HangingTerminationCallback();
        var session = await server.StartSessionAsync(
            "player-a",
            GameEndpointName.Control,
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
        Assert.Equal(new SessionEndpointKey(session, GameEndpointName.Control), closed.Endpoint);
        Assert.Equal("connection-a", closed.ConnectionId);
        Assert.NotNull(callback.Notice);
        Assert.Same(callback.Notice, closed.Notice);
    }

    private sealed class TestCallback
    {
        public List<(long Sequence, string Payload)> Delivered { get; } = new();
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

    private sealed class RecordingEndpointCloser : IGameSessionEndpointCloser
    {
        public List<(SessionEndpointKey Endpoint, string ConnectionId, SessionTerminationNotice Notice)> Closed { get; } = new();

        public ValueTask CloseEndpointAsync(
            SessionEndpointKey endpoint,
            string connectionId,
            SessionTerminationNotice notice,
            CancellationToken cancellationToken = default)
        {
            Closed.Add((endpoint, connectionId, notice));
            return ValueTask.CompletedTask;
        }
    }

    internal sealed class FailingHotfixManager : IHotfixManager
    {
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
