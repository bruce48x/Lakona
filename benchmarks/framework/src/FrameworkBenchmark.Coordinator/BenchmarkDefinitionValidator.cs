using FrameworkBenchmark.Contracts;

namespace FrameworkBenchmark.Coordinator;

public static class BenchmarkDefinitionValidator
{
    private static readonly HashSet<string> KnownWorkloads = new(StringComparer.Ordinal)
    {
        "frontdoor.echo",
        "cluster.direct",
        "cluster.routed"
    };

    public static void Validate(BenchmarkSuite suite)
    {
        ArgumentNullException.ThrowIfNull(suite);
        RequireVersion(suite.SchemaVersion, "suite");
        RequireName(suite.Id, "Suite id");
        RequireDistinctNames(suite.Frameworks, "framework");
        RequireDistinctNames(suite.Workloads, "workload");

        foreach (var workload in suite.Workloads)
        {
            if (!KnownWorkloads.Contains(workload))
            {
                throw new InvalidDataException($"Suite '{suite.Id}' contains unknown workload '{workload}'.");
            }
        }

        RequirePositiveDistinct(suite.PayloadSizes, "payload size");
        RequirePositiveDistinct(suite.Concurrency, "concurrency");
        ValidateTiming(suite.Timing);
        ValidateHistogram(suite.Histogram);
    }

    public static void Validate(AdapterManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        RequireVersion(manifest.SchemaVersion, $"adapter '{manifest.Framework}'");
        RequireName(manifest.Framework, "Framework");
        RequireName(manifest.Revision, "Revision");
        RequireName(manifest.Runtime, "Runtime");
        RequireName(manifest.BuildMode, "Build mode");
        RequireName(manifest.LicenseUrl, "License URL");
        RequireDistinctNames(manifest.SupportedWorkloads, "supported workload");

        if (!Uri.TryCreate(manifest.LicenseUrl, UriKind.Absolute, out _))
        {
            throw new InvalidDataException($"Adapter '{manifest.Framework}' has an invalid license URL.");
        }

        if (manifest.Servers.Count == 0)
        {
            throw new InvalidDataException($"Adapter '{manifest.Framework}' must declare at least one server process.");
        }

        var roles = new HashSet<string>(StringComparer.Ordinal);
        var ports = new HashSet<string>(StringComparer.Ordinal);
        foreach (var server in manifest.Servers)
        {
            RequireName(server.Role, "Server role");
            if (!roles.Add(server.Role))
            {
                throw new InvalidDataException($"Adapter '{manifest.Framework}' declares duplicate server role '{server.Role}'.");
            }

            ValidateCommand(server.Command, $"server '{server.Role}'");
            foreach (var port in server.Ports)
            {
                RequireName(port, "Port placeholder");
                if (!ports.Add(port))
                {
                    throw new InvalidDataException($"Adapter '{manifest.Framework}' declares duplicate port placeholder '{port}'.");
                }
            }
        }

        ValidateCommand(manifest.Driver, "driver");
    }

    private static void ValidateCommand(ProcessCommand command, string owner)
    {
        if (command is null || string.IsNullOrWhiteSpace(command.FileName))
        {
            throw new InvalidDataException($"The {owner} command requires a file name.");
        }

        if (command.Arguments.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidDataException($"The {owner} command contains an empty argument.");
        }
    }

    private static void ValidateTiming(SuiteTiming timing)
    {
        if (timing.StartupTimeoutMilliseconds <= 0 ||
            timing.ReadinessTimeoutMilliseconds <= 0 ||
            timing.WarmupMilliseconds < 0 ||
            timing.MeasurementMilliseconds <= 0 ||
            timing.RequestTimeoutMilliseconds <= 0 ||
            timing.DrainTimeoutMilliseconds <= 0 ||
            timing.ShutdownTimeoutMilliseconds <= 0)
        {
            throw new InvalidDataException("Suite timing values must be positive, except warm-up which may be zero.");
        }
    }

    private static void ValidateHistogram(HistogramConfiguration histogram)
    {
        if (!string.Equals(histogram.Unit, "microseconds", StringComparison.Ordinal) ||
            histogram.LowestDiscernibleValue <= 0 ||
            histogram.HighestTrackableValue <= histogram.LowestDiscernibleValue ||
            histogram.SignificantDigits is < 1 or > 5)
        {
            throw new InvalidDataException("Suite histogram configuration is invalid.");
        }
    }

    private static void RequireVersion(string version, string owner)
    {
        if (!string.Equals(version, BenchmarkSchemaVersions.V1, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Unsupported {owner} schema version '{version}'.");
        }
    }

    private static void RequireDistinctNames(IReadOnlyList<string> values, string description)
    {
        if (values is null || values.Count == 0)
        {
            throw new InvalidDataException($"At least one {description} is required.");
        }

        var unique = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            RequireName(value, description);
            if (!unique.Add(value))
            {
                throw new InvalidDataException($"Duplicate {description} '{value}'.");
            }
        }
    }

    private static void RequirePositiveDistinct(IReadOnlyList<int> values, string description)
    {
        if (values is null || values.Count == 0 || values.Any(static value => value <= 0) || values.Distinct().Count() != values.Count)
        {
            throw new InvalidDataException($"Suite {description} values must be positive and distinct.");
        }
    }

    private static void RequireName(string value, string description)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException($"{description} is required.");
        }
    }
}
