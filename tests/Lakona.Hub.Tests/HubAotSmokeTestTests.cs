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

    [Fact]
    public void NativeAotSmoke_CoversEveryPublicSkill()
    {
        var repositoryRoot = FindRepositoryRoot();
        var expected = Directory
            .GetDirectories(Path.Combine(repositoryRoot, "skills"))
            .Where(directory => File.Exists(Path.Combine(directory, "SKILL.md")))
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected, HubAotSmokeTest.BundledSkillNames.Order(StringComparer.Ordinal));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CONTRIBUTING.md")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not find the Lakona repository root.");
    }
}
