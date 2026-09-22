using System.Diagnostics;
using Lakona.Rpc.Core;
using Lakona.Rpc.Server;
using Lakona.Rpc.Serializer.Json;
using Microsoft.Extensions.Logging;

namespace Lakona.Rpc.Tests;

public sealed class RpcResponsePipelineTests
{
    [Theory]
    [InlineData(RpcStatus.Ok, null)]
    [InlineData(RpcStatus.BadRequest, "invalid input")]
    [InlineData(RpcStatus.NotFound, "")]
    public async Task Raw_response_wire_and_completion_metadata_agree(RpcStatus status, string? error)
    {
        await using var transport = new RecordingTransport();
        var registry = new RpcServiceRegistry();
        registry.RegisterRaw(1, 1, (_, _, _, _) =>
            ValueTask.FromResult(new RpcRawResult(status, new byte[] { 4, 5 }, error)));
        await using var session = CreateSession(transport, registry);
        using var connection = new RpcConnectionChannel(transport, RpcKeepAliveOptions.Disabled);
        var logger = new RecordingLogger();
        var dispatcher = new ServerRequestDispatcher(registry, null, connection, logger);
        using var frame = RpcEnvelopeCodec.EncodeRequest(new RpcRequestEnvelope
        {
            RequestId = 7, ServiceId = 1, MethodId = 1
        });
        using var request = RpcEnvelopeCodec.DecodeRequest(frame);

        Assert.Equal(status, await dispatcher.DispatchAsync(session, request, default, Stopwatch.GetTimestamp()));

        using var sent = TransportFrame.CopyOf(Assert.Single(transport.Sent));
        using var response = RpcEnvelopeCodec.DecodeResponse(sent);
        Assert.Equal((uint)7, response.RequestId);
        Assert.Equal(status, response.Status);
        Assert.Equal(new byte[] { 4, 5 }, response.Payload.ToArray());
        Assert.Equal(string.IsNullOrEmpty(error) ? null : error, response.ErrorMessage);
        var completed = Assert.Single(logger.Entries, e => e.ContainsKey("Status"));
        Assert.Equal(response.Status, completed["Status"]);
        if (status != RpcStatus.Ok)
            Assert.Equal(response.ErrorMessage, completed["ErrorMessage"]);
    }

    [Theory]
    [InlineData("success")]
    [InlineData("publication failure")]
    [InlineData("cancellation")]
    [InlineData("send failure")]
    public async Task Publication_and_send_paths_release_the_response(string outcome)
    {
        await using var transport = new RecordingTransport { FailSend = outcome == "send failure" };
        var registry = new RpcServiceRegistry();
        using var cancellation = new CancellationTokenSource();
        var publishing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        RpcServerResponse ownedResponse = default;
        registry.Register(1, 1, (_, request, _) =>
        {
            RpcResponsePublicationScope.Register(async (_, ct) =>
            {
                publishing.SetResult();
                await release.Task.WaitAsync(ct);
                if (outcome == "publication failure") throw new InvalidOperationException("private detail");
            });
            ownedResponse = RpcServerResponse.Encode(request.RequestId, RpcStatus.Ok, new byte[] { 1 });
            return ValueTask.FromResult(ownedResponse);
        });
        await using var session = CreateSession(transport, registry);
        using var connection = new RpcConnectionChannel(transport, RpcKeepAliveOptions.Disabled);
        var logger = new RecordingLogger();
        var dispatcher = new ServerRequestDispatcher(registry, null, connection, logger);
        using var frame = RpcEnvelopeCodec.EncodeRequest(new RpcRequestEnvelope
        {
            RequestId = 7, ServiceId = 1, MethodId = 1
        });
        using var request = RpcEnvelopeCodec.DecodeRequest(frame);
        var dispatch = dispatcher.DispatchAsync(session, request, cancellation.Token, Stopwatch.GetTimestamp());
        await publishing.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(dispatch.IsCompleted);
        Assert.Empty(transport.Sent);
        Assert.False(ownedResponse.Memory.IsEmpty);

        if (outcome == "cancellation") cancellation.Cancel();
        release.SetResult();
        if (outcome == "cancellation")
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => dispatch.WaitAsync(TimeSpan.FromSeconds(5)));
        else if (outcome == "send failure")
            await Assert.ThrowsAsync<IOException>(() => dispatch.WaitAsync(TimeSpan.FromSeconds(5)));
        else
        {
            var expected = outcome == "success" ? RpcStatus.Ok : RpcStatus.InternalError;
            Assert.Equal(expected, await dispatch.WaitAsync(TimeSpan.FromSeconds(5)));
            using var sent = TransportFrame.CopyOf(Assert.Single(transport.Sent));
            using var response = RpcEnvelopeCodec.DecodeResponse(sent);
            Assert.Equal(expected, response.Status);
            if (expected == RpcStatus.InternalError)
                Assert.Equal("RPC server failed to process the request.", response.ErrorMessage);
        }

        Assert.Throws<ObjectDisposedException>(() => ownedResponse.Memory);
        Assert.Equal(outcome == "cancellation" ? 0 : 1, transport.Sent.Count);
        Assert.Equal(outcome is "cancellation" or "send failure" ? 0 : 1,
            logger.Entries.Count(e => e.ContainsKey("Status")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Uninitialized_response_is_sanitized_and_send_failure_is_not_retried(bool failSend)
    {
        await using var transport = new RecordingTransport { FailSend = failSend };
        var registry = new RpcServiceRegistry();
        registry.Register(1, 1, (_, _, _) => ValueTask.FromResult(default(RpcServerResponse)));
        await using var session = CreateSession(transport, registry);
        using var connection = new RpcConnectionChannel(transport, RpcKeepAliveOptions.Disabled);
        var dispatcher = new ServerRequestDispatcher(registry, null, connection, new RecordingLogger());
        using var frame = RpcEnvelopeCodec.EncodeRequest(new RpcRequestEnvelope
        {
            RequestId = 7, ServiceId = 1, MethodId = 1
        });
        using var request = RpcEnvelopeCodec.DecodeRequest(frame);

        var dispatch = dispatcher.DispatchAsync(session, request, default, Stopwatch.GetTimestamp());
        if (failSend)
            await Assert.ThrowsAsync<IOException>(() => dispatch);
        else
            Assert.Equal(RpcStatus.InternalError, await dispatch);

        using var sent = TransportFrame.CopyOf(Assert.Single(transport.Sent));
        using var response = RpcEnvelopeCodec.DecodeResponse(sent);
        Assert.Equal(RpcStatus.InternalError, response.Status);
        Assert.Equal("RPC server failed to process the request.", response.ErrorMessage);
    }

    private static RpcSession CreateSession(ITransport transport, RpcServiceRegistry registry) =>
        new(transport, new JsonRpcSerializer(), registry, "response-pipeline", ownsTransport: false);

    private sealed class RecordingTransport : ITransport
    {
        public bool FailSend { get; init; }
        public List<byte[]> Sent { get; } = [];
        public bool IsConnected => true;
        public ValueTask ConnectAsync(CancellationToken ct = default) => default;
        public ValueTask<TransportFrame> ReceiveFrameAsync(CancellationToken ct = default) => new(TransportFrame.Empty);
        public ValueTask SendFrameAsync(ReadOnlyMemory<byte> frame, CancellationToken ct = default)
        {
            Sent.Add(frame.ToArray());
            if (FailSend) throw new IOException("send failed");
            return default;
        }
        public ValueTask DisposeAsync() => default;
    }

    private sealed class RecordingLogger : ILogger
    {
        public List<Dictionary<string, object?>> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add(((IEnumerable<KeyValuePair<string, object?>>)state!).ToDictionary());
    }
}
