using Xunit;

namespace Lakona.Hub.Tests;

public sealed class HubAotSmokeTestTests
{
    [Fact]
    public void Capture_RecognizesOnlyTheDedicatedSmokeInvocation()
    {
        Assert.Empty(HubAotSmokeTest.Capture([HubAotSmokeTest.Argument]));
        Assert.True(HubAotSmokeTest.IsRequested);

        var regular = new[] { "--some-other-argument" };
        Assert.Same(regular, HubAotSmokeTest.Capture(regular));
        Assert.False(HubAotSmokeTest.IsRequested);
    }
}
