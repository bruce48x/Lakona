using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Lakona.Game.Cluster;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.Dispatch;
using Xunit;

namespace Lakona.Game.Server.Tests;

public sealed class HotfixFeatureMessageHandlerTests
{
    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-1")]
    public async Task HandleAsyncRejectsInvalidTypedCommandKind(string kind)
    {
        var invoker = new RecordingCommandInvoker();
        var handler = new HotfixFeatureMessageHandler(
            new FixedAccessor(invoker),
            new JsonFeatureSerializer());

        var reply = await handler.HandleAsync(
            NewRequest(kind: kind),
            TestContext.Current.CancellationToken);

        Assert.Equal(ClusterSendStatus.Rejected, reply.Status);
        Assert.Empty(invoker.Requests);
    }

    [Fact]
    public async Task HandleAsyncDispatchesTypedCommandAndSerializesReply()
    {
        var serializer = new JsonFeatureSerializer();
        var invoker = new RecordingCommandInvoker(new TestReply("accepted"));
        var handler = new HotfixFeatureMessageHandler(new FixedAccessor(invoker), serializer);

        var reply = await handler.HandleAsync(
            NewRequest(payload: serializer.Serialize(new TestCommand("room-1"))),
            TestContext.Current.CancellationToken);

        Assert.Equal(ClusterSendStatus.Accepted, reply.Status);
        var request = Assert.Single(invoker.Requests);
        Assert.Equal("battle-runtime", invoker.FeatureName);
        Assert.Equal(FeatureCommandId.From(17), invoker.CommandId);
        Assert.Equal("room-1", request.RoomId);
        Assert.Equal("accepted", serializer.Deserialize<TestReply>(reply.Payload).Status);
    }

    [Fact]
    public async Task HandleAsyncHoldsRuntimeLeaseUntilCommandInvocationCompletes()
    {
        var serializer = new JsonFeatureSerializer();
        var invoker = new BlockingCommandInvoker();
        var accessor = new FixedAccessor(invoker);
        var handler = new HotfixFeatureMessageHandler(accessor, serializer);

        var handling = handler.HandleAsync(
            NewRequest(payload: serializer.Serialize(new TestCommand("room-1"))),
            TestContext.Current.CancellationToken).AsTask();
        await invoker.Invoked.Task.WaitAsync(TestContext.Current.CancellationToken);

        accessor.Current.Retire();
        Assert.False(accessor.Provider.Disposed);

        invoker.Release.SetResult();
        var reply = await handling;

        Assert.Equal(ClusterSendStatus.Accepted, reply.Status);
        Assert.True(accessor.Provider.Disposed);
    }

    [Fact]
    public async Task HandleAsyncReturnsFeatureNotFoundForUnknownCommand()
    {
        var handler = new HotfixFeatureMessageHandler(
            new FixedAccessor(new MissingCommandInvoker()),
            new JsonFeatureSerializer());

        var reply = await handler.HandleAsync(
            NewRequest(),
            TestContext.Current.CancellationToken);

        Assert.Equal(ClusterSendStatus.FeatureNotFound, reply.Status);
    }

    [Fact]
    public async Task HandleAsyncMapsDeserializerFailure()
    {
        var invoker = new RecordingCommandInvoker();
        var handler = new HotfixFeatureMessageHandler(
            new FixedAccessor(invoker),
            new ThrowingDeserializeSerializer());

        var reply = await handler.HandleAsync(
            NewRequest(),
            TestContext.Current.CancellationToken);

        Assert.Equal(ClusterSendStatus.DeserializationFailed, reply.Status);
        Assert.Contains("test deserializer exploded", reply.ErrorMessage, StringComparison.Ordinal);
        Assert.Empty(invoker.Requests);
    }

    [Fact]
    public async Task HandleAsyncMapsSerializerFailureWithOriginalMessage()
    {
        var invoker = new RecordingCommandInvoker(new TestReply("accepted"));
        var handler = new HotfixFeatureMessageHandler(
            new FixedAccessor(invoker),
            new ThrowingReplySerializeSerializer());

        var reply = await handler.HandleAsync(
            NewRequest(),
            TestContext.Current.CancellationToken);

        Assert.Equal(ClusterSendStatus.SerializationFailed, reply.Status);
        Assert.Contains("test serializer exploded", reply.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HandleAsyncReturnsHandlerUnavailableWhenSerializerMissing()
    {
        var handler = new HotfixFeatureMessageHandler(
            new FixedAccessor(new RecordingCommandInvoker()));

        var reply = await handler.HandleAsync(
            NewRequest(),
            TestContext.Current.CancellationToken);

        Assert.Equal(ClusterSendStatus.HandlerUnavailable, reply.Status);
    }

    [Fact]
    public async Task HandleAsyncReturnsExpiredBeforeCommandDispatch()
    {
        var invoker = new RecordingCommandInvoker();
        var handler = new HotfixFeatureMessageHandler(
            new FixedAccessor(invoker),
            new JsonFeatureSerializer());

        var reply = await handler.HandleAsync(
            NewExpiredRequest(),
            TestContext.Current.CancellationToken);

        Assert.Equal(ClusterSendStatus.Expired, reply.Status);
        Assert.Empty(invoker.Requests);
    }

    [Fact]
    public async Task HandleAsyncPropagatesCallerCancellationBeforeDispatch()
    {
        using var cancel = new CancellationTokenSource();
        await cancel.CancelAsync();
        var invoker = new RecordingCommandInvoker();
        var handler = new HotfixFeatureMessageHandler(
            new FixedAccessor(invoker),
            new JsonFeatureSerializer());

        await Assert.ThrowsAsync<OperationCanceledException>(() => handler
            .HandleAsync(NewRequest(), cancel.Token)
            .AsTask());
        Assert.Empty(invoker.Requests);
    }

    [Fact]
    public async Task HandleAsyncPropagatesCommandCancellationWhenCallerTokenIsCanceledDuringDispatch()
    {
        using var cancel = new CancellationTokenSource();
        var handler = new HotfixFeatureMessageHandler(
            new FixedAccessor(new CancelingCommandInvoker(cancel)),
            new JsonFeatureSerializer());

        await Assert.ThrowsAsync<OperationCanceledException>(() => handler
            .HandleAsync(NewRequest(), cancel.Token)
            .AsTask());
    }

    [Fact]
    public async Task HandleAsyncMapsDetachedOperationCanceledExceptionToFailed()
    {
        var handler = new HotfixFeatureMessageHandler(
            new FixedAccessor(new DetachedCancellationCommandInvoker()),
            new JsonFeatureSerializer());

        var reply = await handler.HandleAsync(
            NewRequest(),
            TestContext.Current.CancellationToken);

        Assert.Equal(ClusterSendStatus.Failed, reply.Status);
    }

    private static FeatureMessageRequest NewRequest(
        string kind = "17",
        ReadOnlyMemory<byte> payload = default)
    {
        return new FeatureMessageRequest(
            new FeatureName("battle-runtime"),
            kind,
            payload.IsEmpty ? new JsonFeatureSerializer().Serialize(new TestCommand("room-1")) : payload,
            DateTimeOffset.UtcNow.AddMinutes(1),
            new NodeId("data-1"),
            "corr-1");
    }

    private static FeatureMessageRequest NewExpiredRequest()
    {
        return new FeatureMessageRequest(
            new FeatureName("battle-runtime"),
            "17",
            new JsonFeatureSerializer().Serialize(new TestCommand("room-1")),
            DateTimeOffset.UtcNow.AddMinutes(-1),
            new NodeId("data-1"),
            "corr-1");
    }

    private sealed class JsonFeatureSerializer : IFeatureMessageSerializer
    {
        public ReadOnlyMemory<byte> Serialize<T>(T value)
        {
            return JsonSerializer.SerializeToUtf8Bytes(value);
        }

        public T Deserialize<T>(ReadOnlyMemory<byte> payload)
        {
            return JsonSerializer.Deserialize<T>(payload.Span)!;
        }
    }

    private sealed class ThrowingDeserializeSerializer : IFeatureMessageSerializer
    {
        public ReadOnlyMemory<byte> Serialize<T>(T value)
        {
            return JsonSerializer.SerializeToUtf8Bytes(value);
        }

        public T Deserialize<T>(ReadOnlyMemory<byte> payload)
        {
            throw new InvalidOperationException("test deserializer exploded");
        }
    }

    private sealed class ThrowingReplySerializeSerializer : IFeatureMessageSerializer
    {
        public ReadOnlyMemory<byte> Serialize<T>(T value)
        {
            if (typeof(T) == typeof(TestReply))
            {
                throw new InvalidOperationException("test serializer exploded");
            }

            return JsonSerializer.SerializeToUtf8Bytes(value);
        }

        public T Deserialize<T>(ReadOnlyMemory<byte> payload)
        {
            return JsonSerializer.Deserialize<T>(payload.Span)!;
        }
    }

    private sealed class FixedAccessor : IHotfixRuntimeAccessor
    {
        private readonly ServiceProvider _provider;

        public FixedAccessor(IHotfixFeatureCommandInvoker featureCommands)
        {
            _provider = new ServiceCollection().BuildServiceProvider();
            Provider = new TrackingServiceProvider(_provider);
            Current = new HotfixRuntimeSnapshot(
                new HotfixServiceInvoker(),
                featureCommands,
                Provider,
                onRetired: Provider.Dispose);
        }

        public HotfixRuntimeSnapshot Current { get; }

        public TrackingServiceProvider Provider { get; }
    }

    private sealed class TrackingServiceProvider(IServiceProvider inner) : IServiceProvider, IDisposable
    {
        public bool Disposed { get; private set; }

        public object? GetService(Type serviceType)
        {
            return inner.GetService(serviceType);
        }

        public void Dispose()
        {
            Disposed = true;
            (inner as IDisposable)?.Dispose();
        }
    }

    private sealed class RecordingCommandInvoker : IHotfixFeatureCommandInvoker
    {
        private static readonly HotfixFeatureCommandDescriptor TestDescriptor = new(
            "battle-runtime:17",
            "battle-runtime",
            FeatureCommandId.From(17),
            typeof(TestCommand),
            typeof(TestReply));

        private readonly object? _reply;

        public RecordingCommandInvoker(object? reply = null)
        {
            _reply = reply ?? new TestReply("ok");
        }

        public string? FeatureName { get; private set; }

        public FeatureCommandId CommandId { get; private set; }

        public List<TestCommand> Requests { get; } = [];

        public bool TryResolve(
            string featureName,
            FeatureCommandId commandId,
            out HotfixFeatureCommandDescriptor descriptor)
        {
            FeatureName = featureName;
            CommandId = commandId;
            descriptor = TestDescriptor;
            return commandId == FeatureCommandId.From(17);
        }

        public ValueTask<object?> InvokeAsync(
            HotfixFeatureCommandDescriptor descriptor,
            object? request,
            FeatureMessageRequest message,
            IServiceProvider services,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(Assert.IsType<TestCommand>(request));
            return new ValueTask<object?>(_reply);
        }
    }

    private sealed class MissingCommandInvoker : IHotfixFeatureCommandInvoker
    {
        public bool TryResolve(
            string featureName,
            FeatureCommandId commandId,
            out HotfixFeatureCommandDescriptor descriptor)
        {
            descriptor = default!;
            return false;
        }

        public ValueTask<object?> InvokeAsync(
            HotfixFeatureCommandDescriptor descriptor,
            object? request,
            FeatureMessageRequest message,
            IServiceProvider services,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class CancelingCommandInvoker : IHotfixFeatureCommandInvoker
    {
        private static readonly HotfixFeatureCommandDescriptor TestDescriptor = new(
            "battle-runtime:17",
            "battle-runtime",
            FeatureCommandId.From(17),
            typeof(TestCommand),
            typeof(TestReply));

        private readonly CancellationTokenSource _cancel;

        public CancelingCommandInvoker(CancellationTokenSource cancel)
        {
            _cancel = cancel;
        }

        public bool TryResolve(
            string featureName,
            FeatureCommandId commandId,
            out HotfixFeatureCommandDescriptor descriptor)
        {
            descriptor = TestDescriptor;
            return true;
        }

        public async ValueTask<object?> InvokeAsync(
            HotfixFeatureCommandDescriptor descriptor,
            object? request,
            FeatureMessageRequest message,
            IServiceProvider services,
            CancellationToken cancellationToken = default)
        {
            await _cancel.CancelAsync();
            cancellationToken.ThrowIfCancellationRequested();
            return new TestReply("never");
        }
    }

    private sealed class BlockingCommandInvoker : IHotfixFeatureCommandInvoker
    {
        private static readonly HotfixFeatureCommandDescriptor TestDescriptor = new(
            "battle-runtime:17",
            "battle-runtime",
            FeatureCommandId.From(17),
            typeof(TestCommand),
            typeof(TestReply));

        public TaskCompletionSource Invoked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool TryResolve(
            string featureName,
            FeatureCommandId commandId,
            out HotfixFeatureCommandDescriptor descriptor)
        {
            descriptor = TestDescriptor;
            return true;
        }

        public async ValueTask<object?> InvokeAsync(
            HotfixFeatureCommandDescriptor descriptor,
            object? request,
            FeatureMessageRequest message,
            IServiceProvider services,
            CancellationToken cancellationToken = default)
        {
            Invoked.SetResult();
            await Release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            return new TestReply("accepted");
        }
    }

    private sealed class DetachedCancellationCommandInvoker : IHotfixFeatureCommandInvoker
    {
        private static readonly HotfixFeatureCommandDescriptor TestDescriptor = new(
            "battle-runtime:17",
            "battle-runtime",
            FeatureCommandId.From(17),
            typeof(TestCommand),
            typeof(TestReply));

        public bool TryResolve(
            string featureName,
            FeatureCommandId commandId,
            out HotfixFeatureCommandDescriptor descriptor)
        {
            descriptor = TestDescriptor;
            return true;
        }

        public ValueTask<object?> InvokeAsync(
            HotfixFeatureCommandDescriptor descriptor,
            object? request,
            FeatureMessageRequest message,
            IServiceProvider services,
            CancellationToken cancellationToken = default)
        {
            throw new OperationCanceledException();
        }
    }

    [FeatureCommand(17)]
    private sealed record TestCommand(string RoomId);

    private sealed record TestReply(string Status);
}
