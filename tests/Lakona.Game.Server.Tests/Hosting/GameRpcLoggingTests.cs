using System.Buffers;
using System.Collections.Concurrent;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Hosting;
using Lakona.Rpc.Client;
using Lakona.Rpc.Core;
using Lakona.Rpc.Server;
using Lakona.Rpc.Serializer.Json;
using Lakona.Rpc.Transport.Tcp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Lakona.Game.Server.Tests.Hosting;

public sealed class GameRpcLoggingTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Typed_calls_use_application_categories_and_explicit_payload_switch(bool include, bool failingProvider)
    {
        var provider = new RecordingProvider { FailPayload = failingProvider };
        using var factory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Trace).AddProvider(provider));
        var options = new LakonaRpcLoggingOptions { IncludePayload = include };
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var listener = new TcpConnectionAcceptor(0);
        var address = new Uri(listener.ListenAddress);
        var builder = RpcServerHostBuilder.Create().UseSerializer(new JsonRpcSerializer())
            .UseAcceptor(_ => new ValueTask<IRpcConnectionAcceptor>(listener))
            .UseLoggerFactory(new GameRpcLoggerFactory(factory, "Lakona.Game.Rpc"));
        builder.PayloadLogging = options;
        builder.ServiceRegistry.RegisterSingleton(77, new object(), "LoginService").Register<Login, Login>(1,
            (_, request, _) => new ValueTask<Login>(request), "LoginAsync");
        builder.ServiceRegistry.RegisterRaw(77, 2,
            async (_, channel, payload, ct) =>
            {
                var notice = new Login("notify-alice", "do-not-log");
                await channel.SendAsync(77, 3, notice, cancellationToken: ct);
                await channel.SendRawAsync<Login>(77, 3, System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(notice), null, ct);
                return RpcRawResult.Ok(payload);
            }, "RawService", "Echo");
        var run = builder.RunAsync(deadline.Token).AsTask();
        try
        {
            await using var client = new RpcClientRuntime(new RpcClientOptions(new TcpTransport(address.Host, address.Port), new JsonRpcSerializer())
            {
                LoggerFactory = new GameRpcLoggerFactory(factory, "Lakona.Game.Cluster.Rpc"),
                PayloadLogging = options
            });
            await client.StartAsync(deadline.Token);
            var reply = await client.CallAsync(new RpcMethod<Login, Login>(77, 1, "LoginService", "LoginAsync"), new Login("alice", "do-not-log"), deadline.Token);
            Assert.Equal("alice", reply.Name);
            Assert.Contains(provider.Entries, e => e.Category == "Lakona.Game.Rpc");
            Assert.Contains(provider.Entries, e => e.Category == "Lakona.Game.Cluster.Rpc");
            var payloads = provider.Entries.Where(e => e.Text.Contains("payload=")).ToArray();
            Assert.Equal(include && !failingProvider ? 4 : 0, payloads.Length);
            Assert.All(payloads, e => { Assert.Contains("alice", e.Text); Assert.DoesNotContain("do-not-log", e.Text); });
            Assert.All(payloads, e => Assert.Contains("LoginService.LoginAsync", e.Text));
            Assert.Contains(provider.Entries, e => e.Category == "Lakona.Game.Cluster.Rpc" && e.Text.Contains("RPC request completed LoginService.LoginAsync requestId=") && e.Text.Contains("status=Ok serviceId=77 methodId=1"));
            Assert.Contains(provider.Entries, e => e.Category == "Lakona.Game.Rpc" && e.Text.Contains("RPC request received LoginService.LoginAsync requestId=") && e.Text.Contains("serviceId=77 methodId=1 connectionId="));
            Assert.DoesNotContain(provider.Entries, e => e.Category.StartsWith("Lakona.Rpc.Server.Request") || e.Category.StartsWith("Lakona.Rpc.Client.Request"));
            var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var count = 0;
            client.RegisterNotificationHandler(new RpcNotificationMethod<Login>(77, 3, "LoginCallback.OnLogin"), notice =>
            {
                Assert.Equal("notify-alice", notice.Name);
                if (Interlocked.Increment(ref count) == 2) received.TrySetResult();
            });
            using var raw = await client.CallRawAsync(77, 2, writer => writer.Write(new byte[] { 1, 2 }), "RawService.Echo", deadline.Token);
            await received.Task.WaitAsync(deadline.Token);
            var notifications = provider.Entries.Where(e => e.Text.Contains("payload=") && e.Text.Contains("notify-alice")).ToArray();
            Assert.Equal(include && !failingProvider ? 4 : 0, notifications.Length);
            Assert.All(payloads.Concat(notifications), e => Assert.True(e.Category is "Lakona.Game.Rpc" or "Lakona.Game.Cluster.Rpc"));
            Assert.All(notifications, e => Assert.DoesNotContain("do-not-log", e.Text));
            Assert.Equal(new byte[] { 1, 2 }, raw.Memory.ToArray());
            Assert.Contains(provider.Entries, e => e.Category == "Lakona.Game.Cluster.Rpc" && e.Text.Contains("RawService.Echo"));
            Assert.Contains(provider.Entries, e => e.Category == "Lakona.Game.Rpc" && e.Text.Contains("RawService.Echo"));
        }
        finally
        {
            deadline.Cancel();
            try { await run; } catch (OperationCanceledException) { }
        }
    }

    [Fact]
    public void Configuration_defaults_and_independent_switches()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Lakona:Logging:Rpc:IncludePayload"] = "true",
            ["Lakona:Logging:Rpc:RedactedMembers:0"] = "Email"
        }).Build();
        var options = LakonaGameRuntimeOptions.FromConfiguration(config).Logging;
        Assert.True(options.Rpc.IncludePayload);
        Assert.False(options.ClusterRpc.IncludePayload);
        Assert.Contains("Password", options.Rpc.RedactedMembers);
        Assert.Contains("Email", options.Rpc.RedactedMembers);
        Assert.False(new LakonaGameRuntimeOptions().Logging.Rpc.IncludePayload);
    }

    [Fact]
    public void Snapshot_is_bounded_redacts_nested_fields_and_contains_getter_failures()
    {
        var options = new LakonaRpcLoggingOptions { IncludePayload = true, MaxPayloadCharacters = 128 };
        var snapshot = new Lakona.Rpc.PayloadSnapshot(options);
        var redacted = snapshot.Format(new { Nested = new Dictionary<string, object> { ["Password"] = "secret" } });
        Assert.DoesNotContain("secret", redacted);
        Assert.Contains("redacted", redacted);
        Assert.True(snapshot.Format(new { Text = new string('x', 10000) }).Length <= 128);
        Assert.Equal("[payload unavailable]", snapshot.Format(new Broken()));
    }

    [Theory]
    [InlineData(false, LogLevel.Trace)]
    [InlineData(true, LogLevel.Information)]
    public void Disabled_payload_or_filtered_trace_skips_snapshot_getters(bool include, LogLevel level)
    {
        var provider = new RecordingProvider();
        using var factory = LoggerFactory.Create(b => b.SetMinimumLevel(level).AddProvider(provider));
        var logger = Lakona.Rpc.RpcPayloadLogging.Create(factory, "Rpc.Test",
            new RpcPayloadLoggingOptions { IncludePayload = include });
        var probe = new GetterProbe();
        logger?.Write("request", 1, 1, 1, probe);
        Assert.False(probe.WasRead);
        Assert.Empty(provider.Entries);
    }

    private sealed class GetterProbe
    {
        public bool WasRead { get; private set; }
        public int Value { get { WasRead = true; return 42; } }
    }

    public sealed record Login(string Name, string Password);
    private sealed class Broken { public string Value => throw new Exception("secret"); }
    private sealed class RecordingProvider : ILoggerProvider
    {
        internal bool FailPayload;
        internal readonly ConcurrentQueue<(string Category, string Text)> Entries = new();
        public ILogger CreateLogger(string categoryName) => new Recorder(this, categoryName);
        public void Dispose() { }
        private sealed class Recorder(RecordingProvider owner, string category) : ILogger
        {
            public bool IsEnabled(LogLevel level) => true;
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (owner.FailPayload && state is IEnumerable<KeyValuePair<string, object?>> fields && fields.Any(field => field.Key == "Payload")) throw new InvalidOperationException("Logging failed.");
                owner.Entries.Enqueue((category, formatter(state, exception)));
            }
        }
    }
}
