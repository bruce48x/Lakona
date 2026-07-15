using FrameworkBenchmark.Lakona.Contracts;

namespace FrameworkBenchmark.Lakona.Driver;

public enum EchoResponseOutcome
{
    Succeeded,
    Corrupt,
    Misrouted
}

public static class EchoResponseClassifier
{
    public static EchoResponseOutcome Classify(
        EchoResponse response,
        long requestId,
        ReadOnlySpan<byte> payload,
        string terminalNode)
    {
        if (response.RequestId != requestId || !response.Payload.AsSpan().SequenceEqual(payload))
        {
            return EchoResponseOutcome.Corrupt;
        }

        return response.TerminalNode == terminalNode
            ? EchoResponseOutcome.Succeeded
            : EchoResponseOutcome.Misrouted;
    }
}

public static class PayloadGenerator
{
    public static byte[] Create(int seed, long requestId, int size)
    {
        var payload = new byte[size];
        for (var index = 0; index < payload.Length; index++)
        {
            payload[index] = unchecked((byte)((seed * 31L) + (requestId * 17L) + (index * 13L)));
        }

        return payload;
    }
}

public static class HistogramBucketQuantizer
{
    public static long UpperBound(long value, int significantDigits)
    {
        var scale = 1L;
        var threshold = (long)Math.Pow(10, significantDigits);
        var reduced = value;
        while (reduced >= threshold)
        {
            reduced /= 10;
            scale *= 10;
        }

        return checked(((value + scale - 1) / scale) * scale);
    }
}
