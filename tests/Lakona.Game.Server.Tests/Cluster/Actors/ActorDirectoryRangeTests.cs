using Lakona.Game.Cluster.Actors;
using Xunit;

namespace Lakona.Game.Server.Tests.Cluster.Actors;

public sealed class ActorDirectoryRangeTests
{
    [Fact]
    public void Wrapped_range_contains_both_ring_ends()
    {
        var range = ActorDirectoryRange.Create(0xf0000000, 0x10000000);

        Assert.True(range.Contains(0xf0000000));
        Assert.True(range.Contains(0xffffffff));
        Assert.True(range.Contains(0));
        Assert.True(range.Contains(0x0fffffff));
        Assert.False(range.Contains(0x10000000));
        Assert.False(range.Contains(0x80000000));
    }

    [Fact]
    public void Difference_preserves_wrapped_remainder()
    {
        var source = ActorDirectoryRange.Create(0xf0000000, 0x20000000);
        var cut = ActorDirectoryRange.Create(0, 0x10000000);

        var result = source.Difference(cut);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, range => range.Contains(0xf0000000));
        Assert.Contains(result, range => range.Contains(0x10000000));
        Assert.DoesNotContain(result, range => range.Contains(0));
    }

    [Fact]
    public void Intersection_merges_both_ring_ends_into_one_wrapped_range()
    {
        var left = ActorDirectoryRange.Create(0xf0000000, 0x20000000);
        var right = ActorDirectoryRange.Create(0xe0000000, 0x10000000);

        var result = Assert.Single(left.Intersections(right));

        Assert.True(result.IsWrapped);
        Assert.Equal(0xf0000000u, result.Start);
        Assert.Equal(0x10000000u, result.End);
    }

    [Fact]
    public void Difference_and_intersection_match_point_membership_across_random_ranges()
    {
        var random = new Random(0x4c414b4f);
        for (var rangeIndex = 0; rangeIndex < 250; rangeIndex++)
        {
            var left = RandomRange(random);
            var right = RandomRange(random);
            var intersection = left.Intersections(right);
            var difference = left.Difference(right);

            for (var pointIndex = 0; pointIndex < 250; pointIndex++)
            {
                var point = unchecked((uint)random.NextInt64(0, (long)uint.MaxValue + 1));
                Assert.Equal(
                    left.Contains(point) && right.Contains(point),
                    intersection.Any(range => range.Contains(point)));
                Assert.Equal(
                    left.Contains(point) && !right.Contains(point),
                    difference.Any(range => range.Contains(point)));
            }
        }
    }

    private static ActorDirectoryRange RandomRange(Random random)
    {
        return random.Next(20) switch
        {
            0 => ActorDirectoryRange.Empty,
            1 => ActorDirectoryRange.Full,
            _ => ActorDirectoryRange.Create(
                unchecked((uint)random.NextInt64(0, (long)uint.MaxValue + 1)),
                unchecked((uint)random.NextInt64(0, (long)uint.MaxValue + 1)))
        };
    }
}
