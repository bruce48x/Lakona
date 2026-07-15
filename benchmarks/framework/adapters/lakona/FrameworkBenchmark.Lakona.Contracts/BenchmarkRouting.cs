using System.Text;

namespace FrameworkBenchmark.Lakona.Contracts;

public static class BenchmarkRouting
{
    public const int TargetCount = 256;

    public static string TargetKey(long requestId) => $"entity/{requestId % TargetCount}";

    public static string Owner(string targetKey)
    {
        var hash = 2166136261u;
        foreach (var value in Encoding.UTF8.GetBytes(targetKey))
        {
            hash ^= value;
            hash *= 16777619u;
        }

        return (hash & 1) == 0 ? "worker-1" : "worker-2";
    }
}
