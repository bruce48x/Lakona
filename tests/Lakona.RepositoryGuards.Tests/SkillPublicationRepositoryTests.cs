using Lakona.RepositoryGuards.Tests.PackageVersions;
using Xunit;

namespace Lakona.RepositoryGuards.Tests;

public sealed class SkillPublicationRepositoryTests
{
    [Fact]
    public void Repository_private_skills_are_internal_and_public_skills_are_discoverable()
    {
        var repositoryRoot = GitChangeSetReader.FindRepositoryRoot();

        foreach (var skillFile in GetSkillFiles(repositoryRoot, ".agents/skills"))
        {
            Assert.Contains(
                "\nmetadata:\n  internal: true\n",
                $"\n{ReadNormalizedFrontmatter(skillFile)}\n",
                StringComparison.Ordinal);
        }

        foreach (var skillFile in GetSkillFiles(repositoryRoot, "skills"))
        {
            Assert.DoesNotContain(
                "\nmetadata:\n  internal: true\n",
                $"\n{ReadNormalizedFrontmatter(skillFile)}\n",
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Public_skills_are_bundled_and_smoke_tested_in_release_artifacts()
    {
        var repositoryRoot = GitChangeSetReader.FindRepositoryRoot();
        var project = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "Lakona.ProjectSystem",
            "Lakona.ProjectSystem.csproj"));
        var workflow = File.ReadAllText(Path.Combine(
            repositoryRoot,
            ".github",
            "workflows",
            "publish-nuget.yml"));
        var hubSmoke = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "Lakona.Hub",
            "HubAotSmokeTest.cs"));

        Assert.Contains(@"..\..\skills\**\*", project, StringComparison.Ordinal);
        Assert.Contains("Lakona.ProjectSystem.SkillPack/", project, StringComparison.Ordinal);
        Assert.Contains("Smoke test packed Lakona.Tool Skill Pack", workflow, StringComparison.Ordinal);
        Assert.Contains("SkillSmoke/.agents/skills", workflow, StringComparison.Ordinal);
        Assert.Contains("BundledSkillNames", hubSmoke, StringComparison.Ordinal);
        Assert.Contains(".agents\", \"skills", hubSmoke, StringComparison.Ordinal);
    }

    private static IEnumerable<string> GetSkillFiles(string repositoryRoot, string relativeRoot)
    {
        var root = Path.Combine(repositoryRoot, relativeRoot);
        return Directory
            .GetDirectories(root)
            .Select(directory => Path.Combine(directory, "SKILL.md"))
            .Where(File.Exists);
    }

    private static string ReadNormalizedFrontmatter(string skillFile)
    {
        var content = File.ReadAllText(skillFile).Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.StartsWith("---\n", content, StringComparison.Ordinal);

        var end = content.IndexOf("\n---\n", 4, StringComparison.Ordinal);
        Assert.True(end >= 0, $"Skill frontmatter is not terminated: {skillFile}");
        return content[4..end];
    }
}
