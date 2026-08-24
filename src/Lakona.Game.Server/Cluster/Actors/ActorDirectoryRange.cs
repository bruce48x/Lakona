namespace Lakona.Game.Cluster.Actors;

using Lakona.Game.Server.Actors;

/// <summary>
/// A half-open range on the 32-bit Actor Directory hash ring.
/// </summary>
internal readonly record struct ActorDirectoryRange
{
    private const ulong RingSize = (ulong)uint.MaxValue + 1;

    private ActorDirectoryRange(uint start, uint end, bool isFull, bool isEmpty)
    {
        Start = start;
        End = end;
        IsFull = isFull;
        IsEmpty = isEmpty;
    }

    public uint Start { get; }

    public uint End { get; }

    public bool IsFull { get; }

    public bool IsEmpty { get; }

    public bool IsWrapped => !IsFull && !IsEmpty && Start > End;

    public static ActorDirectoryRange Empty { get; } = new(0, 0, false, true);

    public static ActorDirectoryRange Full { get; } = new(0, 0, true, false);

    public static ActorDirectoryRange Create(uint start, uint end) =>
        start == end ? Empty : new ActorDirectoryRange(start, end, false, false);

    public bool Contains(uint value)
    {
        if (IsFull) return true;
        if (IsEmpty) return false;
        return Start < End
            ? value >= Start && value < End
            : value >= Start || value < End;
    }

    public bool Contains(ActorId actorId) => Contains(ActorDirectoryRing.Hash(actorId));

    public bool Intersects(ActorDirectoryRange other) => Intersections(other).Count != 0;

    public IReadOnlyList<ActorDirectoryRange> Intersections(ActorDirectoryRange other)
    {
        if (IsEmpty || other.IsEmpty) return [];
        if (IsFull) return [other];
        if (other.IsFull) return [this];

        var result = new List<ActorDirectoryRange>(2);
        foreach (var left in ToLinearIntervals())
        foreach (var right in other.ToLinearIntervals())
        {
            var start = Math.Max(left.Start, right.Start);
            var end = Math.Min(left.End, right.End);
            if (start < end) AddLinear(result, start, end);
        }

        return MergeRingEnds(result);
    }

    public IReadOnlyList<ActorDirectoryRange> Difference(ActorDirectoryRange other)
    {
        if (IsEmpty || other.IsFull) return [];
        if (other.IsEmpty) return [this];

        var remaining = ToLinearIntervals().ToList();
        foreach (var cut in other.ToLinearIntervals())
        {
            var next = new List<LinearInterval>(remaining.Count + 1);
            foreach (var source in remaining)
            {
                if (cut.End <= source.Start || cut.Start >= source.End)
                {
                    next.Add(source);
                    continue;
                }

                if (source.Start < cut.Start)
                    next.Add(new LinearInterval(source.Start, cut.Start));
                if (cut.End < source.End)
                    next.Add(new LinearInterval(cut.End, source.End));
            }

            remaining = next;
        }

        var result = new List<ActorDirectoryRange>(remaining.Count);
        foreach (var interval in remaining) AddLinear(result, interval.Start, interval.End);
        return MergeRingEnds(result);
    }

    public override string ToString()
    {
        if (IsFull) return "full";
        if (IsEmpty) return "empty";
        return $"[{Start:x8},{End:x8})";
    }

    private IReadOnlyList<LinearInterval> ToLinearIntervals()
    {
        if (IsEmpty) return [];
        if (IsFull) return [new LinearInterval(0, RingSize)];
        if (Start < End) return [new LinearInterval(Start, End)];
        return
        [
            new LinearInterval(Start, RingSize),
            new LinearInterval(0, End)
        ];
    }

    private static void AddLinear(List<ActorDirectoryRange> result, ulong start, ulong end)
    {
        if (start == 0 && end == RingSize)
        {
            result.Add(Full);
            return;
        }

        result.Add(new ActorDirectoryRange(
            unchecked((uint)start),
            unchecked((uint)end),
            false,
            false));
    }

    private static IReadOnlyList<ActorDirectoryRange> MergeRingEnds(List<ActorDirectoryRange> ranges)
    {
        if (ranges.Count < 2 || ranges.Any(static range => range.IsFull)) return ranges;

        var high = ranges.FindIndex(static range => range.End == 0 && range.Start != 0);
        var low = ranges.FindIndex(static range => range.Start == 0 && range.End != 0);
        if (high < 0 || low < 0) return ranges;

        var merged = new ActorDirectoryRange(ranges[high].Start, ranges[low].End, false, false);
        var result = new List<ActorDirectoryRange>(ranges.Count - 1) { merged };
        for (var i = 0; i < ranges.Count; i++)
        {
            if (i != high && i != low) result.Add(ranges[i]);
        }

        return result;
    }

    private readonly record struct LinearInterval(ulong Start, ulong End);
}
