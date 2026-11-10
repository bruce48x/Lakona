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
        cancellationToken.ThrowIfCancellationRequested();

        var recorder = new LoadRunRecorder(scenario.Name, options.Users);
        var timestamp = Stopwatch.GetTimestamp();

        using var plannedRun = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        plannedRun.CancelAfter(options.RampUp + options.Duration);

        var tasks = new List<Task>(options.Users);
        try
        {
            for (var userIndex = 0; userIndex < options.Users; userIndex++)
            {
                plannedRun.Token.ThrowIfCancellationRequested();
                await DelayForUserStartAsync(userIndex, options, plannedRun.Token).ConfigureAwait(false);
                plannedRun.Token.ThrowIfCancellationRequested();
                tasks.Add(RunUserAsync(userIndex, scenario, recorder, plannedRun.Token));
            }
        }
        catch (OperationCanceledException) when (plannedRun.IsCancellationRequested)
        {
            // Planned expiry returns a summary; caller cancellation is propagated after draining.
        }
        catch
        {
            await plannedRun.CancelAsync().ConfigureAwait(false);
            throw;
        }
        finally
        {
            // Scenario tasks own their cleanup. Never abandon them with a canceled wait token.
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
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
        catch (Exception ex)
        {
            if (!context.IsRecordedException(ex))
            {
                recorder.RecordFailedUser(ex);
            }
        }
        finally
        {
            recorder.RecordCompletedUser();
        }
    }
}
