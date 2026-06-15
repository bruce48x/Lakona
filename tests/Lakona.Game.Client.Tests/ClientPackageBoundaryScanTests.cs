using System.Text;
using Xunit;

namespace Lakona.Game.Client.Tests;

public sealed class ClientPackageBoundaryScanTests
{
    [Fact]
    public void ClientFacingPackages_DoNotExposeServerGameSessionKey()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourceText = ReadAllSourceFiles(Path.Combine(repositoryRoot, "src", "Lakona.Game.Abstractions"))
            + ReadAllSourceFiles(Path.Combine(repositoryRoot, "src", "Lakona.Game.Client"));

        Assert.DoesNotContain("GameSessionKey", sourceText, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "src", "Lakona.Game.Client"))
                && Directory.Exists(Path.Combine(directory.FullName, "src", "Lakona.Game.Abstractions")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }

    private static string ReadAllSourceFiles(string root)
    {
        var builder = new StringBuilder();
        foreach (var path in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                     .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                         && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                     .Order(StringComparer.Ordinal))
        {
            builder.AppendLine(File.ReadAllText(path));
        }

        return builder.ToString();
    }
}
