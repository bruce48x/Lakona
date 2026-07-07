using Microsoft.Extensions.Logging;
using Lakona.Rpc.Client;
using Lakona.Rpc.Core;
using Lakona.Rpc.Server;
using Lakona.Rpc.Serializer.Json;
using Lakona.Rpc.Transport.Loopback;

namespace Lakona.Rpc.Tests;

public sealed class RpcRequestLoggingTests
{
    private static readonly RpcMethod<string, string> EchoMethod = new(1, 1);
    private static readonly RpcNotificationMethod<string> NotifyMethod = new(2, 2);

    [Fact]
    public async Task Server_successful_request_writes_debug_request_logs()
    {
        LoopbackTransport.CreatePair(out var clientTransport, out var serverTransport);
        var serializer = new JsonRpcSerializer();
        var loggerProvider = new CategoryRecordingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Debug);
            logging.AddProvider(loggerProvider);
        });
        var requestLogger = loggerFactory.CreateLogger("Lakona.Rpc.Server.Request");
        var server = new RpcSession(
            serverTransport,
            serializer,
            registry: null,
            contextId: "request-log-test",
            ownsTransport: false,
            requestLogger: requestLogger);
        server.Register(1, 1, (req, ct) =>
        {
            var arg = serializer.Deserialize<string>(req.Payload);
            using var payload = serializer.SerializeFrame($"echo:{arg}");
            return ValueTask.FromResult(new RpcResponseEnvelope
            {
                RequestId = req.RequestId,
                Status = RpcStatus.Ok,
                Payload = payload.Memory
            });
        });

        await server.StartAsync();
        var client = new RpcClientRuntime(clientTransport, serializer);
        await client.StartAsync();

        var response = await client.CallAsync(EchoMethod, "ping");

        Assert.Equal("echo:ping", response);
        Assert.Contains(loggerProvider.Entries, entry =>
            entry.Category == "Lakona.Rpc.Server.Request" &&
            entry.Level == LogLevel.Debug &&
            entry.Message.Contains("RPC request received", StringComparison.Ordinal) &&
            entry.Message.Contains("received 1", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(loggerProvider.Entries, entry =>
            entry.Category == "Lakona.Rpc.Server.Request" &&
            entry.Level == LogLevel.Debug &&
            entry.Message.Contains("RPC request completed", StringComparison.Ordinal) &&
            entry.Message.Contains("status Ok", StringComparison.OrdinalIgnoreCase));

        await client.DisposeAsync();
        await server.StopAsync();
        await clientTransport.DisposeAsync();
    }

    [Fact]
    public async Task Server_not_found_writes_warning_request_log()
    {
        LoopbackTransport.CreatePair(out var clientTransport, out var serverTransport);
        var serializer = new JsonRpcSerializer();
        var loggerProvider = new CategoryRecordingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Debug);
            logging.AddProvider(loggerProvider);
        });
        var server = new RpcSession(
            serverTransport,
            serializer,
            registry: null,
            contextId: Guid.NewGuid().ToString("N"),
            ownsTransport: false,
            requestLogger: loggerFactory.CreateLogger("Lakona.Rpc.Server.Request"));

        await server.StartAsync();
        await clientTransport.ConnectAsync();
        await clientTransport.SendFrameAsync(RpcEnvelopeCodec.EncodeRequest(new RpcRequestEnvelope
        {
            RequestId = 7,
            ServiceId = 99,
            MethodId = 99,
            Payload = ReadOnlyMemory<byte>.Empty
        }));

        using var response = await ReceiveResponseAsync(clientTransport);

        Assert.Equal(RpcStatus.NotFound, response.Status);
        Assert.Contains(loggerProvider.Entries, entry =>
            entry.Category == "Lakona.Rpc.Server.Request" &&
            entry.Level == LogLevel.Warning &&
            entry.Message.Contains("RPC request completed", StringComparison.Ordinal) &&
            entry.Message.Contains("status NotFound", StringComparison.OrdinalIgnoreCase));

        await server.StopAsync();
        await clientTransport.DisposeAsync();
    }

    [Fact]
    public async Task Server_notification_send_writes_debug_push_log()
    {
        LoopbackTransport.CreatePair(out var clientTransport, out var serverTransport);
        var serializer = new JsonRpcSerializer();
        var loggerProvider = new CategoryRecordingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Debug);
            logging.AddProvider(loggerProvider);
        });
        var server = new RpcSession(
            serverTransport,
            serializer,
            registry: null,
            contextId: Guid.NewGuid().ToString("N"),
            ownsTransport: false,
            requestLogger: loggerFactory.CreateLogger("Lakona.Rpc.Server.Request"));
        var client = new RpcClientRuntime(
            clientTransport,
            serializer,
            loggerFactory: loggerFactory);
        client.RegisterNotificationHandler(NotifyMethod, _ => default);

        await server.StartAsync();
        await client.StartAsync();
        await server.SendNotificationAsync(2, 2, "push");

        await Task.Delay(100);

        Assert.Contains(loggerProvider.Entries, entry =>
            entry.Category == "Lakona.Rpc.Server.Request" &&
            entry.Level == LogLevel.Debug &&
            entry.Message.Contains("RPC notification sent", StringComparison.Ordinal) &&
            entry.Message.Contains("service 2 method 2", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(loggerProvider.Entries, entry =>
            entry.Category == "Lakona.Rpc.Client.Request" &&
            entry.Level == LogLevel.Debug &&
            entry.Message.Contains("RPC notification received", StringComparison.Ordinal));

        await client.DisposeAsync();
        await server.StopAsync();
        await clientTransport.DisposeAsync();
    }

    [Fact]
    public async Task Client_successful_request_writes_debug_request_logs()
    {
        LoopbackTransport.CreatePair(out var clientTransport, out var serverTransport);
        var serializer = new JsonRpcSerializer();
        var loggerProvider = new CategoryRecordingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Debug);
            logging.AddProvider(loggerProvider);
        });
        var server = new RpcSession(serverTransport, serializer);
        server.Register(1, 1, (req, ct) =>
        {
            using var payload = serializer.SerializeFrame("ok");
            return ValueTask.FromResult(new RpcResponseEnvelope
            {
                RequestId = req.RequestId,
                Status = RpcStatus.Ok,
                Payload = payload.Memory
            });
        });

        await server.StartAsync();
        var client = new RpcClientRuntime(clientTransport, serializer, loggerFactory: loggerFactory);
        await client.StartAsync();

        await client.CallAsync(EchoMethod, "world");

        Assert.Contains(loggerProvider.Entries, entry =>
            entry.Category == "Lakona.Rpc.Client.Request" &&
            entry.Level == LogLevel.Debug &&
            entry.Message.Contains("RPC request sent", StringComparison.Ordinal));
        Assert.Contains(loggerProvider.Entries, entry =>
            entry.Category == "Lakona.Rpc.Client.Request" &&
            entry.Level == LogLevel.Debug &&
            entry.Message.Contains("RPC request completed", StringComparison.Ordinal) &&
            entry.Message.Contains("status Ok", StringComparison.OrdinalIgnoreCase));

        await client.DisposeAsync();
        await server.StopAsync();
        await clientTransport.DisposeAsync();
    }

    private static async Task<RpcResponseFrame> ReceiveResponseAsync(ITransport transport)
    {
        using var frame = await transport.ReceiveFrameAsync();
        return RpcEnvelopeCodec.DecodeResponse(frame);
    }

    private sealed class CategoryRecordingLoggerProvider : ILoggerProvider
    {
        public List<LogEntry> Entries { get; } = [];

        public ILogger CreateLogger(string categoryName)
        {
            return new CategoryRecordingLogger(categoryName, Entries);
        }

        public void Dispose()
        {
        }

        public sealed record LogEntry(string Category, LogLevel Level, string Message, Exception? Exception);

        private sealed class CategoryRecordingLogger(string category, List<LogEntry> entries) : ILogger
        {
            public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                entries.Add(new LogEntry(category, logLevel, formatter(state, exception), exception));
            }
        }

        private sealed class NullScope : IDisposable
        {
            public static NullScope Instance { get; } = new();

            public void Dispose()
            {
            }
        }
    }
}
