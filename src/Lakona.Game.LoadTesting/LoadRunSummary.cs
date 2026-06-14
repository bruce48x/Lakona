namespace Lakona.Game.LoadTesting;

public sealed record LoadRunSummary(
    string ScenarioName,
    int ConfiguredUsers,
    int StartedUsers,
    int CompletedUsers,
    int TotalOperations,
    int SucceededOperations,
    int FailedOperations,
    int CanceledOperations,
    int FailedUsers,
    TimeSpan Elapsed,
    IReadOnlyList<LoadOperationLatencySummary> Latencies,
    IReadOnlyList<LoadErrorSummary> Errors);

public sealed record LoadOperationLatencySummary(
    string OperationName,
    int Count,
    TimeSpan Average,
    TimeSpan P50,
    TimeSpan P95,
    TimeSpan P99);

public sealed record LoadErrorSummary(
    string OperationName,
    string ExceptionType,
    string Message,
    int Count);
