using System.Diagnostics;
using System.Threading.Channels;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Health;
using Lakona.Game.Server.Hosting;
using Lakona.Rpc.Core;
using Lakona.Rpc.Serializer.Json;
using Lakona.Rpc.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Lakona.Game.Server.Tests.Hosting;

public sealed class RpcServersHostedServiceTests
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(4);

    [Theory]
    [InlineData(false, "io")]
    [InlineData(true, "io")]
    [InlineData(true, "closed")]
    [InlineData(false, "canceled")]
    public async Task Runtime_endpoint_exit_closes_admission_and_preserves_the_cause_after_ordered_shutdown(
        bool includeHealthyEndpoint, string failure)
    {
        Exception primary = failure switch
        {
            "closed" => new ChannelClosedException(),
            "canceled" => new OperationCanceledException("Acceptor canceled before shutdown."),
            _ => new IOException("Injected listener failure after startup.")
        };
        var releaseListener = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failed = new ControlledAcceptor("test://failed") { ReleaseDisposal = releaseListener };
        var healthy = new ControlledAcceptor("test://healthy");
        var membership = new PausedShutdownParticipant { FailCleanup = includeHealthyEndpoint && failure == "io" };
        var endpoints = new List<IRpcServerConfigurator> { new ImmediateConfigurator("failed", failed) };
        if (includeHealthyEndpoint) endpoints.Add(new ImmediateConfigurator("healthy", healthy));
        var host = CreateHost(endpoints, membership);
        var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
        var admission = host.Services.GetRequiredService<DistributedWorkAdmissionGate>();
        var readiness = host.Services.GetRequiredService<LakonaServerReadinessState>();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopping = new TaskCompletionSource<(bool Open, string? Diagnostic)>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var onStart = lifetime.ApplicationStarted.Register(() => started.TrySetResult());
        using var onStop = lifetime.ApplicationStopping.Register(() =>
            stopping.TrySetResult((admission.IsOpen, readiness.Diagnostics.SingleOrDefault()?.Code)));
        var run = Record.ExceptionAsync(() => LakonaGameServerRunner.RunAsync(host)).AsTask();
        var ct = TestContext.Current.CancellationToken;
        try
        {
            await started.Task.WaitAsync(Guard, ct);
            Assert.True(admission.IsOpen);
            Assert.Empty(readiness.Diagnostics);
            failed.Fail.TrySetException(primary);

            var atStop = await stopping.Task.WaitAsync(Guard, ct);
            Assert.False(atStop.Open);
            Assert.Equal(LakonaServerReadinessState.ListenerFailedCode, atStop.Diagnostic);
            await membership.Entered.Task.WaitAsync(Guard, ct);
            await failed.Disposing.Task.WaitAsync(Guard, ct);
            Assert.False(run.IsCompleted);
            if (includeHealthyEndpoint) Assert.Equal(0, healthy.DisposeCount);
        }
        finally
        {
            lifetime.StopApplication();
            releaseListener.TrySetResult();
            membership.Release.TrySetResult();
            await run.WaitAsync(Guard, ct);
        }

        Assert.Same(primary, await run);
        Assert.Equal(1, membership.Stops);
        Assert.Equal(1, failed.DisposeCount);
        Assert.Equal(includeHealthyEndpoint ? 1 : 0, healthy.DisposeCount);
    }

    [Fact]
    public async Task Listener_failure_during_a_later_startup_stage_survives_node_rollback()
    {
        var acceptor = new ControlledAcceptor("test://started");
        var releaseStartup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var membership = new PausedShutdownParticipant { ReleaseStartup = releaseStartup };
        membership.Release.TrySetResult();
        var host = CreateHost([new ImmediateConfigurator("started", acceptor)], membership);
        var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
        var admission = host.Services.GetRequiredService<DistributedWorkAdmissionGate>();
        var readiness = host.Services.GetRequiredService<LakonaServerReadinessState>();
        var stopping = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var onStop = lifetime.ApplicationStopping.Register(() => stopping.TrySetResult());
        var run = Record.ExceptionAsync(() => LakonaGameServerRunner.RunAsync(host)).AsTask();
        var ct = TestContext.Current.CancellationToken;
        var primary = new IOException("Listener failed while membership was starting.");
        try
        {
            await membership.Starting.Task.WaitAsync(Guard, ct);
            Assert.False(lifetime.ApplicationStarted.IsCancellationRequested);
            acceptor.Fail.TrySetException(primary);
            await stopping.Task.WaitAsync(Guard, ct);
            // Even a late startup attempt to open admission cannot undo the listener failure.
            Assert.Throws<InvalidOperationException>(admission.Open);
            Assert.False(admission.IsOpen);
            Assert.Equal(LakonaServerReadinessState.ListenerFailedCode, Assert.Single(readiness.Diagnostics).Code);
        }
        finally
        {
            lifetime.StopApplication();
            releaseStartup.TrySetException(new InvalidOperationException("Membership failed during startup rollback."));
            await run.WaitAsync(Guard, ct);
        }
        Assert.Same(primary, await run);
        Assert.Equal(1, membership.Stops);
        Assert.Equal(1, acceptor.DisposeCount);
    }

    private sealed class ControlledAcceptor(string address) : IRpcConnectionAcceptor
    {
        public string ListenAddress => address;
        public TaskCompletionSource<RpcAcceptedConnection> Fail { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Disposing { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource? ReleaseDisposal { get; init; }
        public int DisposeCount { get; private set; }
        public ValueTask<RpcAcceptedConnection> AcceptAsync(CancellationToken ct = default) => new(Fail.Task.WaitAsync(ct));
        public async ValueTask DisposeAsync()
        {
            DisposeCount++;
            Disposing.TrySetResult();
            if (ReleaseDisposal is not null) await ReleaseDisposal.Task;
        }
    }

    private sealed class PausedShutdownParticipant : ILakonaNodeLifecycleParticipant
    {
        public string Name => "controlled membership";
        public LakonaNodeLifecycleStage Stage => LakonaNodeLifecycleStage.Membership;
        public TaskCompletionSource Starting { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource? ReleaseStartup { get; init; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Stops { get; private set; }
        public bool FailCleanup { get; init; }
        public Task StartAsync(CancellationToken cancellationToken)
        {
            Starting.TrySetResult();
            return ReleaseStartup?.Task ?? Task.CompletedTask;
        }
        public async Task StopAsync(CancellationToken cancellationToken)
        {
            Stops++;
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            if (FailCleanup) throw new IOException("Secondary membership cleanup failure.");
        }
    }

    private static IHost CreateHost(IEnumerable<IRpcServerConfigurator> endpoints, PausedShutdownParticipant? membership = null)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        foreach (var endpoint in endpoints) builder.Services.AddSingleton(endpoint);
        builder.Services.AddSingleton(new LakonaGameRuntimeOptions());
        builder.Services.AddSingleton<LakonaServerReadinessState>();
        builder.Services.AddSingleton<DistributedWorkAdmissionGate>();
        builder.Services.AddSingleton<RpcServersHostedService>();
        builder.Services.AddSingleton<ILakonaNodeLifecycleParticipant, RpcServersLifecycleParticipant>();
        if (membership is not null) builder.Services.AddSingleton<ILakonaNodeLifecycleParticipant>(membership);
        builder.Services.AddSingleton<LakonaNodeLifecycle>();
        builder.Services.AddHostedService<LakonaNodeHostedService>();
        builder.Services.AddHostedService<LakonaServerStartupHostedService>();
        return builder.Build();
    }

    [Fact]
    public async Task StartAsync_waits_until_every_rpc_acceptor_is_listening()
    {
        var first = new DelayedConfigurator("first");
        var second = new DelayedConfigurator("second");
        using var host = CreateHost([first, second]);
        var hosted = host.Services.GetRequiredService<RpcServersHostedService>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var start = hosted.StartAsync(cts.Token);
        await Task.Yield();
        Assert.False(start.IsCompleted);

        first.Release("test://first");
        await Task.Yield();
        Assert.False(start.IsCompleted);

        second.Release("test://second");
        await start;
        await hosted.StopAsync(CancellationToken.None);
        Assert.DoesNotContain(host.Services.GetRequiredService<LakonaServerReadinessState>().Diagnostics,
            diagnostic => diagnostic.Code == LakonaServerReadinessState.ListenerFailedCode);
    }

    [Fact]
    public async Task StartAsync_propagates_acceptor_creation_failure()
    {
        using var host = CreateHost([new FailingConfigurator()]);
        var hosted = host.Services.GetRequiredService<RpcServersHostedService>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            hosted.StartAsync(TestContext.Current.CancellationToken));

        Assert.Equal("bind failed", exception.Message);
    }

    [Fact]
    public async Task StartAsync_stops_listeners_when_another_acceptor_fails()
    {
        var listening = new TrackingConnectionAcceptor("test://listening");
        using var host = CreateHost([new ImmediateConfigurator("listening", listening), new FailingConfigurator()]);
        var hosted = host.Services.GetRequiredService<RpcServersHostedService>();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            hosted.StartAsync(TestContext.Current.CancellationToken));

        try
        {
            Assert.True(listening.Disposed.IsCompleted);
        }
        finally
        {
            await hosted.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task StartAsync_completes_when_no_rpc_configurators_exist()
    {
        using var host = CreateHost([]);
        var hosted = host.Services.GetRequiredService<RpcServersHostedService>();

        await hosted.StartAsync(TestContext.Current.CancellationToken);
        await hosted.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StartAsync_cancels_without_hanging_before_listener_readiness()
    {
        var configurator = new DelayedConfigurator("delayed");
        using var host = CreateHost([configurator]);
        var hosted = host.Services.GetRequiredService<RpcServersHostedService>();
        using var cts = new CancellationTokenSource();

        var start = hosted.StartAsync(cts.Token);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => start);
        await hosted.StopAsync(CancellationToken.None);
    }

    private sealed class DelayedConfigurator(string transport) : IRpcServerConfigurator
    {
        private readonly TaskCompletionSource<IRpcConnectionAcceptor> _acceptor = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public string Transport { get; } = transport;

        public void Configure(LakonaGameServerRpcContext context)
        {
            context.Builder
                .UseSerializer(new JsonRpcSerializer())
                .ConfigureServices(_ => { })
                .UseAcceptor(ct => new ValueTask<IRpcConnectionAcceptor>(
                    _acceptor.Task.WaitAsync(ct)));
        }

        public void Release(string listenAddress)
        {
            _acceptor.TrySetResult(new BlockingConnectionAcceptor(listenAddress));
        }
    }

    private sealed class FailingConfigurator : IRpcServerConfigurator
    {
        public string Transport => "failing";

        public void Configure(LakonaGameServerRpcContext context)
        {
            context.Builder
                .UseSerializer(new JsonRpcSerializer())
                .ConfigureServices(_ => { })
                .UseAcceptor(_ => throw new InvalidOperationException("bind failed"));
        }
    }

    private sealed class ImmediateConfigurator(
        string transport,
        IRpcConnectionAcceptor acceptor) : IRpcServerConfigurator
    {
        public string Transport { get; } = transport;

        public void Configure(LakonaGameServerRpcContext context)
        {
            context.Builder
                .UseSerializer(new JsonRpcSerializer())
                .ConfigureServices(_ => { })
                .UseAcceptor(acceptor);
        }
    }

    private sealed class BlockingConnectionAcceptor(string listenAddress) : IRpcConnectionAcceptor
    {
        public string ListenAddress { get; } = listenAddress;

        public async ValueTask<RpcAcceptedConnection> AcceptAsync(CancellationToken ct = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            throw new UnreachableException();
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TrackingConnectionAcceptor(string listenAddress) : IRpcConnectionAcceptor
    {
        private readonly TaskCompletionSource _disposed = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public string ListenAddress { get; } = listenAddress;

        public Task Disposed => _disposed.Task;

        public async ValueTask<RpcAcceptedConnection> AcceptAsync(CancellationToken ct = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            throw new UnreachableException();
        }

        public ValueTask DisposeAsync()
        {
            _disposed.TrySetResult();
            return ValueTask.CompletedTask;
        }
    }
}
