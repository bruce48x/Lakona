using System.Buffers;
using Lakona.Game.Server.Hosting;
using Lakona.Rpc.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lakona.Game.Server.Tests.Hosting;

public sealed class EndpointSerializerLifetimeTests
{
    [Fact]
    public async Task Async_only_serializer_requires_async_disposal_without_starting_sync_cleanup()
    {
        var owner = new LakonaEndpointSerializerOwner();
        var serializer = new AsyncSerializer();
        owner.Create("async", () => serializer);
        try
        {
            var error = Assert.Throws<InvalidOperationException>(owner.Dispose);
            Assert.Contains("DisposeAsync", error.Message);
            Assert.Equal(0, serializer.AsyncDisposals);
        }
        finally { await owner.DisposeAsync(); }
        owner.Dispose();
        Assert.Equal(1, serializer.AsyncDisposals);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Repeated_async_disposal_joins_one_cleanup_and_observes_its_result(bool fail)
    {
        var owner = new LakonaEndpointSerializerOwner();
        var serializer = new HeldAsyncSerializer { Failure = fail ? new InvalidOperationException("delayed cleanup failed") : null };
        owner.Create("held", () => serializer);
        var first = owner.DisposeAsync().AsTask();
        try
        {
            await serializer.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            var second = owner.DisposeAsync().AsTask();
            Assert.False(first.IsCompleted);
            Assert.False(second.IsCompleted);
            var factoryCalled = false;
            Assert.Throws<ObjectDisposedException>(() => owner.Create("late", () => { factoryCalled = true; return serializer; }));
            Assert.False(factoryCalled);
            serializer.ReleaseCleanup.TrySetResult();
            var firstError = await Record.ExceptionAsync(() => first);
            var secondError = await Record.ExceptionAsync(() => second);
            if (fail)
            {
                Assert.Contains(serializer.Failure!, Assert.IsType<AggregateException>(firstError).InnerExceptions);
                Assert.Same(firstError, secondError);
            }
            else { Assert.Null(firstError); Assert.Null(secondError); }
            Assert.Equal(1, serializer.AsyncDisposals);
        }
        finally
        {
            serializer.ReleaseCleanup.TrySetResult();
            await Record.ExceptionAsync(() => first);
        }
    }

    [Theory]
    [InlineData("sync", false)]
    [InlineData("sync", true)]
    [InlineData("dual", false)]
    [InlineData("dual", true)]
    [InlineData("async", true)]
    public async Task Host_owns_shared_factory_instance_and_disposes_it_once(string kind, bool asyncDispose)
    {
        TestSerializer serializer = kind switch
        {
            "sync" => new SyncSerializer(),
            "dual" => new DualSerializer(),
            _ => new AsyncSerializer()
        };
        var calls = 0;
        var services = new ServiceCollection()
            .AddLakonaEndpointSerializer("first", () => { calls++; return serializer; })
            .AddLakonaEndpointSerializer("alias", () => { calls++; return serializer; });
        var provider = services.BuildServiceProvider();
        try
        {
            Assert.Equal(0, calls);
            var registry = provider.GetRequiredService<LakonaEndpointRuntimeRegistry>();
            Assert.Same(serializer, registry.GetEndpointSerializer("FIRST"));
            Assert.Same(serializer, registry.GetEndpointSerializer(" alias "));
            Assert.Same(registry, provider.GetRequiredService<LakonaEndpointRuntimeRegistry>());
            Assert.Equal(2, calls);
            if (asyncDispose) await provider.DisposeAsync();
            else provider.Dispose();
            await provider.DisposeAsync();
            provider.Dispose();
            Assert.Equal(asyncDispose && kind != "sync" ? 1 : 0, serializer.AsyncDisposals);
            Assert.Equal(!asyncDispose || kind == "sync" ? 1 : 0, serializer.SyncDisposals);
        }
        finally { await provider.DisposeAsync(); }
    }

    [Fact]
    public async Task Distinct_instances_with_value_equality_are_each_released()
    {
        var first = new SyncSerializer();
        var second = new SyncSerializer();
        Assert.Equal(first, second);
        var provider = new ServiceCollection()
            .AddLakonaEndpointSerializer("first", () => first)
            .AddLakonaEndpointSerializer("second", () => second)
            .BuildServiceProvider();
        provider.GetRequiredService<LakonaEndpointRuntimeRegistry>();
        await provider.DisposeAsync();
        Assert.Equal(1, first.SyncDisposals);
        Assert.Equal(1, second.SyncDisposals);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_registry_construction_leaves_created_instances_owned_by_container(bool returnsNull)
    {
        var first = new SyncSerializer();
        var second = new AsyncSerializer();
        var failure = new InvalidOperationException("serializer factory failed");
        var provider = new ServiceCollection()
            .AddLakonaEndpointSerializer("first", () => first)
            .AddLakonaEndpointSerializer("second", () => second)
            .AddLakonaEndpointSerializer("broken", () => returnsNull ? null! : throw failure)
            .BuildServiceProvider();
        try
        {
            var error = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<LakonaEndpointRuntimeRegistry>());
            if (returnsNull) Assert.Contains("broken", error.Message);
            else Assert.Same(failure, error);
        }
        finally { await provider.DisposeAsync(); }
        Assert.Equal(1, first.SyncDisposals);
        Assert.Equal(1, second.AsyncDisposals);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Disposal_failures_do_not_skip_other_instances_and_are_all_reported(bool asyncDispose)
    {
        var first = new DualSerializer { Failure = new InvalidOperationException("first cleanup failed") };
        var second = new DualSerializer();
        var third = new DualSerializer { Failure = new InvalidOperationException("third cleanup failed") };
        var provider = new ServiceCollection()
            .AddLakonaEndpointSerializer("first", () => first)
            .AddLakonaEndpointSerializer("second", () => second)
            .AddLakonaEndpointSerializer("third", () => third)
            .BuildServiceProvider();
        provider.GetRequiredService<LakonaEndpointRuntimeRegistry>();
        var error = await Record.ExceptionAsync(async () =>
        {
            if (asyncDispose) await provider.DisposeAsync();
            else provider.Dispose();
        });
        var aggregate = Assert.IsType<AggregateException>(error);
        Assert.Equal(2, aggregate.Flatten().InnerExceptions.Count);
        Assert.Contains(first.Failure, aggregate.Flatten().InnerExceptions);
        Assert.Contains(third.Failure, aggregate.Flatten().InnerExceptions);
        await provider.DisposeAsync();
        provider.Dispose();
        Assert.All(new[] { first, second, third }, item =>
        {
            Assert.Equal(asyncDispose ? 1 : 0, item.AsyncDisposals);
            Assert.Equal(asyncDispose ? 0 : 1, item.SyncDisposals);
        });
    }

    internal abstract class TestSerializer : IRpcSerializer
    {
        public int SyncDisposals;
        public int AsyncDisposals;
        public Exception? Failure { get; init; }
        public void Serialize<T>(IBufferWriter<byte> output, T value) => throw new NotSupportedException();
        public T Deserialize<T>(ReadOnlySpan<byte> input) => throw new NotSupportedException();
        public T Deserialize<T>(ReadOnlyMemory<byte> input) => throw new NotSupportedException();
        public override bool Equals(object? obj) => obj is TestSerializer;
        public override int GetHashCode() => 0;
        protected void Release(bool asynchronous)
        {
            if (asynchronous) Interlocked.Increment(ref AsyncDisposals);
            else Interlocked.Increment(ref SyncDisposals);
            if (Failure is not null) throw Failure;
        }
    }

    internal sealed class SyncSerializer : TestSerializer, IDisposable
    {
        public void Dispose() => Release(false);
    }

    internal class AsyncSerializer : TestSerializer, IAsyncDisposable
    {
        public virtual ValueTask DisposeAsync() { Release(true); return default; }
    }

    internal sealed class DualSerializer : AsyncSerializer, IDisposable
    {
        public void Dispose() => Release(false);
    }

    private sealed class HeldAsyncSerializer : AsyncSerializer
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseCleanup { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask DisposeAsync()
        {
            Entered.TrySetResult();
            await ReleaseCleanup.Task;
            await base.DisposeAsync();
        }
    }
}
