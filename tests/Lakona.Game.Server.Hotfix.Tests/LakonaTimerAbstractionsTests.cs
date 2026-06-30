using System.Reflection;
using Lakona.Game.Server.Hotfix.Abstractions.Timers;
using Xunit;

namespace Lakona.Game.Server.Hotfix.Tests;

public sealed class LakonaTimerAbstractionsTests
{
    [Fact]
    public void LakonaTimer_exposes_only_expected_public_facade_methods()
    {
        var methods = typeof(LakonaTimer)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(method => new
            {
                method.Name,
                GenericArguments = method.GetGenericArguments().Select(argument => argument.Name).ToArray(),
                Parameters = method.GetParameters().Select(parameter => parameter.ParameterType).ToArray(),
                method.ReturnType
            })
            .OrderBy(method => method.Name, StringComparer.Ordinal)
            .ThenBy(method => method.Parameters.Length)
            .ToArray();

        Assert.Collection(
            methods,
            method =>
            {
                Assert.Equal(nameof(LakonaTimer.CreateOnceTimerAsync), method.Name);
                Assert.Equal(["TCallback", "TArgs"], method.GenericArguments);
                Assert.Equal([typeof(TimeSpan), typeof(string), typeof(CancellationToken)], [method.Parameters[0], method.Parameters[1], method.Parameters[3]]);
                Assert.True(method.Parameters[2].IsGenericParameter);
                Assert.Equal("TArgs", method.Parameters[2].Name);
                Assert.Equal(typeof(ValueTask<TimerId>), method.ReturnType);
            },
            method =>
            {
                Assert.Equal(nameof(LakonaTimer.CreatePeriodicTimerAsync), method.Name);
                Assert.Equal(["TCallback", "TArgs"], method.GenericArguments);
                Assert.Equal(
                    [typeof(TimeSpan), typeof(TimeSpan), typeof(string), typeof(CancellationToken)],
                    [method.Parameters[0], method.Parameters[1], method.Parameters[2], method.Parameters[4]]);
                Assert.True(method.Parameters[3].IsGenericParameter);
                Assert.Equal("TArgs", method.Parameters[3].Name);
                Assert.Equal(typeof(ValueTask<TimerId>), method.ReturnType);
            },
            method =>
            {
                Assert.Equal(nameof(LakonaTimer.DestroyTimerAsync), method.Name);
                Assert.Empty(method.GenericArguments);
                Assert.Equal([typeof(TimerId), typeof(CancellationToken)], method.Parameters);
                Assert.Equal(typeof(ValueTask), method.ReturnType);
            });
    }

    [Fact]
    public void TimerId_default_value_is_invalid()
    {
        var timerId = default(TimerId);

        Assert.False(timerId.IsValid);
        Assert.Equal("invalid", timerId.ToString());
    }

    [Fact]
    public void TimerId_does_not_expose_a_public_valid_id_factory()
    {
        var publicFactories = typeof(TimerId)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(method => method.ReturnType == typeof(TimerId))
            .ToArray();

        Assert.Empty(publicFactories);
    }

    [Fact]
    public async Task CreateOnceTimerAsync_requires_active_hotfix_execution_scope()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await LakonaTimer.CreateOnceTimerAsync<TimerCallback, TimerArgs>(
                TimeSpan.Zero,
                nameof(TimerCallback.HandleAsync),
                new TimerArgs("outside"),
                CancellationToken.None));

        Assert.Contains("hotfix execution scope", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DestroyTimerAsync_requires_active_hotfix_execution_scope()
    {
        var timerId = TimerId.FromGuid(Guid.NewGuid());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await LakonaTimer.DestroyTimerAsync(timerId, CancellationToken.None));

        Assert.Contains("hotfix execution scope", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateOnceTimerAsync_accepts_zero_due_time()
    {
        var backend = new RecordingTimerBackend();
        using var scope = LakonaTimerExecutionScope.Enter(backend, new object());

        var timerId = await LakonaTimer.CreateOnceTimerAsync<TimerCallback, TimerArgs>(
            TimeSpan.Zero,
            nameof(TimerCallback.HandleAsync),
            new TimerArgs("zero"),
            CancellationToken.None);

        Assert.True(timerId.IsValid);
        Assert.Equal(TimeSpan.Zero, backend.OnceDueTime);
        Assert.Equal(nameof(TimerCallback.HandleAsync), backend.MethodName);
    }

    [Fact]
    public async Task CreateOnceTimerAsync_rejects_negative_due_time()
    {
        var backend = new RecordingTimerBackend();
        using var scope = LakonaTimerExecutionScope.Enter(backend, new object());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await LakonaTimer.CreateOnceTimerAsync<TimerCallback, TimerArgs>(
                TimeSpan.FromTicks(-1),
                nameof(TimerCallback.HandleAsync),
                new TimerArgs("negative"),
                CancellationToken.None));

        Assert.Equal(0, backend.CreateCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task CreatePeriodicTimerAsync_rejects_non_positive_period(long periodTicks)
    {
        var backend = new RecordingTimerBackend();
        using var scope = LakonaTimerExecutionScope.Enter(backend, new object());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await LakonaTimer.CreatePeriodicTimerAsync<TimerCallback, TimerArgs>(
                TimeSpan.Zero,
                TimeSpan.FromTicks(periodTicks),
                nameof(TimerCallback.HandleAsync),
                new TimerArgs("periodic"),
                CancellationToken.None));

        Assert.Equal(0, backend.CreateCount);
    }

    [Fact]
    public async Task Captured_execution_context_cannot_use_scope_after_it_exits()
    {
        var backend = new RecordingTimerBackend();
        ExecutionContext? capturedContext;
        Exception? capturedException = null;
        using (LakonaTimerExecutionScope.Enter(backend, new object()))
        {
            capturedContext = ExecutionContext.Capture();
        }

        Assert.NotNull(capturedContext);
        ExecutionContext.Run(
            capturedContext,
            _ =>
            {
                try
                {
                    LakonaTimer.CreateOnceTimerAsync<TimerCallback, TimerArgs>(
                        TimeSpan.Zero,
                        nameof(TimerCallback.HandleAsync),
                        new TimerArgs("captured"),
                        CancellationToken.None).GetAwaiter().GetResult();
                }
                catch (Exception exception)
                {
                    capturedException = exception;
                }
            },
            null);

        Assert.IsType<InvalidOperationException>(capturedException);
        Assert.Equal(0, backend.CreateCount);
    }

    private sealed record TimerArgs(string Value);

    private sealed class TimerCallback
    {
        public static ValueTask HandleAsync(TimerArgs args, TimerTick tick)
        {
            _ = args;
            _ = tick;
            return default;
        }
    }

    private sealed class RecordingTimerBackend : ILakonaTimerBackend
    {
        public int CreateCount { get; private set; }

        public TimeSpan? OnceDueTime { get; private set; }

        public string? MethodName { get; private set; }

        public ValueTask<TimerId> CreateOnceTimerAsync<TCallback, TArgs>(
            TimeSpan dueTime,
            string methodName,
            TArgs args,
            CancellationToken cancellationToken)
        {
            _ = args;
            _ = cancellationToken;
            CreateCount++;
            OnceDueTime = dueTime;
            MethodName = methodName;
            return new ValueTask<TimerId>(TimerId.FromGuid(Guid.NewGuid()));
        }

        public ValueTask<TimerId> CreatePeriodicTimerAsync<TCallback, TArgs>(
            TimeSpan dueTime,
            TimeSpan period,
            string methodName,
            TArgs args,
            CancellationToken cancellationToken)
        {
            _ = dueTime;
            _ = period;
            _ = methodName;
            _ = args;
            _ = cancellationToken;
            CreateCount++;
            return new ValueTask<TimerId>(TimerId.FromGuid(Guid.NewGuid()));
        }

        public ValueTask DestroyTimerAsync(TimerId timerId, CancellationToken cancellationToken)
        {
            _ = timerId;
            _ = cancellationToken;
            return default;
        }
    }
}
