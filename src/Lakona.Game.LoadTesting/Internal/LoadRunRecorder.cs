using System.Collections.Concurrent;

namespace Lakona.Game.LoadTesting.Internal;

public sealed class LoadRunRecorder
{
    internal const int MaxLatencySamplesPerOperation = 1024;
    internal const int MaxTrackedErrorMessagesPerOperationAndType = 64;
    private const int MaxErrorSummariesPerOperationAndType = 5;
    private const string UserFailureOperationName = "user";

    private readonly ConcurrentDictionary<string, OperationAggregate> operations = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<ErrorBucketKey, ErrorBucket> errors = [];
    private int startedUsers;
    private int completedUsers;
    private int failedUsers;

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

    internal int BufferedLatencySampleCount => operations.Values.Sum(static operation => operation.BufferedLatencySampleCount);

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
        GetOperation(operationName).RecordSucceeded(elapsed);
    }

    public void RecordFailedOperation(string operationName, TimeSpan elapsed, Exception exception)
    {
        GetOperation(operationName).RecordFailed();
        RecordError(operationName, exception);
    }

    public void RecordCanceledOperation(string operationName, TimeSpan elapsed, OperationCanceledException exception)
    {
        GetOperation(operationName).RecordCanceled();
    }

    public void RecordFailedUser(Exception exception)
    {
        Interlocked.Increment(ref failedUsers);
        RecordError(UserFailureOperationName, exception);
    }

    public LoadRunSummary CreateSummary(TimeSpan elapsed)
    {
        var operationSnapshots = operations
            .Select(pair => pair.Value.CreateSnapshot(pair.Key))
            .OrderBy(snapshot => snapshot.OperationName, StringComparer.Ordinal)
            .ToArray();
        var latencies = operationSnapshots
            .Where(snapshot => snapshot.Succeeded > 0)
            .Select(CreateLatencySummary)
            .ToArray();
        var errorSummaries = errors
            .SelectMany(pair => pair.Value.CreateSummaries(pair.Key.OperationName, pair.Key.ExceptionType))
            .OrderBy(error => error.OperationName, StringComparer.Ordinal)
            .ThenBy(error => error.ExceptionType, StringComparer.Ordinal)
            .ThenBy(error => error.Message, StringComparer.Ordinal)
            .ToArray();

        return new LoadRunSummary(
            ScenarioName,
            ConfiguredUsers,
            StartedUsers,
            CompletedUsers,
            operationSnapshots.Sum(static snapshot => snapshot.Total),
            operationSnapshots.Sum(static snapshot => snapshot.Succeeded),
            operationSnapshots.Sum(static snapshot => snapshot.Failed),
            operationSnapshots.Sum(static snapshot => snapshot.Canceled),
            Volatile.Read(ref failedUsers),
            elapsed,
            latencies,
            errorSummaries);
    }

    private OperationAggregate GetOperation(string operationName)
    {
        return operations.GetOrAdd(operationName, static _ => new OperationAggregate());
    }

    private void RecordError(string operationName, Exception exception)
    {
        var key = new ErrorBucketKey(operationName, exception.GetType().Name);
        var bucket = errors.GetOrAdd(key, static _ => new ErrorBucket());
        bucket.Record(exception.Message);
    }

    private static LoadOperationLatencySummary CreateLatencySummary(OperationSnapshot snapshot)
    {
        var values = snapshot.LatencySamples;
        Array.Sort(values);
        return new LoadOperationLatencySummary(
            snapshot.OperationName,
            snapshot.Succeeded,
            snapshot.Average,
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

    private sealed class OperationAggregate
    {
        private readonly long[] latencySampleTicks = new long[MaxLatencySamplesPerOperation];
        private readonly int[] latencySampleReady = new int[MaxLatencySamplesPerOperation];
        private long total;
        private long succeeded;
        private long failed;
        private long canceled;
        private long succeededTicks;
        private int latencySampleCount;

        public int BufferedLatencySampleCount => Volatile.Read(ref latencySampleCount);

        public void RecordSucceeded(TimeSpan elapsed)
        {
            Interlocked.Increment(ref total);
            Interlocked.Increment(ref succeeded);
            Interlocked.Add(ref succeededTicks, elapsed.Ticks);
            while (true)
            {
                var index = Volatile.Read(ref latencySampleCount);
                if (index >= latencySampleTicks.Length)
                {
                    return;
                }

                if (Interlocked.CompareExchange(ref latencySampleCount, index + 1, index) == index)
                {
                    latencySampleTicks[index] = elapsed.Ticks;
                    Volatile.Write(ref latencySampleReady[index], 1);
                    return;
                }
            }
        }

        public void RecordFailed()
        {
            Interlocked.Increment(ref total);
            Interlocked.Increment(ref failed);
        }

        public void RecordCanceled()
        {
            Interlocked.Increment(ref total);
            Interlocked.Increment(ref canceled);
        }

        public OperationSnapshot CreateSnapshot(string operationName)
        {
            var reserved = Math.Min(Volatile.Read(ref latencySampleCount), latencySampleTicks.Length);
            var samples = new TimeSpan[reserved];
            var published = 0;
            for (var index = 0; index < reserved; index++)
            {
                if (Volatile.Read(ref latencySampleReady[index]) == 0)
                {
                    continue;
                }

                samples[published++] = TimeSpan.FromTicks(Volatile.Read(ref latencySampleTicks[index]));
            }

            if (published != samples.Length)
            {
                Array.Resize(ref samples, published);
            }

            var succeededCount = (int)Volatile.Read(ref succeeded);
            var totalTicks = Volatile.Read(ref succeededTicks);
            var average = succeededCount == 0 ? TimeSpan.Zero : TimeSpan.FromTicks(totalTicks / succeededCount);
            return new OperationSnapshot(
                operationName,
                (int)Volatile.Read(ref total),
                succeededCount,
                (int)Volatile.Read(ref failed),
                (int)Volatile.Read(ref canceled),
                average,
                samples);
        }
    }

    private sealed class ErrorBucket
    {
        private readonly Dictionary<string, int> messages = new(StringComparer.Ordinal);
        private readonly object gate = new();

        public void Record(string message)
        {
            lock (gate)
            {
                if (messages.TryGetValue(message, out var count))
                {
                    messages[message] = count + 1;
                    return;
                }

                if (messages.Count < MaxTrackedErrorMessagesPerOperationAndType)
                {
                    messages.Add(message, 1);
                }
            }
        }

        public IReadOnlyList<LoadErrorSummary> CreateSummaries(string operationName, string exceptionType)
        {
            KeyValuePair<string, int>[] snapshot;
            lock (gate)
            {
                snapshot = messages.ToArray();
            }

            return snapshot
                .OrderByDescending(pair => pair.Value)
                .ThenBy(pair => pair.Key, StringComparer.Ordinal)
                .Take(MaxErrorSummariesPerOperationAndType)
                .Select(pair => new LoadErrorSummary(operationName, exceptionType, pair.Key, pair.Value))
                .ToArray();
        }
    }

    private sealed record OperationSnapshot(
        string OperationName,
        int Total,
        int Succeeded,
        int Failed,
        int Canceled,
        TimeSpan Average,
        TimeSpan[] LatencySamples);

    private readonly record struct ErrorBucketKey(string OperationName, string ExceptionType);
}
