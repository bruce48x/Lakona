using FrameworkBenchmark.Contracts;

namespace FrameworkBenchmark.Coordinator;

public static class CaseResultValidator
{
    public static ValidatedCaseResult Validate(BenchmarkCase benchmarkCase, CaseResult result)
    {
        ArgumentNullException.ThrowIfNull(benchmarkCase);
        ArgumentNullException.ThrowIfNull(result);
        var errors = new List<string>();

        Equal(BenchmarkSchemaVersions.V1, result.SchemaVersion, "schemaVersion", errors);
        Equal(benchmarkCase.Id, result.CaseId, "caseId", errors);
        Equal(benchmarkCase.Framework, result.Framework, "framework", errors);
        Equal(benchmarkCase.Workload, result.Workload, "workload", errors);

        if (!double.IsFinite(result.AchievedRequestsPerSecond) || result.AchievedRequestsPerSecond < 0)
        {
            errors.Add("achievedRequestsPerSecond must be finite and non-negative");
        }

        var outcomeValues = new[]
        {
            result.Outcomes.Started,
            result.Outcomes.Completed,
            result.Outcomes.Succeeded,
            result.Outcomes.Rejected,
            result.Outcomes.Corrupt,
            result.Outcomes.Misrouted,
            result.Outcomes.TimedOut,
            result.Outcomes.Disconnected,
            result.Outcomes.CanceledAtDrain,
            result.Outcomes.DuplicateResponses
        };
        if (outcomeValues.Any(static value => value < 0))
        {
            errors.Add("outcome counts must be non-negative");
        }

        var terminal = result.Outcomes.Completed + result.Outcomes.TimedOut +
            result.Outcomes.Disconnected + result.Outcomes.CanceledAtDrain;
        if (result.Outcomes.Started != terminal)
        {
            errors.Add($"started ({result.Outcomes.Started}) does not equal terminal outcomes ({terminal})");
        }

        var completed = result.Outcomes.Succeeded + result.Outcomes.Rejected +
            result.Outcomes.Corrupt + result.Outcomes.Misrouted;
        if (result.Outcomes.Completed != completed)
        {
            errors.Add($"completed ({result.Outcomes.Completed}) does not equal completed outcomes ({completed})");
        }

        if (result.Outcomes.Rejected != 0 || result.Outcomes.Corrupt != 0 ||
            result.Outcomes.Misrouted != 0 || result.Outcomes.TimedOut != 0 ||
            result.Outcomes.Disconnected != 0 || result.Outcomes.CanceledAtDrain != 0 ||
            result.Outcomes.DuplicateResponses != 0)
        {
            errors.Add("correctness threshold requires every error outcome to be zero");
        }

        ValidateHistogram(benchmarkCase, result, errors);
        if (result.Metadata.Count == 0)
        {
            errors.Add("adapter metadata is required");
        }

        return new ValidatedCaseResult(benchmarkCase, result, errors.Count == 0, errors);
    }

    private static void ValidateHistogram(BenchmarkCase benchmarkCase, CaseResult result, List<string> errors)
    {
        var histogram = result.Histogram;
        Equal(benchmarkCase.Histogram.Unit, histogram.Unit, "histogram.unit", errors);
        if (histogram.LowestDiscernibleValue != benchmarkCase.Histogram.LowestDiscernibleValue ||
            histogram.HighestTrackableValue != benchmarkCase.Histogram.HighestTrackableValue ||
            histogram.SignificantDigits != benchmarkCase.Histogram.SignificantDigits)
        {
            errors.Add("histogram configuration does not match the case command");
        }

        if (histogram.TotalCount != result.Outcomes.Completed)
        {
            errors.Add($"histogram totalCount ({histogram.TotalCount}) does not equal completed ({result.Outcomes.Completed})");
        }

        if (histogram.Maximum < 0 || histogram.Maximum > histogram.HighestTrackableValue)
        {
            errors.Add("histogram maximum is outside the configured range");
        }

        long previousBound = 0;
        long bucketTotal = 0;
        foreach (var bucket in histogram.Buckets)
        {
            if (bucket.UpperBound <= previousBound || bucket.Count < 0)
            {
                errors.Add("histogram buckets must have increasing bounds and non-negative counts");
                break;
            }

            previousBound = bucket.UpperBound;
            bucketTotal += bucket.Count;
        }

        if (bucketTotal != histogram.TotalCount)
        {
            errors.Add($"histogram bucket count ({bucketTotal}) does not equal totalCount ({histogram.TotalCount})");
        }
    }

    private static void Equal(string expected, string actual, string property, List<string> errors)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            errors.Add($"{property} expected '{expected}' but was '{actual}'");
        }
    }
}
