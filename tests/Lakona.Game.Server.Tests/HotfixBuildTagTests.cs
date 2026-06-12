using System.Reflection;
using Lakona.Game.Server.Hotfix.BuildTag;
using Xunit;

[assembly: AssemblyMetadata("LakonaHotfixBuildTag", "test-build-tag")]

namespace Lakona.Game.Server.Tests;

public sealed class HotfixBuildTagTests
{
    [Fact]
    public void Get_returns_build_tag_from_assembly_metadata()
    {
        Assert.Equal("test-build-tag", HotfixBuildTag.Get(typeof(HotfixBuildTagTests).Assembly));
    }
}
