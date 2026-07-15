using FrameworkBenchmark.Lakona.Contracts;
using FrameworkBenchmark.Lakona.Driver;
using Xunit;

namespace FrameworkBenchmark.Lakona.Tests;

public sealed class DriverConformanceTests
{
    [Theory]
    [InlineData(32)]
    [InlineData(256)]
    public void PayloadGenerationIsDeterministicAtVersionOneSizes(int size)
    {
        var first = PayloadGenerator.Create(20260715, 42, size);
        var second = PayloadGenerator.Create(20260715, 42, size);

        Assert.Equal(size, first.Length);
        Assert.Equal(first, second);
    }

    [Fact]
    public void ResponseClassificationDistinguishesCompletedOutcomes()
    {
        var payload = PayloadGenerator.Create(7, 9, 32);
        var response = new EchoResponse
        {
            RequestId = 9,
            Payload = payload,
            TerminalNode = "frontdoor-1"
        };

        Assert.Equal(EchoResponseOutcome.Succeeded, EchoResponseClassifier.Classify(response, 9, payload, "frontdoor-1"));
        Assert.Equal(EchoResponseOutcome.Corrupt, EchoResponseClassifier.Classify(response, 10, payload, "frontdoor-1"));
        Assert.Equal(EchoResponseOutcome.Misrouted, EchoResponseClassifier.Classify(response, 9, payload, "wrong"));

        response.TerminalNode = "worker-1";
        Assert.Equal(EchoResponseOutcome.Succeeded, EchoResponseClassifier.Classify(response, 9, payload, "worker-1"));
    }

    [Theory]
    [InlineData(999, 999)]
    [InlineData(1001, 1010)]
    [InlineData(21657, 21700)]
    public void HistogramBucketQuantizationRoundsUp(long value, long expected)
    {
        Assert.Equal(expected, HistogramBucketQuantizer.UpperBound(value, 3));
    }

    [Theory]
    [InlineData("entity/0", "worker-2")]
    [InlineData("entity/1", "worker-1")]
    [InlineData("entity/42", "worker-2")]
    public void RoutedTargetOwnershipIsStable(string targetKey, string expectedOwner)
    {
        Assert.Equal(expectedOwner, BenchmarkRouting.Owner(targetKey));
    }
}
