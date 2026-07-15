using System.Globalization;
using FrameworkBenchmark.Contracts;

namespace FrameworkBenchmark.Coordinator;

public sealed class BenchmarkCoordinator
{
    public async Task<RunSummary> RunAsync(CoordinatorOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        var suitePath = Path.GetFullPath(options.SuitePath);
        var suite = BenchmarkJson.Read<BenchmarkSuite>(suitePath);
        BenchmarkDefinitionValidator.Validate(suite);

        var manifests = LoadManifests(options.AdapterManifestPaths);
        foreach (var framework in suite.Frameworks)
        {
            if (!manifests.ContainsKey(framework))
            {
                throw new BenchmarkToolException($"Suite '{suite.Id}' requires adapter '{framework}', but no manifest was supplied.");
            }
        }

        var runId = $"{DateTimeOffset.UtcNow:yyyyMMddTHHmmssZ}-{Guid.NewGuid():N}"[..26];
        var runDirectory = Path.Combine(Path.GetFullPath(options.OutputRoot), runId);
        Directory.CreateDirectory(runDirectory);
        Directory.CreateDirectory(Path.Combine(runDirectory, "logs"));
        Directory.CreateDirectory(Path.Combine(runDirectory, "histograms"));
        Directory.CreateDirectory(Path.Combine(runDirectory, "work"));
        var startedAt = DateTimeOffset.UtcNow;
        var validatedResults = new List<ValidatedCaseResult>();
        var manifestPath = Path.Combine(runDirectory, "run-manifest.json");
        WriteRunManifest(manifestPath, runId, suite.Id, "running", startedAt, null, null);

        try
        {
            foreach (var benchmarkCase in SuiteExpander.Expand(suite))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (manifest, adapterManifestPath) = manifests[benchmarkCase.Framework];
                if (!manifest.SupportedWorkloads.Contains(benchmarkCase.Workload, StringComparer.Ordinal))
                {
                    throw new BenchmarkToolException(
                        $"Adapter '{manifest.Framework}' does not support workload '{benchmarkCase.Workload}'.");
                }

                validatedResults.Add(await RunCaseAsync(
                    runId,
                    runDirectory,
                    benchmarkCase,
                    manifest,
                    adapterManifestPath,
                    cancellationToken).ConfigureAwait(false));
            }

            var finishedAt = DateTimeOffset.UtcNow;
            var summary = new RunSummary(
                BenchmarkSchemaVersions.V1,
                runId,
                suite.Id,
                "local-dev",
                "native",
                startedAt,
                finishedAt,
                validatedResults);
            BenchmarkJson.Write(Path.Combine(runDirectory, "summary.json"), summary);
            BenchmarkJson.Write(
                Path.Combine(runDirectory, "validation.json"),
                validatedResults.Select(static item => new { item.Case.Id, item.IsValid, item.Errors }).ToArray());
            BenchmarkReportWriter.Write(Path.Combine(runDirectory, "report.md"), summary);
            WriteRunManifest(manifestPath, runId, suite.Id, "complete", startedAt, finishedAt, null);
            return summary;
        }
        catch (Exception ex)
        {
            WriteRunManifest(
                manifestPath,
                runId,
                suite.Id,
                "incomplete",
                startedAt,
                DateTimeOffset.UtcNow,
                ex.Message);
            throw;
        }
    }

    private static Dictionary<string, (AdapterManifest Manifest, string Path)> LoadManifests(
        IReadOnlyList<string> manifestPaths)
    {
        var manifests = new Dictionary<string, (AdapterManifest, string)>(StringComparer.Ordinal);
        foreach (var path in manifestPaths)
        {
            var fullPath = Path.GetFullPath(path);
            var manifest = BenchmarkJson.Read<AdapterManifest>(fullPath);
            BenchmarkDefinitionValidator.Validate(manifest);
            if (!manifests.TryAdd(manifest.Framework, (manifest, fullPath)))
            {
                throw new BenchmarkToolException($"Duplicate adapter manifest for framework '{manifest.Framework}'.");
            }
        }

        return manifests;
    }

    private static async Task<ValidatedCaseResult> RunCaseAsync(
        string runId,
        string runDirectory,
        BenchmarkCase benchmarkCase,
        AdapterManifest manifest,
        string manifestPath,
        CancellationToken cancellationToken)
    {
        var safeCaseId = SafeFileName(benchmarkCase.Id);
        var workDirectory = Path.Combine(runDirectory, "work", safeCaseId);
        Directory.CreateDirectory(workDirectory);
        var caseFile = Path.Combine(workDirectory, "case-command.json");
        var resultFile = Path.Combine(workDirectory, "case-result.json");
        var adapterRoot = Path.GetDirectoryName(manifestPath)!;
        var portValues = PortAllocator.Allocate(manifest.Servers.SelectMany(static server => server.Ports));
        var placeholders = new Dictionary<string, string>(portValues, StringComparer.Ordinal)
        {
            ["adapterRoot"] = adapterRoot,
            ["runDir"] = runDirectory,
            ["caseFile"] = caseFile,
            ["resultFile"] = resultFile,
            ["caseId"] = benchmarkCase.Id,
            ["framework"] = benchmarkCase.Framework
        };
        var servers = new List<ManagedServerProcess>();
        var endpoints = new Dictionary<string, string>(StringComparer.Ordinal);

        try
        {
            foreach (var server in manifest.Servers)
            {
                var process = ManagedServerProcess.Start(
                    CommandTemplateExpander.Expand(server.Command, placeholders),
                    adapterRoot,
                    server.Role,
                    Path.Combine(runDirectory, "logs", $"{safeCaseId}-{server.Role}.stdout.log"),
                    Path.Combine(runDirectory, "logs", $"{safeCaseId}-{server.Role}.stderr.log"));
                servers.Add(process);
                var readyEndpoints = await process.WaitForReadyAsync(
                    TimeSpan.FromMilliseconds(benchmarkCase.Timing.ReadinessTimeoutMilliseconds),
                    cancellationToken).ConfigureAwait(false);
                foreach (var endpoint in readyEndpoints)
                {
                    if (!endpoints.TryAdd(endpoint.Key, endpoint.Value))
                    {
                        throw new BenchmarkToolException(
                            $"Adapter '{manifest.Framework}' reported duplicate endpoint '{endpoint.Key}'.");
                    }
                }
            }

            BenchmarkJson.Write(
                caseFile,
                new CaseCommand(
                    BenchmarkSchemaVersions.V1,
                    runId,
                    benchmarkCase.Id,
                    benchmarkCase.Framework,
                    benchmarkCase.Workload,
                    benchmarkCase.PayloadSize,
                    benchmarkCase.Concurrency,
                    benchmarkCase.Concurrency,
                    benchmarkCase.Seed,
                    benchmarkCase.Timing,
                    benchmarkCase.Histogram,
                    endpoints));

            var driverTimeoutMilliseconds = checked(
                benchmarkCase.Timing.WarmupMilliseconds +
                benchmarkCase.Timing.MeasurementMilliseconds +
                benchmarkCase.Timing.DrainTimeoutMilliseconds +
                benchmarkCase.Timing.RequestTimeoutMilliseconds + 10000);
            var exitCode = await ProcessCommandRunner.RunAsync(
                CommandTemplateExpander.Expand(manifest.Driver, placeholders),
                adapterRoot,
                Path.Combine(runDirectory, "logs", $"{safeCaseId}-driver.stdout.log"),
                Path.Combine(runDirectory, "logs", $"{safeCaseId}-driver.stderr.log"),
                TimeSpan.FromMilliseconds(driverTimeoutMilliseconds),
                cancellationToken).ConfigureAwait(false);
            if (exitCode != 0)
            {
                throw new BenchmarkToolException(
                    $"Driver for case '{benchmarkCase.Id}' exited with code {exitCode}.");
            }

            foreach (var server in servers)
            {
                server.EnsureHealthy();
            }

            if (!File.Exists(resultFile))
            {
                throw new BenchmarkToolException($"Driver for case '{benchmarkCase.Id}' did not write '{resultFile}'.");
            }

            var result = BenchmarkJson.Read<CaseResult>(resultFile);
            var validated = CaseResultValidator.Validate(benchmarkCase, result);
            BenchmarkJson.Write(Path.Combine(runDirectory, "histograms", $"{safeCaseId}.json"), result.Histogram);
            return validated;
        }
        finally
        {
            for (var index = servers.Count - 1; index >= 0; index--)
            {
                await servers[index].DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private static string SafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
    }

    private static void WriteRunManifest(
        string path,
        string runId,
        string suiteId,
        string status,
        DateTimeOffset startedAt,
        DateTimeOffset? finishedAt,
        string? toolError)
    {
        BenchmarkJson.Write(
            path,
            new RunManifest(
                BenchmarkSchemaVersions.V1,
                runId,
                suiteId,
                "local-dev",
                "native",
                status,
                startedAt,
                finishedAt,
                toolError));
    }
}
