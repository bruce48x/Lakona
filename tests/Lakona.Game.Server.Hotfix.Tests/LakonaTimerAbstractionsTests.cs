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
                GenericArguments = method.GetGenericArguments(),
                Parameters = method.GetParameters(),
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
                Assert.Equal(["TArgs"], method.GenericArguments.Select(argument => argument.Name).ToArray());
                Assert.Equal(4, method.Parameters.Length);
                Assert.Equal(typeof(HotfixTimerEntry<>), method.Parameters[0].ParameterType.GetGenericTypeDefinition());
                Assert.Equal([typeof(TimeSpan), typeof(CancellationToken)], [method.Parameters[1].ParameterType, method.Parameters[3].ParameterType]);
                Assert.True(method.Parameters[2].ParameterType.IsGenericParameter);
                Assert.Equal("TArgs", method.Parameters[2].ParameterType.Name);
                Assert.True(method.Parameters[3].HasDefaultValue);
                Assert.Null(method.Parameters[3].DefaultValue);
                Assert.Equal(typeof(ValueTask<TimerId>), method.ReturnType);
            },
            method =>
            {
                Assert.Equal(nameof(LakonaTimer.CreatePeriodicTimerAsync), method.Name);
                Assert.Equal(["TArgs"], method.GenericArguments.Select(argument => argument.Name).ToArray());
                Assert.Equal(5, method.Parameters.Length);
                Assert.Equal(typeof(HotfixTimerEntry<>), method.Parameters[0].ParameterType.GetGenericTypeDefinition());
                Assert.Equal(
                    [typeof(TimeSpan), typeof(TimeSpan), typeof(CancellationToken)],
                    [method.Parameters[1].ParameterType, method.Parameters[2].ParameterType, method.Parameters[4].ParameterType]);
                Assert.True(method.Parameters[3].ParameterType.IsGenericParameter);
                Assert.Equal("TArgs", method.Parameters[3].ParameterType.Name);
                Assert.True(method.Parameters[4].HasDefaultValue);
                Assert.Null(method.Parameters[4].DefaultValue);
                Assert.Equal(typeof(ValueTask<TimerId>), method.ReturnType);
            },
            method =>
            {
                Assert.Equal(nameof(LakonaTimer.DestroyTimerAsync), method.Name);
                Assert.Empty(method.GenericArguments);
                Assert.Equal(2, method.Parameters.Length);
                Assert.Equal([typeof(TimerId), typeof(CancellationToken)], method.Parameters.Select(parameter => parameter.ParameterType).ToArray());
                Assert.True(method.Parameters[1].HasDefaultValue);
                Assert.Null(method.Parameters[1].DefaultValue);
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
    public void TimerId_rejects_empty_guid_from_internal_factory()
    {
        Assert.Throws<ArgumentException>(() => TimerId.FromGuid(Guid.Empty));
    }

    [Fact]
    public void TimerId_uses_guid_equality_and_hash_code()
    {
        var value = Guid.NewGuid();
        var first = TimerId.FromGuid(value);
        var second = TimerId.FromGuid(value);
        var other = TimerId.FromGuid(Guid.NewGuid());

        Assert.Equal(first, second);
        Assert.True(first == second);
        Assert.False(first != second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.NotEqual(first, other);
        Assert.False(first == other);
        Assert.True(first != other);
    }

    [Fact]
    public void TimerId_valid_value_formats_as_invariant_guid_d()
    {
        var value = Guid.Parse("7d9a16f6-1f66-4d01-a861-85081ce0ba4e");
        var timerId = TimerId.FromGuid(value);

        Assert.Equal(value.ToString("D", System.Globalization.CultureInfo.InvariantCulture), timerId.ToString());
    }

    [Fact]
    public void TimerTick_exposes_expected_public_generic_shape()
    {
        var type = typeof(TimerTick<TimerArgs>);

        Assert.True(type.IsGenericType);
        Assert.True(type.IsPublic);
        Assert.True(type.IsClass);
        Assert.False(type.IsValueType);
        Assert.True(type.IsSealed);
        Assert.Equal("TimerTick`1", type.GetGenericTypeDefinition().Name);
        AssertTimerTickProperty<TimerId>(type, nameof(TimerTick<TimerArgs>.TimerId));
        AssertTimerTickProperty<TimerArgs>(type, nameof(TimerTick<TimerArgs>.Args));
        AssertTimerTickProperty<IServiceProvider>(type, nameof(TimerTick<TimerArgs>.Services));
        AssertTimerTickProperty<DateTimeOffset>(type, nameof(TimerTick<TimerArgs>.DueAtUtc));
        AssertTimerTickProperty<DateTimeOffset>(type, nameof(TimerTick<TimerArgs>.ObservedAtUtc));
        AssertTimerTickProperty<CancellationToken>(type, nameof(TimerTick<TimerArgs>.CancellationToken));
    }

    [Fact]
    public void TimerTick_rejects_null_services()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new TimerTick<TimerArgs>(
                TimerId.FromGuid(Guid.NewGuid()),
                new TimerArgs("args"),
                null!,
                DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch,
                CancellationToken.None));
    }

    [Fact]
    public async Task CreateOnceTimerAsync_requires_active_hotfix_execution_scope()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await LakonaTimer.CreateOnceTimerAsync(
                TimerEntry,
                TimeSpan.Zero,
                new TimerArgs("outside"),
                CancellationToken.None));

        Assert.Contains("hotfix execution scope", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreatePeriodicTimerAsync_requires_active_hotfix_execution_scope()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await LakonaTimer.CreatePeriodicTimerAsync(
                TimerEntry,
                TimeSpan.Zero,
                TimeSpan.FromSeconds(1),
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

        var timerId = await LakonaTimer.CreateOnceTimerAsync(
            TimerEntry,
            TimeSpan.Zero,
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
            await LakonaTimer.CreateOnceTimerAsync(
                TimerEntry,
                TimeSpan.FromTicks(-1),
                new TimerArgs("negative"),
                CancellationToken.None));

        Assert.Equal(0, backend.CreateCount);
    }

    [Fact]
    public async Task CreatePeriodicTimerAsync_accepts_zero_due_time()
    {
        var backend = new RecordingTimerBackend();
        using var scope = LakonaTimerExecutionScope.Enter(backend, new object());

        var timerId = await LakonaTimer.CreatePeriodicTimerAsync(
            TimerEntry,
            TimeSpan.Zero,
            TimeSpan.FromSeconds(1),
            new TimerArgs("zero"),
            CancellationToken.None);

        Assert.True(timerId.IsValid);
        Assert.Equal(TimeSpan.Zero, backend.PeriodicDueTime);
        Assert.Equal(TimeSpan.FromSeconds(1), backend.Period);
        Assert.Equal(nameof(TimerCallback.HandleAsync), backend.MethodName);
    }

    [Fact]
    public async Task CreatePeriodicTimerAsync_rejects_negative_due_time()
    {
        var backend = new RecordingTimerBackend();
        using var scope = LakonaTimerExecutionScope.Enter(backend, new object());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await LakonaTimer.CreatePeriodicTimerAsync(
                TimerEntry,
                TimeSpan.FromTicks(-1),
                TimeSpan.FromSeconds(1),
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
            await LakonaTimer.CreatePeriodicTimerAsync(
                TimerEntry,
                TimeSpan.Zero,
                TimeSpan.FromTicks(periodTicks),
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
                    LakonaTimer.CreateOnceTimerAsync(
                        TimerEntry,
                        TimeSpan.Zero,
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

    private static readonly HotfixTimerEntry<TimerArgs> TimerEntry = new(
        typeof(TimerCallback).FullName!,
        nameof(TimerCallback.HandleAsync),
        42UL);

    private sealed class TimerCallback
    {
        public ValueTask HandleAsync(TimerTick<TimerArgs> tick)
        {
            _ = tick;
            return default;
        }
    }

    private sealed class RecordingTimerBackend : ILakonaTimerBackend
    {
        public int CreateCount { get; private set; }

        public TimeSpan? OnceDueTime { get; private set; }

        public TimeSpan? PeriodicDueTime { get; private set; }

        public TimeSpan? Period { get; private set; }

        public string? MethodName { get; private set; }

        public ValueTask<TimerId> CreateOnceTimerAsync<TArgs>(
            HotfixTimerEntry<TArgs> callback,
            TimeSpan dueTime,
            TArgs args,
            CancellationToken cancellationToken)
        {
            _ = args;
            _ = cancellationToken;
            CreateCount++;
            OnceDueTime = dueTime;
            MethodName = callback.MethodName;
            return new ValueTask<TimerId>(TimerId.FromGuid(Guid.NewGuid()));
        }

        public ValueTask<TimerId> CreatePeriodicTimerAsync<TArgs>(
            HotfixTimerEntry<TArgs> callback,
            TimeSpan dueTime,
            TimeSpan period,
            TArgs args,
            CancellationToken cancellationToken)
        {
            _ = args;
            _ = cancellationToken;
            CreateCount++;
            PeriodicDueTime = dueTime;
            Period = period;
            MethodName = callback.MethodName;
            return new ValueTask<TimerId>(TimerId.FromGuid(Guid.NewGuid()));
        }

        public ValueTask DestroyTimerAsync(TimerId timerId, CancellationToken cancellationToken)
        {
            _ = timerId;
            _ = cancellationToken;
            return default;
        }
    }

    private static void AssertTimerTickProperty<TProperty>(Type type, string propertyName)
    {
        var property = type.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(property);
        Assert.Equal(typeof(TProperty), property.PropertyType);
        Assert.NotNull(property.GetMethod);
        Assert.Null(property.SetMethod);
    }
}
