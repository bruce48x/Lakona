namespace FrameworkBenchmark.Tests;

internal static class TestPaths
{
    public static string BenchmarkRoot => AppContext.BaseDirectory;

    public static string Fixture(string name)
    {
        return Path.Combine(BenchmarkRoot, "fixtures", name);
    }
}
