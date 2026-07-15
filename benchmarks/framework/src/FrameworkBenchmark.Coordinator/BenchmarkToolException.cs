namespace FrameworkBenchmark.Coordinator;

public sealed class BenchmarkToolException : Exception
{
    public BenchmarkToolException(string message)
        : base(message)
    {
    }

    public BenchmarkToolException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
