using System.Reflection;
using Lakona.Game.Cluster.Membership;
using StackExchange.Redis;
using Xunit;

namespace Lakona.Game.Cluster.Tests;

public sealed class RedisMembershipConnectionTests
{
    [Fact]
    public async Task Canceling_one_waiter_preserves_the_shared_connection_attempt()
    {
        var connecting = new TaskCompletionSource<IConnectionMultiplexer>(TaskCreationOptions.RunContinuationsAsynchronously);
        var connection = ConnectionStub.Create();
        var attempts = 0;
        await using var table = new RedisMembershipTable(() =>
        {
            Interlocked.Increment(ref attempts);
            return connecting.Task;
        }, "lakona:{connection-test}:table");
        using var canceled = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var first = table.AllocateGenerationAsync("test", canceled.Token).AsTask();
        Task<MembershipTableGeneration>? second = null;
        try
        {
            await canceled.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
            second = table.AllocateGenerationAsync("test", TestContext.Current.CancellationToken).AsTask();
            Assert.Equal(1, attempts);
            connecting.TrySetResult(connection);
            Assert.Equal(1, (await second).Value);
        }
        finally
        {
            connecting.TrySetResult(connection);
            if (second is not null) _ = await Record.ExceptionAsync(() => second);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Disposal_joins_a_late_connection_and_rejects_new_work(bool cancelWaiter)
    {
        var connecting = new TaskCompletionSource<IConnectionMultiplexer>(TaskCreationOptions.RunContinuationsAsynchronously);
        var connection = ConnectionStub.Create();
        var stub = (ConnectionStub)connection;
        stub.HoldDisposal = true;
        var attempts = 0;
        var table = new RedisMembershipTable(() =>
        {
            Interlocked.Increment(ref attempts);
            return connecting.Task;
        }, "lakona:{connection-test}:table");
        using var canceled = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var operation = table.AllocateGenerationAsync("test", canceled.Token).AsTask();
        try
        {
            if (cancelWaiter)
            {
                await canceled.CancelAsync();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
            }
            var firstDisposal = table.DisposeAsync().AsTask();
            var secondDisposal = table.DisposeAsync().AsTask();
            Assert.False(firstDisposal.IsCompleted);
            Assert.False(secondDisposal.IsCompleted);
            await Assert.ThrowsAsync<ObjectDisposedException>(() =>
                table.AllocateGenerationAsync("test", TestContext.Current.CancellationToken).AsTask());
            Assert.Equal(1, attempts);

            connecting.TrySetResult(connection);
            await stub.DisposalStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
            Assert.False(firstDisposal.IsCompleted);
            if (!cancelWaiter)
                await Assert.ThrowsAsync<ObjectDisposedException>(() => operation);
            stub.ReleaseDisposal.TrySetResult();
            await Task.WhenAll(firstDisposal, secondDisposal).WaitAsync(TestContext.Current.CancellationToken);
            Assert.Equal(1, stub.DisposalCount);
            Assert.Equal(0, stub.DatabaseAccessCount);
        }
        finally
        {
            connecting.TrySetResult(connection);
            stub.ReleaseDisposal.TrySetResult();
            _ = await Record.ExceptionAsync(() => operation);
            await table.DisposeAsync();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_connection_can_be_retried_unless_disposal_has_started(bool dispose)
    {
        var connecting = new TaskCompletionSource<IConnectionMultiplexer>(TaskCreationOptions.RunContinuationsAsynchronously);
        var connection = ConnectionStub.Create();
        var attempts = 0;
        var table = new RedisMembershipTable(() =>
            Interlocked.Increment(ref attempts) == 1 ? connecting.Task : Task.FromResult(connection),
            "lakona:{connection-test}:table");
        var operation = table.AllocateGenerationAsync("test", TestContext.Current.CancellationToken).AsTask();
        try
        {
            var stopping = dispose ? table.DisposeAsync().AsTask() : null;
            connecting.TrySetException(new InvalidOperationException("Injected connection failure."));
            await Assert.ThrowsAsync<InvalidOperationException>(() => operation);
            if (stopping is not null)
            {
                await stopping.WaitAsync(TestContext.Current.CancellationToken);
                await Assert.ThrowsAsync<ObjectDisposedException>(() =>
                    table.AllocateGenerationAsync("test", TestContext.Current.CancellationToken).AsTask());
                Assert.Equal(1, attempts);
            }
            else
            {
                Assert.Equal(1, (await table.AllocateGenerationAsync("test", TestContext.Current.CancellationToken)).Value);
                Assert.Equal(2, attempts);
                await table.DisposeAsync();
                Assert.Equal(1, ((ConnectionStub)connection).DisposalCount);
            }
        }
        finally
        {
            connecting.TrySetException(new InvalidOperationException("Test cleanup."));
            _ = await Record.ExceptionAsync(() => operation);
            await table.DisposeAsync();
        }
    }

    [Fact]
    public async Task Disposing_an_unused_table_does_not_start_a_connection()
    {
        var attempts = 0;
        var table = new RedisMembershipTable(() =>
        {
            Interlocked.Increment(ref attempts);
            throw new InvalidOperationException("Unexpected connection attempt.");
        }, "lakona:{connection-test}:table");
        await table.DisposeAsync();
        await table.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            table.AllocateGenerationAsync("test", TestContext.Current.CancellationToken).AsTask());
        Assert.Equal(0, attempts);
    }

    public class ConnectionStub : DispatchProxy
    {
        private readonly IDatabase database = DispatchProxy.Create<IDatabase, DatabaseStub>();
        public int DisposalCount;
        public int DatabaseAccessCount;
        public bool HoldDisposal;
        public TaskCompletionSource DisposalStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseDisposal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public static IConnectionMultiplexer Create() => DispatchProxy.Create<IConnectionMultiplexer, ConnectionStub>();

        protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name switch
        {
            nameof(IConnectionMultiplexer.GetDatabase) => GetDatabase(),
            nameof(IAsyncDisposable.DisposeAsync) => DisposeAsync(),
            _ => throw new NotSupportedException(method?.Name)
        };

        private IDatabase GetDatabase()
        {
            Interlocked.Increment(ref DatabaseAccessCount);
            return database;
        }

        private async ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref DisposalCount);
            DisposalStarted.TrySetResult();
            if (HoldDisposal) await ReleaseDisposal.Task;
        }
    }

    public class DatabaseStub : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name switch
        {
            nameof(IDatabase.ScriptEvaluateAsync) => Task.FromResult(RedisResult.Create([
                RedisResult.Create((RedisValue)1),
                RedisResult.Create((RedisValue)"10000000000000000000000000000000"),
                RedisResult.Create((RedisValue)"1")
            ])),
            _ => throw new NotSupportedException(method?.Name)
        };
    }
}
