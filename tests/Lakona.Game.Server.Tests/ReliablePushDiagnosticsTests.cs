using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Lakona.Game.Abstractions;
using Lakona.Game.Cluster.Rpc;
using Lakona.Game.Server.ReliablePush;
using Lakona.Game.Server.Sessions;
using Lakona.Rpc.Core;
using Lakona.Rpc.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Lakona.Game.Server.Tests;

[Collection(GameSessionPopulationMetricsCollectionNames.Diagnostics)]
public sealed class ReliablePushDiagnosticsTests
{
    [Fact]
    public async Task Ack_keeps_stream_live_capacity_loss_is_logged_once_and_new_session_recovers()
    {
        using var evidence = new Evidence();
        await using var services = new ServiceCollection()
            .AddSingleton<ILoggerFactory>(evidence)
            .AddLakonaGameServerSessions()
            .AddLakonaGameServerReliablePush(options => options.MaxPendingPerSession = 2)
            .BuildServiceProvider();
        var sessions = (InMemoryGameSessionRegistry)services.GetRequiredService<IGameSessionRegistry>();
        var session = await StartAsync(sessions, "first");
        var callback = new Callback();
        await using var connection = new TestCallbackConnection(sessions,
            services.GetRequiredService<GameFrameworkConnectionRegistry>(),
            services.GetRequiredService<GameSessionCallbackProxyRegistry>(), "first", callback);
        var outbox = services.GetRequiredService<IReliablePushOutbox>();
        var runtime = services.GetRequiredService<IReliablePushRuntime>();

        for (var i = 1; i <= 300; i++)
        {
            Assert.Equal(ClientNotificationStatus.Accepted, await runtime.PublishAsync(session, Command(session), Token));
            Assert.Equal(ReliablePushAckStatus.Accepted, (await runtime.AckAsync(session, session, i, Token)).Status);
        }
        Assert.Empty(evidence.Logs);
        Assert.Equal(ClientNotificationStatus.Accepted, await runtime.PublishAsync(session, Command(session), Token));
        Assert.Equal(ClientNotificationStatus.Accepted, await runtime.PublishAsync(session, Command(session), Token));
        var failures = await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(async () =>
            await runtime.PublishAsync(session, Command(session), Token), Token)));
        Assert.All(failures, status => Assert.Equal(ClientNotificationStatus.Failed, status));
        Assert.True(await sessions.IsReliableContinuityLostAsync(session, Token));
        Assert.Equal(302, outbox.GetLastSequence(session));
        var log = Assert.Single(evidence.Logs);
        Assert.Equal(session.SessionId, log["SessionId"]);
        Assert.Equal(2, log["PendingCount"]);
        Assert.Equal(302L, log["LastSequence"]);
        Assert.Equal(300L, log["AcknowledgedSequence"]);
        var measurement = Assert.Single(evidence.Measurements);
        Assert.Equal("lakona.game.reliable_push.continuity_lost", measurement.Name);
        Assert.Equal(1, measurement.Value);
        Assert.Equal(new KeyValuePair<string, object?>("reason", "capacity"), Assert.Single(measurement.Tags));

        await runtime.AckAsync(session, session, 302, Token);
        await runtime.ReplayPendingAsync(session, Token);
        Assert.Equal(ClientNotificationStatus.Failed, await runtime.PublishAsync(session, Command(session), Token));
        Assert.Equal(302, callback.Deliveries);
        Assert.Single(evidence.Logs);
        Assert.Single(evidence.Measurements);

        var replacement = await StartAsync(sessions, "second");
        await using var replacementConnection = new TestCallbackConnection(sessions, "second", callback);
        var replacementRuntime = new ReliablePushRuntime(outbox, new ReliablePushAckService(outbox),
            new LocalClientNotificationCommandDispatcher(replacementConnection.Resolver, evidence), sessions, evidence);
        Assert.Equal(ClientNotificationStatus.Accepted,
            await replacementRuntime.PublishAsync(replacement, Command(replacement), Token));
        Assert.Equal(1, outbox.GetLastSequence(replacement));
        Assert.False(await sessions.IsReliableContinuityLostAsync(replacement, Token));
    }

    [Fact]
    public async Task Send_failure_retains_record_for_replay_and_counts_every_failure_without_log_storm()
    {
        using var evidence = new Evidence();
        var sessions = new InMemoryGameSessionRegistry();
        var session = await StartAsync(sessions, "first");
        var callback = new Callback { Failure = new InvalidOperationException("secret payload credential") };
        await using var connection = new TestCallbackConnection(sessions, "first", callback);
        var outbox = new InMemoryReliablePushOutbox(new ReliablePushOptions { MaxPendingPerSession = 64 });
        var clock = new ManualClock();
        var dispatcher = new LocalClientNotificationCommandDispatcher(connection.Resolver, evidence, clock);
        var runtime = new ReliablePushRuntime(outbox, new ReliablePushAckService(outbox), dispatcher, sessions, evidence);
        Assert.Equal(ClientNotificationStatus.Failed, await runtime.PublishAsync(session, Command(session), Token));
        // Exercise concurrent dispatcher failures independently of the outbox's serial barrier.
        await Task.WhenAll(Enumerable.Range(0, 31).Select(_ => Task.Run(async () =>
            Assert.Equal(ClientNotificationStatus.Failed, await dispatcher.DispatchAsync(Command(session), Token)), Token)));
        var log = Assert.Single(evidence.Logs);
        Assert.Equal(session.SessionId, log["SessionId"]);
        Assert.Equal(typeof(InvalidOperationException).FullName, log["ExceptionType"]);
        Assert.Equal(1, log["ServiceId"]);
        Assert.Equal(2, log["MethodId"]);
        Assert.DoesNotContain("secret", string.Join(" ", log.Values));
        Assert.DoesNotContain("private-owner", string.Join(" ", log.Values));
        Assert.Equal(32, evidence.Measurements.Sum(m => m.Value));
        Assert.All(evidence.Measurements, m =>
        {
            Assert.Equal("lakona.game.notification.send_failure", m.Name);
            Assert.Empty(m.Tags);
        });
        clock.Timestamp = 30_000;
        Assert.Equal(ClientNotificationStatus.Failed, await dispatcher.DispatchAsync(Command(session), Token));
        Assert.Equal(2, evidence.Logs.Count);
        Assert.Equal(33, evidence.Measurements.Sum(m => m.Value));
        Assert.False(await sessions.IsReliableContinuityLostAsync(session, Token));
        callback.Failure = null;
        await runtime.ReplayPendingAsync(session, Token);
        Assert.Equal(1, callback.Deliveries);
        await runtime.AckAsync(session, session, 1, Token);
        await runtime.ReplayPendingAsync(session, Token);
        Assert.Equal(1, callback.Deliveries);
        Assert.Equal(ClientNotificationStatus.Accepted, await runtime.PublishAsync(session, Command(session), Token));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Caller_cancellation_is_not_a_send_failure_and_generated_exception_behavior_is_preserved(bool generated)
    {
        using var evidence = new Evidence();
        var sessions = new InMemoryGameSessionRegistry();
        var session = await StartAsync(sessions, "first");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var callback = new Callback { BeforeSend = cancellation.Cancel, Failure = new OperationCanceledException(cancellation.Token) };
        await using var connection = new TestCallbackConnection(sessions, "first", callback);
        var dispatcher = new LocalClientNotificationCommandDispatcher(connection.Resolver, evidence);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Send(cancellation.Token));
        Assert.Empty(evidence.Logs);
        Assert.Empty(evidence.Measurements);

        callback.BeforeSend = null;
        callback.Failure = new InvalidOperationException("secret");
        if (generated)
            await Assert.ThrowsAsync<InvalidOperationException>(() => Send(Token));
        else
            Assert.Equal(ClientNotificationStatus.Failed, await Send(Token));
        Assert.Single(evidence.Logs);
        Assert.Single(evidence.Measurements);

        Task<ClientNotificationStatus> Send(CancellationToken token) => generated
            ? dispatcher.DispatchGeneratedAsync<Callback, string>(session, 1, 2, "secret", token).AsTask()
            : dispatcher.DispatchAsync(Command(session), token).AsTask();
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static async Task<GameSessionKey> StartAsync(InMemoryGameSessionRegistry sessions, string connection)
    {
        var session = await sessions.StartNewSessionAsync("private-owner", Token);
        await sessions.SetReliablePushPolicyAsync(session, true, Token);
        await sessions.BindSessionAsync(session, connection, Token);
        return session;
    }

    private static ClientNotificationCommand Command(GameSessionKey session) => new()
    {
        OwnerKey = session.OwnerKey, SessionId = session.SessionId,
        CallbackContractType = typeof(Callback).AssemblyQualifiedName!,
        ServiceId = 1, MethodId = 2, MethodName = "Notify", Payload = [1, 2, 3]
    };

    private sealed class Callback : IRpcNotificationDispatchTarget
    {
        public Exception? Failure;
        public Action? BeforeSend;
        public int Deliveries;
        public ValueTask DispatchNotificationAsync<T>(int serviceId, int methodId, T payload,
            RpcPushMetadata? metadata, CancellationToken cancellationToken = default) => Send();
        public ValueTask DispatchNotificationAsync(int serviceId, int methodId, ReadOnlyMemory<byte> payload,
            RpcPushMetadata? metadata, CancellationToken cancellationToken = default) => Send();
        private ValueTask Send()
        {
            BeforeSend?.Invoke();
            if (Failure is { } failure) throw failure;
            Interlocked.Increment(ref Deliveries);
            return default;
        }
    }

    private sealed class ManualClock : TimeProvider
    {
        public long Timestamp;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Timestamp;
    }

    private sealed class Evidence : ILoggerFactory, ILogger
    {
        private readonly MeterListener listener = new();
        public ConcurrentQueue<Dictionary<string, object?>> Logs { get; } = new();
        public ConcurrentQueue<(string Name, long Value, KeyValuePair<string, object?>[] Tags)> Measurements { get; } = new();
        public Evidence()
        {
            listener.InstrumentPublished = (instrument, current) =>
            {
                if (instrument.Name is "lakona.game.reliable_push.continuity_lost" or "lakona.game.notification.send_failure")
                    current.EnableMeasurementEvents(instrument);
            };
            listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
                Measurements.Enqueue((instrument.Name, value, tags.ToArray())));
            listener.Start();
        }
        public ILogger CreateLogger(string categoryName) => this;
        public void AddProvider(ILoggerProvider provider) => throw new NotSupportedException();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Assert.Null(exception);
            Logs.Enqueue(((IEnumerable<KeyValuePair<string, object?>>)state!).ToDictionary());
        }
        public void Dispose() => listener.Dispose();
    }
}
