using System.Collections.Concurrent;
using Lakona.Game.LoadTesting;
using Xunit;

namespace Lakona.Game.LoadTesting.Tests;

public sealed class LoadRunnerTests
{
    [Fact]
    public async Task RunAsync_PrecancelledRunStartsNoUsers()
    {
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        var scenario = new RecordingScenario();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new LoadRunner().RunAsync(
            scenario, new LoadRunOptions(3, TimeSpan.Zero, TimeSpan.FromMinutes(1)), cancel.Token).AsTask());
        Assert.Equal(cancel.Token, error.CancellationToken);
        Assert.Empty(scenario.UserNames);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RunAsync_ExternalCancellationDrainsAllStartedUsers(bool duringRampUp)
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var scenario = new CleanupScenario(duringRampUp ? 1 : 3);
        var run = new LoadRunner().RunAsync(scenario,
            new LoadRunOptions(3, duringRampUp ? TimeSpan.FromMinutes(1) : TimeSpan.Zero, TimeSpan.FromMinutes(1)),
            cancel.Token).AsTask();
        try
        {
            await scenario.AllStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
            cancel.Cancel();
            await scenario.AllCleaning.Task.WaitAsync(TestContext.Current.CancellationToken);
            Assert.False(run.IsCompleted);
            Assert.Equal(0, scenario.Cleaned);
        }
        finally
        {
            cancel.Cancel();
            scenario.ReleaseCleanup.TrySetResult();
        }
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.Equal(cancel.Token, error.CancellationToken);
        Assert.Equal(duringRampUp ? 1 : 3, scenario.Started);
        Assert.Equal(scenario.Started, scenario.Cleaned);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunAsync_CancellationBetweenStartsDoesNotLaunchAnotherUser(bool rampUp)
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var scenario = new CleanupScenario(1, onStart: cancel.Cancel);
        var run = new LoadRunner().RunAsync(scenario,
            new LoadRunOptions(3, rampUp ? TimeSpan.FromMinutes(1) : TimeSpan.Zero, TimeSpan.FromMinutes(1)),
            cancel.Token).AsTask();
        try
        {
            await scenario.AllCleaning.Task.WaitAsync(TestContext.Current.CancellationToken);
            Assert.False(run.IsCompleted);
            Assert.Equal(1, scenario.Started);
        }
        finally { scenario.ReleaseCleanup.TrySetResult(); }
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.Equal(cancel.Token, error.CancellationToken);
        Assert.Equal(1, scenario.Cleaned);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunAsync_PlannedEndWaitsForCleanupAndKeepsUserFailuresInSummary(bool failUser)
    {
        var scenario = new CleanupScenario(3, failUser: failUser);
        var run = new LoadRunner().RunAsync(scenario,
            new LoadRunOptions(3, TimeSpan.Zero, TimeSpan.FromMilliseconds(200)),
            TestContext.Current.CancellationToken).AsTask();
        try
        {
            await scenario.AllStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
            await scenario.AllCleaning.Task.WaitAsync(TestContext.Current.CancellationToken);
            Assert.False(run.IsCompleted);
        }
        finally { scenario.ReleaseCleanup.TrySetResult(); }
        var summary = await run;
        Assert.Equal(3, summary.StartedUsers);
        Assert.Equal(3, summary.CompletedUsers);
        Assert.Equal(3, scenario.Cleaned);
        Assert.Equal(failUser ? 1 : 0, summary.FailedUsers);
        Assert.Equal(0, summary.FailedOperations);
        if (failUser) Assert.Equal("user failed", Assert.Single(summary.Errors).Message);
        else Assert.Empty(summary.Errors);
    }

    private sealed class CleanupScenario(int expectedUsers, Action? onStart = null, bool failUser = false) : ILoadScenario
    {
        public string Name => "controlled-cleanup";
        public TaskCompletionSource AllStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AllCleaning { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseCleanup { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Started;
        public int Cleaned;
        private int cleaning;

        public async ValueTask RunUserAsync(LoadUserContext context, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref Started) == expectedUsers) AllStarted.TrySetResult();
            try
            {
                onStart?.Invoke();
                if (failUser && context.UserIndex == 0) throw new InvalidOperationException("user failed");
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            finally
            {
                if (Interlocked.Increment(ref cleaning) == expectedUsers) AllCleaning.TrySetResult();
                await ReleaseCleanup.Task;
                Interlocked.Increment(ref Cleaned);
            }
        }
    }

    [Fact]
    public async Task RunAsync_StartsConfiguredUsersAndCompletes()
    {
        var scenario = new RecordingScenario();
        var runner = new LoadRunner();

        var summary = await runner.RunAsync(
            scenario,
            new LoadRunOptions(Users: 3, RampUp: TimeSpan.Zero, Duration: TimeSpan.FromMilliseconds(20)),
            TestContext.Current.CancellationToken);

        Assert.Equal(3, summary.ConfiguredUsers);
        Assert.Equal(3, summary.StartedUsers);
        Assert.Equal(3, summary.CompletedUsers);
        Assert.Equal(["user-1", "user-2", "user-3"], scenario.UserNames.Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task RunAsync_SpreadsStartsAcrossRampUp()
    {
        var scenario = new RecordingScenario();
        var runner = new LoadRunner();

        await runner.RunAsync(
            scenario,
            new LoadRunOptions(Users: 3, RampUp: TimeSpan.FromMilliseconds(90), Duration: TimeSpan.FromMilliseconds(20)),
            TestContext.Current.CancellationToken);

        var starts = scenario.StartOffsets.Order().ToArray();
        Assert.Equal(3, starts.Length);
        Assert.True(starts[1] >= TimeSpan.FromMilliseconds(20), $"Second user started too early: {starts[1]}");
        Assert.True(starts[2] >= TimeSpan.FromMilliseconds(50), $"Third user started too early: {starts[2]}");
    }

    [Fact]
    public async Task RunAsync_PlannedCancellationIsNotFailure()
    {
        var scenario = new LoopUntilCanceledScenario();
        var runner = new LoadRunner();

        var summary = await runner.RunAsync(
            scenario,
            new LoadRunOptions(Users: 2, RampUp: TimeSpan.Zero, Duration: TimeSpan.FromMilliseconds(30)),
            TestContext.Current.CancellationToken);

        Assert.Equal(2, summary.StartedUsers);
        Assert.Equal(2, summary.CompletedUsers);
        Assert.Equal(0, summary.FailedOperations);
        Assert.True(summary.Elapsed >= TimeSpan.FromMilliseconds(20));
    }

    [Fact]
    public async Task RunAsync_MeasuredFailureAppearsInSummary()
    {
        var runner = new LoadRunner();

        var summary = await runner.RunAsync(
            new FailingScenario(),
            new LoadRunOptions(Users: 1, RampUp: TimeSpan.Zero, Duration: TimeSpan.FromMilliseconds(20)),
            TestContext.Current.CancellationToken);

        Assert.Equal(1, summary.FailedOperations);
        var error = Assert.Single(summary.Errors);
        Assert.Equal("login", error.OperationName);
        Assert.Equal(nameof(InvalidOperationException), error.ExceptionType);
    }

    [Fact]
    public async Task RunAsync_UnmeasuredScenarioFailureAppearsInSummary()
    {
        var runner = new LoadRunner();

        var summary = await runner.RunAsync(
            new SetupFailingScenario(),
            new LoadRunOptions(Users: 1, RampUp: TimeSpan.Zero, Duration: TimeSpan.FromMilliseconds(20)),
            TestContext.Current.CancellationToken);

        Assert.Equal(0, summary.FailedOperations);
        Assert.Equal(1, summary.FailedUsers);
        var error = Assert.Single(summary.Errors);
        Assert.Equal("user", error.OperationName);
        Assert.Equal(nameof(InvalidOperationException), error.ExceptionType);
        Assert.Equal("setup failed", error.Message);
    }

    private sealed class RecordingScenario : ILoadScenario
    {
        private readonly long createdAt = Environment.TickCount64;

        public string Name => "recording";

        public ConcurrentBag<string> UserNames { get; } = [];

        public ConcurrentBag<TimeSpan> StartOffsets { get; } = [];

        public ValueTask RunUserAsync(LoadUserContext context, CancellationToken cancellationToken)
        {
            UserNames.Add(context.UserName);
            StartOffsets.Add(TimeSpan.FromMilliseconds(Environment.TickCount64 - createdAt));
            return default;
        }
    }

    private sealed class LoopUntilCanceledScenario : ILoadScenario
    {
        public string Name => "loop";

        public async ValueTask RunUserAsync(LoadUserContext context, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(5, cancellationToken);
            }
        }
    }

    private sealed class FailingScenario : ILoadScenario
    {
        public string Name => "failing";

        public async ValueTask RunUserAsync(LoadUserContext context, CancellationToken cancellationToken)
        {
            await context.MeasureAsync("login", _ => throw new InvalidOperationException("login rejected"), cancellationToken);
        }
    }

    private sealed class SetupFailingScenario : ILoadScenario
    {
        public string Name => "setup-failing";

        public ValueTask RunUserAsync(LoadUserContext context, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("setup failed");
        }
    }
}
