using FrameworkBenchmark.Coordinator;
using Xunit;

namespace FrameworkBenchmark.Tests;

public sealed class CoordinatorOptionsTests
{
    [Fact]
    public void Parse_AcceptsFrameworkWorkloadAndNoPrepareSelection()
    {
        var options = CoordinatorOptions.Parse([
            "--suite", "suite.json",
            "--adapter", "adapter.json",
            "--output", "output",
            "--framework", "pinus",
            "--workload", "frontdoor.echo",
            "--no-prepare"
        ]);

        Assert.Equal("pinus", options.Framework);
        Assert.Equal("frontdoor.echo", options.Workload);
        Assert.False(options.PrepareAdapters);
    }
}
