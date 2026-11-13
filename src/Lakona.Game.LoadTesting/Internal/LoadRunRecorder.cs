using System.Collections.Concurrent;

namespace Lakona.Game.LoadTesting.Internal;

public sealed class LoadRunRecorder
{
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
        ArgumentOutOfRangeException.ThrowIfNegative(elapsed.Ticks, nameof(elapsed));
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

    /// <summary>
    /// Creates the final summary after recording has finished. Concurrent writers
    /// are supported, but this method is not an atomic live-progress snapshot.
    /// </summary>
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
        return new LoadOperationLatencySummary(
            snapshot.OperationName,
            snapshot.Succeeded,
            snapshot.Latency.Average,
            snapshot.Latency.Percentile(50),
            snapshot.Latency.Percentile(95),
            snapshot.Latency.Percentile(99));
    }

    private sealed class OperationAggregate
    {
        private readonly LatencyHistogram latency = new();
        private long failed;
        private long canceled;

        public void RecordSucceeded(TimeSpan elapsed) => latency.Record(elapsed.Ticks);

        public void RecordFailed() => Interlocked.Increment(ref failed);

        public void RecordCanceled() => Interlocked.Increment(ref canceled);

        public OperationSnapshot CreateSnapshot(string operationName)
        {
            var snapshot = latency.CreateSnapshot();
            var succeededCount = snapshot.Count;
            var failedCount = Volatile.Read(ref failed);
            var canceledCount = Volatile.Read(ref canceled);
            return new OperationSnapshot(
                operationName,
                succeededCount + failedCount + canceledCount,
                succeededCount,
                failedCount,
                canceledCount,
                snapshot);
        }
    }

    private sealed class ErrorBucket
    {
        private readonly Dictionary<string, long> messages = new(StringComparer.Ordinal);
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
            KeyValuePair<string, long>[] snapshot;
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
        long Total,
        long Succeeded,
        long Failed,
        long Canceled,
        LatencyHistogram.Snapshot Latency);

    private readonly record struct ErrorBucketKey(string OperationName, string ExceptionType);
}
