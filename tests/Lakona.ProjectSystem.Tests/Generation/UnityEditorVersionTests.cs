using Lakona.ProjectSystem.Generation.Domain;
using Xunit;

namespace Lakona.ProjectSystem.Tests.Generation;

public sealed class UnityEditorVersionTests
{
    [Theory]
    [InlineData("6000.3.13f1", "6000.3.3f1")]
    [InlineData("2022.3.72f1", "2022.3.62f3c1")]
    [InlineData("2022.3.61t8", "2022.3.61t8")]
    public void IsCompatible_MatchesMajorAndMinorOnly(string actual, string expected)
    {
        Assert.True(UnityEditorVersion.IsCompatible(actual, expected));
    }

    [Theory]
    [InlineData("6000.4.13f1", "6000.3.3f1")]
    [InlineData("2021.3.72f1", "2022.3.62f3c1")]
    [InlineData("not-a-version", "2022.3.62f3c1")]
    public void IsCompatible_RejectsDifferentStreams(string actual, string expected)
    {
        Assert.False(UnityEditorVersion.IsCompatible(actual, expected));
    }
}
