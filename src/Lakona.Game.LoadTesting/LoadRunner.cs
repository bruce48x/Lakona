using System.Diagnostics;
using Lakona.Game.LoadTesting.Internal;

namespace Lakona.Game.LoadTesting;

public sealed class LoadRunner
{
    public async ValueTask<LoadRunSummary> RunAsync(
        ILoadScenario scenario,
        LoadRunOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(options);

        var recorder = new LoadRunRecorder(scenario.Name, options.Users);
        var timestamp = Stopwatch.GetTimestamp();

        using var plannedRun = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        plannedRun.CancelAfter(options.RampUp + options.Duration);

        var tasks = new List<Task>(options.Users);
        for (var userIndex = 0; userIndex < options.Users; userIndex++)
        {
            await DelayForUserStartAsync(userIndex, options, cancellationToken).ConfigureAwait(false);
            tasks.Add(RunUserAsync(userIndex, scenario, recorder, plannedRun.Token));
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
        return recorder.CreateSummary(Stopwatch.GetElapsedTime(timestamp));
    }

    private static async Task DelayForUserStartAsync(int userIndex, LoadRunOptions options, CancellationToken cancellationToken)
    {
        if (userIndex == 0 || options.RampUp == TimeSpan.Zero || options.Users == 1)
        {
            return;
        }

        var delay = TimeSpan.FromTicks(options.RampUp.Ticks * userIndex / (options.Users - 1));
        var previousDelay = TimeSpan.FromTicks(options.RampUp.Ticks * (userIndex - 1) / (options.Users - 1));
        var incrementalDelay = delay - previousDelay;
        if (incrementalDelay > TimeSpan.Zero)
        {
            await Task.Delay(incrementalDelay, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task RunUserAsync(
        int userIndex,
        ILoadScenario scenario,
        LoadRunRecorder recorder,
        CancellationToken cancellationToken)
    {
        var context = new LoadUserContext(userIndex, $"user-{userIndex + 1}", recorder);
        recorder.RecordStartedUser();
        try
        {
            await scenario.RunUserAsync(context, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
        }
        finally
        {
            recorder.RecordCompletedUser();
        }
    }
}
