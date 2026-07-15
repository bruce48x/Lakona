using Xunit;

namespace Lakona.RepositoryGuards.Tests;

public sealed class PublicRemovedAuthoringRepositoryTests
{
    [Fact]
    public void User_facing_sources_do_not_expose_removed_authoring()
    {
        var repositoryRoot = FindRepositoryRoot();
        var bannedTerms = new[]
        {
            string.Concat("Hotfix", "Fea", "tureAttribute"),
            string.Concat("HotfixGame", "Fea", "ture"),
            string.Concat("Hotfix", "Fea", "tureContext"),
            string.Concat("Hotfix", "Fea", "tureDeclaration"),
            string.Concat("Hotfix", "Fea", "tureStartCall"),
            string.Concat("Hotfix", "Fea", "tureState"),
            string.Concat("Hotfix", "Fea", "tureStopCall"),
            string.Concat("Fea", "tureCommandAttribute"),
            string.Concat("Fea", "tureCommandClient"),
            string.Concat("Fea", "tureCommandId"),
            string.Concat("Fea", "tureMessage"),
            string.Concat("Fea", "tureName"),
            string.Concat("LakonaGame", "Fea", "ture"),
            string.Concat("I", "Fea", "tureCommandClient"),
            string.Concat("I", "Fea", "tureMessage"),
            string.Concat("Lakona:", "Fea", "ture")
        };
        var violations = new List<string>();

        foreach (var file in EnumerateUserFacingTextFiles(repositoryRoot))
        {
            var text = File.ReadAllText(file);
            foreach (var term in bannedTerms)
            {
                if (text.Contains(term, StringComparison.Ordinal))
                    violations.Add($"{NormalizeRelativePath(repositoryRoot, file)} contains {term}");
            }
        }

        Assert.True(
            violations.Count == 0,
            "Removed authoring model must not be exposed in user-facing sources:" +
            Environment.NewLine +
            string.Join(Environment.NewLine, violations.Order(StringComparer.Ordinal)));
    }

    private static IEnumerable<string> EnumerateUserFacingTextFiles(string repositoryRoot)
    {
        foreach (var root in new[] { "src", "samples", "docs" })
        {
            var absoluteRoot = Path.Combine(repositoryRoot, root);
            if (!Directory.Exists(absoluteRoot))
                continue;

            foreach (var file in Directory.EnumerateFiles(absoluteRoot, "*", SearchOption.AllDirectories))
            {
                if (IsIgnoredPath(repositoryRoot, file) || !IsTextFile(file))
                    continue;

                yield return file;
            }
        }

        yield return Path.Combine(repositoryRoot, "README.md");
    }

    private static bool IsIgnoredPath(string repositoryRoot, string file)
    {
        var relative = NormalizeRelativePath(repositoryRoot, file);
        return relative.StartsWith("docs/plans/", StringComparison.Ordinal) ||
               relative.Contains("/bin/", StringComparison.Ordinal) ||
               relative.Contains("/obj/", StringComparison.Ordinal) ||
               relative.Contains("/_artifacts/", StringComparison.Ordinal) ||
               relative.Contains("/Library/", StringComparison.Ordinal) ||
               relative.Contains("/Temp/", StringComparison.Ordinal);
    }

    private static bool IsTextFile(string file)
    {
        return Path.GetExtension(file).ToLowerInvariant() is
            ".cs" or
            ".csproj" or
            ".md" or
            ".json" or
            ".yml" or
            ".yaml" or
            ".props" or
            ".targets";
    }

    private static string FindRepositoryRoot()
    {
        var directory = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(directory))
        {
            if (File.Exists(Path.Combine(directory, "Lakona.slnx")) && Directory.Exists(Path.Combine(directory, "src")))
                return directory;

            directory = Directory.GetParent(directory)?.FullName ?? string.Empty;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }

    private static string NormalizeRelativePath(string repositoryRoot, string path)
    {
        return Path.GetRelativePath(repositoryRoot, path)
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');
    }
}
