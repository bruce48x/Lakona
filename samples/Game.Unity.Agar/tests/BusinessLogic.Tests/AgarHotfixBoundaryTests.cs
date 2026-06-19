using System.Text.RegularExpressions;
using Xunit;

namespace BusinessLogic.Tests;

public sealed class AgarHotfixBoundaryTests
{
    [Fact]
    public void Stable_state_actors_do_not_declare_business_methods()
    {
        var stateRoot = FindRepositoryFile("samples/Game.Unity.Agar/Server/App/State/StateStores.cs")
            .DirectoryName!;
        var actorFiles = Directory.GetFiles(stateRoot, "*Actor.cs", SearchOption.AllDirectories);
        var forbidden = new Regex(
            @"^\s*(public|internal|private|protected)\s+(static\s+|override\s+|async\s+)*(Task|ValueTask|[\w<>]+)\s+\w+\s*\(",
            RegexOptions.Multiline);

        foreach (var file in actorFiles)
        {
            var text = File.ReadAllText(file);
            var matches = forbidden.Matches(text)
                .Where(match => !match.Value.Contains("OnActivateAsync", StringComparison.Ordinal))
                .Where(match => !match.Value.Contains("OnDeactivateAsync", StringComparison.Ordinal))
                .ToArray();

            Assert.True(matches.Length == 0, $"{Path.GetRelativePath(Directory.GetCurrentDirectory(), file)} declares stable actor methods: {string.Join(", ", matches.Select(match => match.Value.Trim()))}");
        }
    }

    private static FileInfo FindRepositoryFile(string relativePath)
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return new FileInfo(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(relativePath);
    }
}
