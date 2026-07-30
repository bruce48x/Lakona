using System.Text.RegularExpressions;
using Xunit;

namespace Lakona.RepositoryGuards.Tests;

public sealed class CrossPlatformPathRepositoryTests
{
    [Fact]
    public void Cross_platform_tooling_does_not_assume_fixed_Windows_drive_roots()
    {
        var repositoryRoot = FindRepositoryRoot();
        var fixedDriveRoot = new Regex(
            @"(?<![A-Za-z])[A-Za-z]:[\\/]",
            RegexOptions.CultureInvariant);
        var files = new[]
        {
            Path.Combine(repositoryRoot, "scripts", "game", "ci", "test-agar-three-node.ps1"),
            Path.Combine(repositoryRoot, "tests", "Lakona.ProjectSystem.Tests", "Domain", "ProjectSpecFactoryTests.cs"),
            Path.Combine(repositoryRoot, "samples", "Game.Unity.Agar", "README.md"),
            Path.Combine(repositoryRoot, ".agents", "skills", "agar-three-node-e2e", "SKILL.md")
        };
        var violations = files
            .Select(file => (File: file, Match: fixedDriveRoot.Match(File.ReadAllText(file))))
            .Where(candidate => candidate.Match.Success)
            .Select(candidate =>
                $"{Path.GetRelativePath(repositoryRoot, candidate.File)} contains {candidate.Match.Value}")
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "Cross-platform tooling must derive absolute paths from the current platform:" +
            Environment.NewLine +
            string.Join(Environment.NewLine, violations));
    }

    private static string FindRepositoryRoot()
    {
        var directory = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(directory))
        {
            if (File.Exists(Path.Combine(directory, "Lakona.slnx")) &&
                Directory.Exists(Path.Combine(directory, "src")))
                return directory;

            directory = Directory.GetParent(directory)?.FullName;
        }

        throw new InvalidOperationException("Unable to locate the repository root.");
    }
}
