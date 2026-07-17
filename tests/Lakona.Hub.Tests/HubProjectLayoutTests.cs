using Xunit;

namespace Lakona.Hub.Tests;

public sealed class HubProjectLayoutTests
{
    [Theory]
    [InlineData(1000, false)]
    [InlineData(1179, false)]
    [InlineData(1180, true)]
    [InlineData(2048, true)]
    public void UseWideLayout_SwitchesAtTheSupportedBreakpoint(double width, bool expected)
    {
        Assert.Equal(expected, HubProjectLayout.UseWideLayout(width));
    }
}
