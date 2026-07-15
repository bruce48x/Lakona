using FrameworkBenchmark.Contracts;

namespace FrameworkBenchmark.Coordinator;

public static class CoordinatorCli
{
    public static async Task<int> RunAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        try
        {
            var options = CoordinatorOptions.Parse(args);
            var coordinator = new BenchmarkCoordinator();
            var summary = await coordinator.RunAsync(options, cancellationToken).ConfigureAwait(false);
            var validCount = summary.Cases.Count(static result => result.IsValid);
            await output.WriteLineAsync(
                $"run={summary.RunId} valid={validCount}/{summary.Cases.Count} output={Path.Combine(Path.GetFullPath(options.OutputRoot), summary.RunId)}");
            return validCount == summary.Cases.Count ? 0 : 1;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await error.WriteLineAsync("Framework benchmark canceled.");
            return 130;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            await error.WriteLineAsync($"Framework benchmark failed: {ex.Message}");
            return 2;
        }
    }
}

public sealed record CoordinatorOptions(
    string SuitePath,
    IReadOnlyList<string> AdapterManifestPaths,
    string OutputRoot,
    string? Framework = null,
    string? Workload = null,
    bool PrepareAdapters = true)
{
    public static CoordinatorOptions Parse(IReadOnlyList<string> args)
    {
        string? suite = null;
        string? output = null;
        string? framework = null;
        string? workload = null;
        var prepareAdapters = true;
        var adapters = new List<string>();

        for (var index = 0; index < args.Count; index++)
        {
            var option = args[index];
            switch (option)
            {
                case "--suite":
                    suite = NextValue(args, ref index, option);
                    break;
                case "--adapter":
                    adapters.Add(NextValue(args, ref index, option));
                    break;
                case "--output":
                    output = NextValue(args, ref index, option);
                    break;
                case "--framework":
                    framework = NextValue(args, ref index, option);
                    break;
                case "--workload":
                    workload = NextValue(args, ref index, option);
                    break;
                case "--no-prepare":
                    prepareAdapters = false;
                    break;
                default:
                    throw new ArgumentException(
                        $"Unknown option '{option}'. Expected --suite, --adapter, --output, --framework, --workload, or --no-prepare.");
            }
        }

        if (string.IsNullOrWhiteSpace(suite))
        {
            throw new ArgumentException("--suite <path> is required.");
        }

        if (adapters.Count == 0)
        {
            throw new ArgumentException("At least one --adapter <path> is required.");
        }

        if (string.IsNullOrWhiteSpace(output))
        {
            throw new ArgumentException("--output <directory> is required.");
        }

        return new CoordinatorOptions(suite, adapters, output, framework, workload, prepareAdapters);
    }

    private static string NextValue(IReadOnlyList<string> args, ref int index, string option)
    {
        if (index + 1 >= args.Count || string.IsNullOrWhiteSpace(args[index + 1]))
        {
            throw new ArgumentException($"{option} requires a value.");
        }

        index++;
        return args[index];
    }
}
