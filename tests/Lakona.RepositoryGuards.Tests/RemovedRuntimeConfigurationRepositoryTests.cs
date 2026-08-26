using Xunit;

namespace Lakona.RepositoryGuards.Tests;

public sealed class RemovedRuntimeConfigurationRepositoryTests
{
    [Fact]
    public void Automation_does_not_inject_removed_runtime_configuration()
    {
        var repositoryRoot = FindRepositoryRoot();
        var removedKeys = new[]
        {
            string.Concat("Lakona__", "Actor", "Hosts"),
            string.Concat("Lakona__", "Startup", "Actors")
        };
        var violations = new List<string>();

        foreach (var root in new[] { "scripts", ".github", "samples" })
        {
            var absoluteRoot = Path.Combine(repositoryRoot, root);
            foreach (var file in Directory.EnumerateFiles(
                         absoluteRoot,
                         "*",
                         SearchOption.AllDirectories)
                     .Where(IsRuntimeConfigurationFile))
            {
                var text = File.ReadAllText(file);
                foreach (var removedKey in removedKeys)
                {
                    if (text.Contains(removedKey, StringComparison.OrdinalIgnoreCase))
                    {
                        violations.Add(
                            $"{Path.GetRelativePath(repositoryRoot, file).Replace('\\', '/')} contains {removedKey}");
                    }
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "Automation must not inject removed Lakona runtime configuration:" +
            Environment.NewLine +
            string.Join(Environment.NewLine, violations.Order(StringComparer.Ordinal)));
    }

    private static bool IsRuntimeConfigurationFile(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is
            ".ps1" or ".sh" or ".yml" or ".yaml" or ".json";

    private static string FindRepositoryRoot()
    {
        var directory = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(directory))
        {
            if (File.Exists(Path.Combine(directory, "Lakona.slnx"))
                && Directory.Exists(Path.Combine(directory, "src")))
            {
                return directory;
            }

            directory = Directory.GetParent(directory)?.FullName ?? string.Empty;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
