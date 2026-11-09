using System.Numerics;

namespace Lakona.Game.LoadTesting.Internal;

// Exact ticks below 64, then 32 equal-width buckets per power of two.
// Upper bounds never underestimate a sample and have less than 1/32 relative error.
internal sealed class LatencyHistogram
{
    internal const int BucketCount = 1888;
    private readonly long[] buckets = new long[BucketCount];
    private long totalTicksLow;
    private long totalTicksHigh;

    public void Record(long ticks)
    {
        var shift = Math.Max(0, BitOperations.Log2((ulong)ticks) - 5);
        var index = (shift * 32) + (int)((ulong)ticks >> shift);

        // An unsigned two-word sum preserves the exact average even when several
        // valid TimeSpans overflow Int64. Only a carry needs the second atomic add.
        var low = unchecked((ulong)Interlocked.Add(ref totalTicksLow, ticks));
        if (low < (ulong)ticks)
        {
            Interlocked.Increment(ref totalTicksHigh);
        }

        Interlocked.Increment(ref buckets[index]);
    }

    // Like the run summary, a final snapshot is taken after all writers finish.
    public Snapshot CreateSnapshot()
    {
        var counts = new long[BucketCount];
        long count = 0;
        for (var index = 0; index < counts.Length; index++)
        {
            counts[index] = Volatile.Read(ref buckets[index]);
            count += counts[index];
        }

        var ticks = ((UInt128)(ulong)Volatile.Read(ref totalTicksHigh) << 64)
            | (ulong)Volatile.Read(ref totalTicksLow);
        var average = count == 0 ? 0 : (long)(ticks / (ulong)count);
        return new Snapshot(counts, count, TimeSpan.FromTicks(average));
    }

    internal sealed record Snapshot(long[] Counts, long Count, TimeSpan Average)
    {
        public TimeSpan Percentile(int percent)
        {
            if (Count == 0)
            {
                return TimeSpan.Zero;
            }

            var rank = (Count / 100 * percent) + ((Count % 100 * percent + 99) / 100);
            long cumulative = 0;
            for (var index = 0; index < Counts.Length; index++)
            {
                cumulative += Counts[index];
                if (cumulative >= rank)
                {
                    var shift = Math.Max(0, (index / 32) - 1);
                    var significand = index < 64 ? index : 32 + (index % 32);
                    var upperTicks = (((ulong)significand + 1) << shift) - 1;
                    return TimeSpan.FromTicks((long)upperTicks);
                }
            }

            throw new InvalidOperationException("Histogram count does not match its buckets.");
        }
    }
}
