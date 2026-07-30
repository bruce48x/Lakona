using System.Reflection;
using Lakona.Game.Server.Hotfix.BuildTag;
using Xunit;

[assembly: AssemblyMetadata("LakonaBuildTag", "TestBuild1")]

namespace Lakona.Game.Server.Tests;

public sealed class HotfixBuildTagTests
{
    [Fact]
    public void Get_returns_build_tag_from_assembly_metadata()
    {
        Assert.Equal("TestBuild1", HotfixBuildTag.Get(typeof(HotfixBuildTagTests).Assembly));
    }
}
