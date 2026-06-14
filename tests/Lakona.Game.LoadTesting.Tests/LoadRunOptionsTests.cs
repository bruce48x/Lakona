using Lakona.Game.LoadTesting;
using Xunit;

namespace Lakona.Game.LoadTesting.Tests;

public sealed class LoadRunOptionsTests
{
    [Fact]
    public void Constructor_ValidValues_AssignsProperties()
    {
        var options = new LoadRunOptions(Users: 3, RampUp: TimeSpan.FromSeconds(2), Duration: TimeSpan.FromSeconds(5));

        Assert.Equal(3, options.Users);
        Assert.Equal(TimeSpan.FromSeconds(2), options.RampUp);
        Assert.Equal(TimeSpan.FromSeconds(5), options.Duration);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_InvalidUsers_Throws(int users)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new LoadRunOptions(users, TimeSpan.Zero, TimeSpan.FromSeconds(1)));

        Assert.Equal("Users", ex.ParamName);
    }

    [Fact]
    public void Constructor_NegativeRampUp_Throws()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new LoadRunOptions(1, TimeSpan.FromMilliseconds(-1), TimeSpan.FromSeconds(1)));

        Assert.Equal("RampUp", ex.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_InvalidDuration_Throws(int milliseconds)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new LoadRunOptions(1, TimeSpan.Zero, TimeSpan.FromMilliseconds(milliseconds)));

        Assert.Equal("Duration", ex.ParamName);
    }
}
