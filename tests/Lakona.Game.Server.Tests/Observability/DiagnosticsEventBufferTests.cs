using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Observability;
using Lakona.Game.Server.Observability.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Xunit;
using GameActor = Lakona.Game.Server.Actors.Actor;

namespace Lakona.Game.Server.Tests.Observability;

public sealed class DiagnosticsEventBufferTests
{
    [Fact]
    public void Bounded_buffer_keeps_most_recent_events_newest_first()
    {
        var buffer = new BoundedDiagnosticsEventBuffer(2, LogLevel.Trace);

        buffer.Publish(CreateEvent(LogLevel.Warning, "first"));
        buffer.Publish(CreateEvent(LogLevel.Error, "second"));
        buffer.Publish(CreateEvent(LogLevel.Critical, "third"));

        var snapshot = buffer.Snapshot(10);

        Assert.Equal(["third", "second"], snapshot.Select(static evt => evt.Message));
    }

    [Fact]
    public void Events_below_minimum_level_are_filtered()
    {
        var buffer = new BoundedDiagnosticsEventBuffer(8, LogLevel.Warning);

        buffer.Publish(CreateEvent(LogLevel.Information, "ignored"));
        buffer.Publish(CreateEvent(LogLevel.Warning, "kept"));

        var diagnostic = Assert.Single(buffer.Snapshot(10));
        Assert.Equal("kept", diagnostic.Message);
    }

    [Fact]
    public void Snapshot_limit_is_honored()
    {
        var buffer = new BoundedDiagnosticsEventBuffer(8, LogLevel.Trace);

        buffer.Publish(CreateEvent(LogLevel.Warning, "one"));
        buffer.Publish(CreateEvent(LogLevel.Warning, "two"));
        buffer.Publish(CreateEvent(LogLevel.Warning, "three"));

        var snapshot = buffer.Snapshot(2);

        Assert.Equal(["three", "two"], snapshot.Select(static evt => evt.Message));
    }

    [Fact]
    public async Task Concurrent_publishers_and_snapshots_preserve_the_bound()
    {
        const int capacity = 64;
        var buffer = new BoundedDiagnosticsEventBuffer(capacity, LogLevel.Trace);
        var publishers = Enumerable.Range(0, 8)
            .Select(worker => Task.Run(() =>
            {
                for (var index = 0; index < 2_000; index++)
                {
                    buffer.Publish(CreateEvent(LogLevel.Warning, $"{worker}:{index}"));
                    Assert.True(buffer.Snapshot(capacity).Count <= capacity);
                }
            }, TestContext.Current.CancellationToken));

        await Task.WhenAll(publishers);

        var snapshot = buffer.Snapshot(capacity);
        Assert.Equal(capacity, snapshot.Count);
        Assert.Equal(capacity, snapshot.Select(static item => item.Message).Distinct().Count());
    }

    [Fact]
    public void Sanitized_event_dimensions_do_not_use_sensitive_keys()
    {
        var buffer = new BoundedDiagnosticsEventBuffer(8, LogLevel.Trace);
        var diagnostic = CreateEvent(
            LogLevel.Warning,
            "sanitized",
            new Dictionary<string, string?>
            {
                ["actor_id"] = "actor/secret",
                ["session_id"] = "session-secret",
                ["connection_id"] = "connection-secret",
                ["token"] = "token-secret",
                ["payload"] = "payload-secret",
                ["call_chain"] = "call-chain-secret",
                ["message_type"] = "Ping"
            });

        buffer.Publish(diagnostic);

        var evt = Assert.Single(buffer.Snapshot(10));
        Assert.DoesNotContain("actor_id", evt.Dimensions.Keys);
        Assert.DoesNotContain("session_id", evt.Dimensions.Keys);
        Assert.DoesNotContain("connection_id", evt.Dimensions.Keys);
        Assert.DoesNotContain("token", evt.Dimensions.Keys);
        Assert.DoesNotContain("payload", evt.Dimensions.Keys);
        Assert.DoesNotContain("call_chain", evt.Dimensions.Keys);
        Assert.Equal("Ping", evt.Dimensions["message_type"]);
    }

    [Fact]
    public void Event_messages_redact_spaced_secret_values_bearer_tokens_and_paths()
    {
        var buffer = new BoundedDiagnosticsEventBuffer(8, LogLevel.Trace);

        buffer.Publish(CreateEvent(
            LogLevel.Error,
            "token: abc123 password hunter2 password=hunter2 Bearer abc.def.ghi payload { \"secret\": true } at C:\\deploy\\private\\hotfix.dll and /var/secrets/hotfix.dll"));

        var evt = Assert.Single(buffer.Snapshot(10));
        Assert.DoesNotContain("abc123", evt.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hunter2", evt.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("abc.def.ghi", evt.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"secret\"", evt.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("C:\\deploy\\private\\hotfix.dll", evt.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/var/secrets/hotfix.dll", evt.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[redacted", evt.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Event_messages_preserve_benign_sensitive_words_without_values()
    {
        var buffer = new BoundedDiagnosticsEventBuffer(8, LogLevel.Trace);

        buffer.Publish(CreateEvent(
            LogLevel.Information,
            "request completed; payload size recorded; requestId assigned"));

        var evt = Assert.Single(buffer.Snapshot(10));
        Assert.Equal("request completed; payload size recorded; requestId assigned", evt.Message);
    }

    [Fact]
    public void Event_dimension_values_redact_sensitive_fragments_for_allowed_keys()
    {
        var buffer = new BoundedDiagnosticsEventBuffer(8, LogLevel.Trace);

        buffer.Publish(CreateEvent(
            LogLevel.Error,
            "sanitized",
            new Dictionary<string, string?>
            {
                ["provider"] = "hotfix token=abc123 C:\\deploy\\private\\hotfix.dll /var/secrets/hotfix.dll",
                ["message_type"] = "request completed"
            }));

        var evt = Assert.Single(buffer.Snapshot(10));
        Assert.DoesNotContain("abc123", evt.Dimensions["provider"], StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("C:\\deploy\\private\\hotfix.dll", evt.Dimensions["provider"], StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/var/secrets/hotfix.dll", evt.Dimensions["provider"], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[redacted", evt.Dimensions["provider"], StringComparison.OrdinalIgnoreCase);
        Assert.Equal("request completed", evt.Dimensions["message_type"]);
    }

    [Fact]
    public void Logger_provider_captures_Lakona_warnings_without_rendering_templates_or_structured_secrets()
    {
        var buffer = new BoundedDiagnosticsEventBuffer(8, LogLevel.Warning);
        using var provider = new DiagnosticsEventLoggerProvider(buffer, LogLevel.Warning);
        var logger = provider.CreateLogger("Lakona.Game.Server.Auth");

        logger.LogWarning("Failed login for {Token}", "secret-token-value");

        var evt = Assert.Single(buffer.Snapshot(10));
        Assert.Equal(LogLevel.Warning, evt.Level);
        Assert.Equal("Lakona.Game.Server.Auth", evt.Category);
        Assert.Equal("framework.log", evt.Kind);
        Assert.DoesNotContain("Failed login", evt.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-token-value", evt.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Token", evt.Dimensions.Keys);
        Assert.DoesNotContain(evt.Dimensions.Values, value => value?.Contains("secret", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public void Actor_bridge_publishes_sanitized_actor_diagnostic_events()
    {
        var buffer = new BoundedDiagnosticsEventBuffer(8, LogLevel.Trace);
        var bridge = new ActorDiagnosticsEventBridge(buffer);

        bridge.OnDeadLetter(new ActorDeadLetterDiagnostic(
            ActorId.From("actor/secret"),
            new PayloadProbe("payload-secret"),
            "mailbox_full"));
        bridge.OnSlowMessage(new ActorSlowMessageDiagnostic(
            ActorId.From("actor/secret"),
            new PayloadProbe("payload-secret"),
            TimeSpan.FromMilliseconds(42)));
        bridge.OnCallTimeout(new ActorCallTimeoutDiagnostic(
            ActorId.From("caller/secret"),
            ActorId.From("target/secret"),
            new PayloadProbe("payload-secret"),
            TimeSpan.FromMilliseconds(250),
            ActorCallTimeoutReason.ResponseTimeout,
            [ActorId.From("chain/secret")]));

        var events = buffer.Snapshot(10);
        Assert.Equal(["actor.call_timeout", "actor.slow_message", "actor.dead_letter"], events.Select(static evt => evt.Kind));

        foreach (var evt in events)
        {
            Assert.DoesNotContain("actor/secret", evt.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("payload-secret", evt.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("chain/secret", evt.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(evt.Dimensions.Keys, IsSensitiveKey);
            Assert.DoesNotContain(evt.Dimensions.Values, value => value?.Contains("secret", StringComparison.OrdinalIgnoreCase) == true);
        }
    }

    [Fact]
    public async Task Actor_observer_publishes_sanitized_event_before_user_slow_message_callback_throws()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var buffer = new BoundedDiagnosticsEventBuffer(8, LogLevel.Trace);
        var id = ActorId.From("slow/secret");

        await using var provider = new ServiceCollection()
            .AddSingleton<IDiagnosticsEventSink>(buffer)
            .AddSingleton<IActorDiagnosticsObserver, ActorDiagnosticsEventBridge>()
            .AddLakonaGameServerActors(options =>
            {
                options.SlowMessageThreshold = TimeSpan.FromMilliseconds(1);
                options.SlowMessageHandler = _ => throw new InvalidOperationException("user callback failed");
            })
            .BuildServiceProvider();

        var hosting = provider.GetRequiredService<ActorHosting>();
        var runtime = provider.GetRequiredService<IActorRuntime>();
        await hosting.CreateAsync<SlowActor>(id, cancellationToken);

        await runtime.TellAsync<SlowActor>(
            id,
            static (actor, ct) => actor.DelayAsync(TimeSpan.FromMilliseconds(50), ct),
            cancellationToken);

        var evt = await WaitForEventAsync(
            buffer,
            static evt => evt.Kind == "actor.slow_message",
            cancellationToken);
        Assert.DoesNotContain("slow/secret", evt.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(evt.Dimensions.Keys, IsSensitiveKey);
    }

    [Fact]
    public async Task Actor_observer_preserves_runtime_message_type_name()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var buffer = new BoundedDiagnosticsEventBuffer(8, LogLevel.Trace);
        var id = ActorId.From("slow/type-name");

        await using var provider = new ServiceCollection()
            .AddSingleton<IDiagnosticsEventSink>(buffer)
            .AddSingleton<IActorDiagnosticsObserver, ActorDiagnosticsEventBridge>()
            .AddLakonaGameServerActors(options => options.SlowMessageThreshold = TimeSpan.FromMilliseconds(1))
            .BuildServiceProvider();

        var hosting = provider.GetRequiredService<ActorHosting>();
        var runtime = provider.GetRequiredService<IActorRuntime>();
        await hosting.CreateAsync<SlowActor>(id, cancellationToken);

        await runtime.TellAsync<SlowActor>(
            id,
            static (actor, ct) => actor.DelayAsync(TimeSpan.FromMilliseconds(50), ct),
            cancellationToken);

        var evt = await WaitForEventAsync(
            buffer,
            static evt => evt.Kind == "actor.slow_message",
            cancellationToken);
        Assert.Contains("ActorRuntimeEnvelope", evt.Dimensions["message_type"], StringComparison.Ordinal);
        Assert.NotEqual("String", evt.Dimensions["message_type"]);
    }

    [Fact]
    public void LakonaActorRuntime_preserves_two_argument_constructor()
    {
        var constructor = typeof(LakonaActorRuntime).GetConstructor(
            [typeof(IServiceProvider), typeof(ActorRuntimeOptions)]);

        Assert.NotNull(constructor);
    }

    [Fact]
    public void Disabled_event_buffer_does_not_retain_published_events_or_capture_logs()
    {
        using var provider = new ServiceCollection()
            .AddLakonaGameObservability(new LakonaObservabilityOptions
            {
                Diagnostics = new LakonaDiagnosticsObservabilityOptions
                {
                    EventBuffer = new LakonaDiagnosticsEventBufferOptions { Enabled = false }
                }
            })
            .BuildServiceProvider();
        var sink = provider.GetRequiredService<IDiagnosticsEventSink>();
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("Lakona.Game.Server.Tests");

        sink.Publish(CreateEvent(LogLevel.Critical, "direct"));
        logger.LogCritical("diagnostic secret {Token}", "secret-token");

        Assert.Empty(sink.Snapshot(10));
    }

    [Fact]
    public void Registered_event_buffer_honors_configured_capacity_and_minimum_level_from_final_options()
    {
        using var provider = new ServiceCollection()
            .AddLakonaGameObservability(new LakonaObservabilityOptions
            {
                Diagnostics = new LakonaDiagnosticsObservabilityOptions
                {
                    EventBuffer = new LakonaDiagnosticsEventBufferOptions
                    {
                        Capacity = 2,
                        MinimumLevel = LogLevel.Error
                    }
                }
            })
            .BuildServiceProvider();
        var sink = provider.GetRequiredService<IDiagnosticsEventSink>();

        sink.Publish(CreateEvent(LogLevel.Warning, "ignored"));
        sink.Publish(CreateEvent(LogLevel.Error, "first"));
        sink.Publish(CreateEvent(LogLevel.Critical, "second"));
        sink.Publish(CreateEvent(LogLevel.Critical, "third"));

        Assert.Equal(["third", "second"], sink.Snapshot(10).Select(static evt => evt.Message));
    }

    [Fact]
    public void Event_buffer_uses_later_explicit_observability_options()
    {
        var services = new ServiceCollection()
            .AddLakonaGameObservability();
        services.RemoveAll<LakonaObservabilityOptions>();
        services.AddSingleton(new LakonaObservabilityOptions
        {
            Diagnostics = new LakonaDiagnosticsObservabilityOptions
            {
                EventBuffer = new LakonaDiagnosticsEventBufferOptions
                {
                    Capacity = 1,
                    MinimumLevel = LogLevel.Critical
                }
            }
        });

        using var provider = services.BuildServiceProvider();
        var sink = provider.GetRequiredService<IDiagnosticsEventSink>();

        sink.Publish(CreateEvent(LogLevel.Error, "ignored"));
        sink.Publish(CreateEvent(LogLevel.Critical, "kept"));
        sink.Publish(CreateEvent(LogLevel.Critical, "newest"));

        var evt = Assert.Single(sink.Snapshot(10));
        Assert.Equal("newest", evt.Message);
    }

    private static DiagnosticsEvent CreateEvent(
        LogLevel level,
        string message,
        IReadOnlyDictionary<string, string?>? dimensions = null)
    {
        return new DiagnosticsEvent(
            DateTimeOffset.UtcNow,
            level,
            "Lakona.Game.Server.Tests",
            "test.event",
            message,
            TraceId: null,
            CorrelationId: null,
            dimensions ?? new Dictionary<string, string?>());
    }

    private static bool IsSensitiveKey(string key)
    {
        return key is "actor_id" or "session_id" or "connection_id" or "token" or "payload" or "call_chain";
    }

    private static async Task<DiagnosticsEvent> WaitForEventAsync(
        IDiagnosticsEventSink sink,
        Func<DiagnosticsEvent, bool> predicate,
        CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        while (true)
        {
            var evt = sink.Snapshot(10).FirstOrDefault(predicate);
            if (evt is not null)
            {
                return evt;
            }

            await Task.Delay(10, linked.Token);
        }
    }

    private sealed record PayloadProbe(string Value);

    private sealed class SlowActor : GameActor
    {
        public async ValueTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            await Task.Delay(delay, cancellationToken);
        }
    }
}
