namespace FrameworkBenchmark.Tests;

internal static class TestPaths
{
    public static string BenchmarkRoot { get; } = FindBenchmarkRoot();

    public static string Fixture(string name)
    {
        return Path.Combine(BenchmarkRoot, "tests", "fixtures", name);
    }

    private static string FindBenchmarkRoot()
    {
        var directory = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(directory))
        {
            if (File.Exists(Path.Combine(directory, "FrameworkBenchmark.slnx")) &&
                Directory.Exists(Path.Combine(directory, "schemas")))
            {
                return directory;
            }

            directory = Directory.GetParent(directory)?.FullName ?? string.Empty;
        }

        throw new InvalidOperationException("Could not locate the framework benchmark root.");
    }
}
