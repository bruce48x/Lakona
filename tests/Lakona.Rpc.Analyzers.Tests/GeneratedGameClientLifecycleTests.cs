using System.Reflection;
using Lakona.Game.Client;
using Lakona.Tests;
using Xunit;

namespace Lakona.Rpc.Analyzers.Tests;

public sealed class GeneratedGameClientLifecycleTests
{
    [Fact]
    public async Task Generated_facade_keeps_typed_api_and_callback_binding_across_recovery()
    {
        const string source = """
            using System;
            using System.Threading.Tasks;
            using System.Threading;
            using System.Threading.Channels;
            using Lakona.Game.Client;
            using Lakona.Rpc.Core;
            using Lakona.Tests;
            namespace Contracts
            {
                [RpcService(42, ApiGroup = "Test", ApiName = "Echo", NotificationContract = typeof(INotifications))]
                public interface IEchoService
                {
                    [RpcMethod(1)] ValueTask<TestNumber> EchoAsync(TestNumber value);
                }
                [RpcNotificationContract]
                public interface INotifications
                {
                    [RpcNotification(2)] void Notify(TestNumber value);
                }
                public sealed class Probe : INotifications, IAsyncDisposable
                {
                    private readonly Client.Generated.LakonaGameClient _client;
                    private Client.Generated.RpcApi _api;
                    public TaskCompletionSource<int> Notification = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
                    public SynchronizationContext CallbackContext;
                    public Channel<LakonaGameConnectionStateChange> Changes = Channel.CreateUnbounded<LakonaGameConnectionStateChange>();
                    public Probe(LakonaGameClientOptions options)
                    {
                        _client = new Client.Generated.LakonaGameClient(options, this);
                        _client.ConnectionStateChanged += OnStateChanged;
                    }
                    private void OnStateChanged(LakonaGameConnectionStateChange change) { Changes.Writer.TryWrite(change); }
                    public void Unsubscribe() { _client.ConnectionStateChanged -= OnStateChanged; }
                    public LakonaGameConnectionState State => _client.ConnectionState;
                    public bool Connected => _client.IsConnected;
                    public async Task Connect() { await _client.ConnectAsync(); _api = _client.Api; }
                    public async Task<int> Echo(int value)
                    {
                        if (!ReferenceEquals(_api, _client.Api)) throw new Exception("API instance changed.");
                        return (await _api.Test.Echo.EchoAsync(new TestNumber { Value = value })).Value;
                    }
                    public void Notify(TestNumber value) { CallbackContext = SynchronizationContext.Current; Notification.TrySetResult(value.Value); }
                    public ValueTask DisposeAsync() { return _client.DisposeAsync(); }
                }
            }
            """;
        var result = AnalyzerTestHelpers.RunGenerator(
            AnalyzerTestHelpers.CreateCompilation(source, "GeneratedLifecycle_" + Guid.NewGuid().ToString("N"),
                additionalReferences: new[] { Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(typeof(TestNumber).Assembly.Location) }),
            new Dictionary<string, string>
            {
                ["build_property.LakonaGameGenerateClient"] = "true",
                ["build_property.LakonaRpcGeneratedNamespace"] = "Client.Generated"
            }, out var compilation);
        Assert.Empty(result.Diagnostics);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        var assembly = Assembly.Load(image.ToArray());
        var probeType = assembly.GetType("Contracts.Probe")!;
        var first = new GameClientTestTransport();
        var second = new GameClientTestTransport();
        var transports = new Queue<GameClientTestTransport>(new[] { first, second });
        var scheduler = new ControlledRecoveryScheduler();
        var options = new LakonaGameClientOptions(() => transports.Dequeue(), new IntegerSerializer())
        {
            RecoveryScheduler = scheduler
        };
        await using var probe = (IAsyncDisposable)Activator.CreateInstance(probeType, options)!;
        AssertState(LakonaGameConnectionState.Created);
        var timeout = TimeSpan.FromSeconds(5);
        var context = new DispatchContext();
        var previousContext = SynchronizationContext.Current;
        Task connecting;
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            connecting = (Task)probeType.GetMethod("Connect")!.Invoke(probe, null)!;
        }
        finally { SynchronizationContext.SetSynchronizationContext(previousContext); }
        await connecting.WaitAsync(timeout);
        AssertState(LakonaGameConnectionState.Connected);
        Assert.Equal(7, await ((Task<int>)probeType.GetMethod("Echo")!.Invoke(probe, new object[] { 7 })!).WaitAsync(timeout));
        first.EstablishSession();
        await first.EstablishedAcknowledged.Task.WaitAsync(timeout);
        first.Disconnect();
        await scheduler.Waiting.Task.WaitAsync(timeout);
        AssertState(LakonaGameConnectionState.Reconnecting);
        scheduler.Step();
        await first.Disposed.Task.WaitAsync(timeout);
        AssertState(LakonaGameConnectionState.Connected);
        Assert.Equal(11, await ((Task<int>)probeType.GetMethod("Echo")!.Invoke(probe, new object[] { 11 })!).WaitAsync(timeout));
        second.Push(42, 2, BitConverter.GetBytes(19));
        var notification = (TaskCompletionSource<int>)probeType.GetField("Notification")!.GetValue(probe)!;
        Assert.Equal(19, await notification.Task.WaitAsync(timeout));
        Assert.Same(context, probeType.GetField("CallbackContext")!.GetValue(probe));
        await probe.DisposeAsync();
        AssertState(LakonaGameConnectionState.Disposed);
        var changes = (System.Threading.Channels.Channel<LakonaGameConnectionStateChange>)probeType.GetField("Changes")!.GetValue(probe)!;
        var expectedStates = new[] { LakonaGameConnectionState.Created, LakonaGameConnectionState.Connecting,
            LakonaGameConnectionState.Connected, LakonaGameConnectionState.Reconnecting,
            LakonaGameConnectionState.Connected, LakonaGameConnectionState.Disposed };
        for (var index = 1; index < expectedStates.Length; index++)
        {
            var change = await changes.Reader.ReadAsync().AsTask().WaitAsync(timeout);
            Assert.Equal(expectedStates[index - 1], change.PreviousState);
            Assert.Equal(expectedStates[index], change.CurrentState);
        }
        probeType.GetMethod("Unsubscribe")!.Invoke(probe, null);

        void AssertState(LakonaGameConnectionState expected)
        {
            Assert.Equal(expected, (LakonaGameConnectionState)probeType.GetProperty("State")!.GetValue(probe)!);
            Assert.Equal(expected == LakonaGameConnectionState.Connected, (bool)probeType.GetProperty("Connected")!.GetValue(probe)!);
        }
    }

    private sealed class DispatchContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var previous = Current;
                SetSynchronizationContext(this);
                try { callback(state); }
                finally { SetSynchronizationContext(previous); }
            });
        }
    }
}
