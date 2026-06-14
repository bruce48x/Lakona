using System.Collections.Concurrent;

namespace Lakona.Game.LoadTesting.Internal;

public sealed class LoadRunRecorder
{
    private readonly ConcurrentBag<OperationSample> samples = [];
    private int startedUsers;
    private int completedUsers;

    public LoadRunRecorder(string scenarioName, int configuredUsers)
    {
        ScenarioName = string.IsNullOrWhiteSpace(scenarioName)
            ? throw new ArgumentException("Scenario name is required.", nameof(scenarioName))
            : scenarioName;
        ConfiguredUsers = configuredUsers > 0
            ? configuredUsers
            : throw new ArgumentOutOfRangeException(nameof(configuredUsers), configuredUsers, "Configured users must be greater than zero.");
    }

    public string ScenarioName { get; }

    public int ConfiguredUsers { get; }

    public int StartedUsers => Volatile.Read(ref startedUsers);

    public int CompletedUsers => Volatile.Read(ref completedUsers);

    public void RecordStartedUser()
    {
        Interlocked.Increment(ref startedUsers);
    }

    public void RecordCompletedUser()
    {
        Interlocked.Increment(ref completedUsers);
    }

    public void RecordSucceededOperation(string operationName, TimeSpan elapsed)
    {
        samples.Add(new OperationSample(operationName, elapsed, OperationOutcome.Succeeded, null));
    }

    public void RecordFailedOperation(string operationName, TimeSpan elapsed, Exception exception)
    {
        samples.Add(new OperationSample(operationName, elapsed, OperationOutcome.Failed, exception));
    }

    public void RecordCanceledOperation(string operationName, TimeSpan elapsed, OperationCanceledException exception)
    {
        samples.Add(new OperationSample(operationName, elapsed, OperationOutcome.Canceled, exception));
    }

    public LoadRunSummary CreateSummary(TimeSpan elapsed)
    {
        var snapshot = samples.ToArray();
        var latencies = snapshot
            .Where(sample => sample.Outcome == OperationOutcome.Succeeded)
            .GroupBy(sample => sample.OperationName, StringComparer.Ordinal)
            .Select(group => CreateLatencySummary(group.Key, group.Select(sample => sample.Elapsed).ToArray()))
            .OrderBy(summary => summary.OperationName, StringComparer.Ordinal)
            .ToArray();
        var errors = snapshot
            .Where(sample => sample.Outcome == OperationOutcome.Failed && sample.Exception is not null)
            .GroupBy(
                sample => new
                {
                    sample.OperationName,
                    ExceptionType = sample.Exception!.GetType().Name,
                    Message = sample.Exception.Message
                })
            .Select(group => new LoadErrorSummary(
                group.Key.OperationName,
                group.Key.ExceptionType,
                group.Key.Message,
                group.Count()))
            .GroupBy(error => new { error.OperationName, error.ExceptionType })
            .SelectMany(group => group
                .OrderByDescending(error => error.Count)
                .ThenBy(error => error.Message, StringComparer.Ordinal)
                .Take(5))
            .OrderBy(error => error.OperationName, StringComparer.Ordinal)
            .ThenBy(error => error.ExceptionType, StringComparer.Ordinal)
            .ThenBy(error => error.Message, StringComparer.Ordinal)
            .ToArray();

        return new LoadRunSummary(
            ScenarioName,
            ConfiguredUsers,
            StartedUsers,
            CompletedUsers,
            snapshot.Length,
            snapshot.Count(sample => sample.Outcome == OperationOutcome.Succeeded),
            snapshot.Count(sample => sample.Outcome == OperationOutcome.Failed),
            snapshot.Count(sample => sample.Outcome == OperationOutcome.Canceled),
            elapsed,
            latencies,
            errors);
    }

    private static LoadOperationLatencySummary CreateLatencySummary(string operationName, TimeSpan[] values)
    {
        Array.Sort(values);
        var ticks = values.Sum(value => value.Ticks) / values.Length;
        return new LoadOperationLatencySummary(
            operationName,
            values.Length,
            TimeSpan.FromTicks(ticks),
            Percentile(values, 0.50),
            Percentile(values, 0.95),
            Percentile(values, 0.99));
    }

    private static TimeSpan Percentile(TimeSpan[] sortedValues, double percentile)
    {
        if (sortedValues.Length == 0)
        {
            return TimeSpan.Zero;
        }

        var index = (int)Math.Ceiling(percentile * sortedValues.Length) - 1;
        index = Math.Clamp(index, 0, sortedValues.Length - 1);
        return sortedValues[index];
    }

    private sealed record OperationSample(
        string OperationName,
        TimeSpan Elapsed,
        OperationOutcome Outcome,
        Exception? Exception);

    private enum OperationOutcome
    {
        Succeeded,
        Failed,
        Canceled
    }
}
