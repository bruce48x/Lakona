namespace FrameworkBenchmark.Contracts;

public static class BenchmarkSchemaVersions
{
    public const string V1 = "1";
}

public sealed record BenchmarkSuite(
    string SchemaVersion,
    string Id,
    IReadOnlyList<string> Frameworks,
    IReadOnlyList<string> Workloads,
    IReadOnlyList<int> PayloadSizes,
    IReadOnlyList<int> Concurrency,
    int Seed,
    SuiteTiming Timing,
    HistogramConfiguration Histogram);

public sealed record SuiteTiming(
    int StartupTimeoutMilliseconds,
    int ReadinessTimeoutMilliseconds,
    int WarmupMilliseconds,
    int MeasurementMilliseconds,
    int RequestTimeoutMilliseconds,
    int DrainTimeoutMilliseconds,
    int ShutdownTimeoutMilliseconds);

public sealed record HistogramConfiguration(
    string Unit,
    long LowestDiscernibleValue,
    long HighestTrackableValue,
    int SignificantDigits);

public sealed record AdapterManifest(
    string SchemaVersion,
    string Framework,
    string Revision,
    string Runtime,
    string BuildMode,
    string LicenseUrl,
    IReadOnlyList<string> SupportedWorkloads,
    IReadOnlyDictionary<string, string> Metadata,
    IReadOnlyList<ServerProcessSpecification> Servers,
    ProcessCommand Driver);

public sealed record ServerProcessSpecification(
    string Role,
    IReadOnlyList<string> Ports,
    ProcessCommand Command);

public sealed record ProcessCommand(
    string FileName,
    IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string>? Environment = null);

public sealed record BenchmarkCase(
    string Id,
    string SuiteId,
    string Framework,
    string Workload,
    int PayloadSize,
    int Concurrency,
    int Seed,
    SuiteTiming Timing,
    HistogramConfiguration Histogram);

public sealed record CaseCommand(
    string SchemaVersion,
    string RunId,
    string CaseId,
    string Framework,
    string Workload,
    int PayloadSize,
    int Concurrency,
    int ConnectionCount,
    int Seed,
    SuiteTiming Timing,
    HistogramConfiguration Histogram,
    IReadOnlyDictionary<string, string> Endpoints);

public sealed record CaseResult(
    string SchemaVersion,
    string CaseId,
    string Framework,
    string Workload,
    double AchievedRequestsPerSecond,
    CaseOutcomeCounts Outcomes,
    LatencyHistogram Histogram,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record CaseOutcomeCounts(
    long Started,
    long Completed,
    long Succeeded,
    long Rejected,
    long Corrupt,
    long Misrouted,
    long TimedOut,
    long Disconnected,
    long CanceledAtDrain,
    long DuplicateResponses);

public sealed record LatencyHistogram(
    string Unit,
    long LowestDiscernibleValue,
    long HighestTrackableValue,
    int SignificantDigits,
    long TotalCount,
    long Maximum,
    IReadOnlyList<HistogramBucket> Buckets);

public sealed record HistogramBucket(long UpperBound, long Count);

public sealed record ValidatedCaseResult(
    BenchmarkCase Case,
    CaseResult Result,
    bool IsValid,
    IReadOnlyList<string> Errors);

public sealed record RunSummary(
    string SchemaVersion,
    string RunId,
    string SuiteId,
    string Profile,
    string Mode,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    IReadOnlyList<ValidatedCaseResult> Cases);

public sealed record RunManifest(
    string SchemaVersion,
    string RunId,
    string SuiteId,
    string Profile,
    string Mode,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    string? ToolError);
