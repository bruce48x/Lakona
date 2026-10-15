using System.Text.RegularExpressions;
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

    [Fact]
    public void Public_skill_names_prompts_and_local_references_resolve_in_the_bundled_pack()
    {
        var repositoryRoot = GitChangeSetReader.FindRepositoryRoot();
        var packRoot = Path.Combine(repositoryRoot, "skills");
        foreach (var skillFile in GetSkillFiles(repositoryRoot, "skills"))
        {
            var skillDirectory = Path.GetDirectoryName(skillFile)!;
            var name = Path.GetFileName(skillDirectory);
            Assert.Matches($@"(?m)^name: {Regex.Escape(name)}$", ReadNormalizedFrontmatter(skillFile));

            var metadata = File.ReadAllText(Path.Combine(skillDirectory, "agents", "openai.yaml"));
            var prompt = Regex.Match(metadata, "(?m)^  default_prompt: \"(?<prompt>[^\"\\r\\n]+)\"\\r?$");
            Assert.True(prompt.Success, $"Missing quoted default_prompt: {name}");
            var targets = Regex.Matches(prompt.Groups["prompt"].Value, @"\$(?<name>lakona-[a-z0-9-]+)")
                .Select(match => match.Groups["name"].Value).ToArray();
            Assert.Contains(name, targets);
            Assert.All(targets, target => Assert.True(
                File.Exists(Path.Combine(packRoot, target, "SKILL.md")),
                $"Unknown Skill prompt target: {name} -> {target}"));

            foreach (var document in Directory.EnumerateFiles(skillDirectory, "*.md", SearchOption.AllDirectories))
            foreach (Match link in Regex.Matches(File.ReadAllText(document), @"\[[^\]\r\n]+\]\((?<target>[^)\s]+)\)"))
            {
                var target = link.Groups["target"].Value.Split('#')[0];
                if (target.Length == 0 || Uri.TryCreate(target, UriKind.Absolute, out _)) continue;
                var fullPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(document)!, target));
                Assert.True(fullPath.StartsWith(packRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal),
                    $"Public Skill depends on a file outside the bundled pack: {document} -> {target}");
                Assert.True(File.Exists(fullPath), $"Missing public Skill reference: {document} -> {target}");
            }
        }
    }

    [Fact]
    public void Public_skills_do_not_reintroduce_removed_module_apis_or_workflows()
    {
        var repositoryRoot = GitChangeSetReader.FindRepositoryRoot();
        var files = GetSkillFiles(repositoryRoot, "skills")
            .SelectMany(skill => Directory.EnumerateFiles(Path.GetDirectoryName(skill)!, "*", SearchOption.AllDirectories))
            .Append(Path.Combine(repositoryRoot, "docs", "tool", "agent-skills.md"));
        const string removed = @"\bILakonaModule\b|\blakona-implement-module\b|\blakona-test-cluster\b|Server\.App\s+modules\b|application(?:-resource)?\s+modules\b|\bModule\s+Skill\b";
        foreach (var file in files)
        {
            Assert.False(Regex.IsMatch(File.ReadAllText(file), removed, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
                $"Removed Module API/Skill guidance in {Path.GetRelativePath(repositoryRoot, file)}");
        }
    }

    [Fact]
    public void Repository_private_skill_document_references_exist()
    {
        var repositoryRoot = GitChangeSetReader.FindRepositoryRoot();

        foreach (var skillFile in GetSkillFiles(repositoryRoot, ".agents/skills"))
        {
            var content = File.ReadAllText(skillFile);
            foreach (Match match in Regex.Matches(
                         content,
                         "`(?<path>docs/[A-Za-z0-9._/-]+\\.md)`",
                         RegexOptions.CultureInvariant))
            {
                var relativePath = match.Groups["path"].Value;
                var fullPath = Path.Combine(repositoryRoot, relativePath);
                Assert.True(
                    File.Exists(fullPath),
                    $"Skill document reference does not exist: {Path.GetRelativePath(repositoryRoot, skillFile)} -> {relativePath}");
            }
        }
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
