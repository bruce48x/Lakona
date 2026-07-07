using System.Text.RegularExpressions;
using Xunit;

namespace Lakona.RepositoryGuards.Tests;

public sealed class UnityMetaGuidRepositoryTests
{
    [Fact]
    public void Sample_import_guard_meta_files_have_valid_unique_unity_guids()
    {
        var repositoryRoot = FindRepositoryRoot();
        var metaPaths = Directory
            .EnumerateFiles(
                Path.Combine(repositoryRoot, "samples"),
                "LakonaGameNuGetPackageImportGuard.cs.meta",
                SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var guidOwners = new Dictionary<string, string>(StringComparer.Ordinal);

        Assert.True(metaPaths.Length > 0, "Expected to find sample import guard Unity meta files.");
        foreach (var metaPath in metaPaths)
        {
            var relativePath = NormalizeRelativePath(repositoryRoot, metaPath);
            var guidLine = File.ReadLines(metaPath)
                .FirstOrDefault(line => line.StartsWith("guid: ", StringComparison.Ordinal));

            Assert.True(!string.IsNullOrWhiteSpace(guidLine), $"{relativePath} must contain a Unity guid line.");

            var guid = guidLine["guid: ".Length..].Trim();
            Assert.True(
                Regex.IsMatch(guid, "^[0-9a-f]{32}$"),
                $"{relativePath} has invalid Unity meta GUID '{guid}'. Unity GUIDs must be 32 lowercase hex characters.");

            Assert.True(
                !guidOwners.TryGetValue(guid, out var existingPath),
                $"{relativePath} reuses Unity meta GUID '{guid}' from {existingPath}.");
            guidOwners.Add(guid, relativePath);
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(directory))
        {
            if (File.Exists(Path.Combine(directory, "Lakona.slnx")) && Directory.Exists(Path.Combine(directory, "samples")))
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
