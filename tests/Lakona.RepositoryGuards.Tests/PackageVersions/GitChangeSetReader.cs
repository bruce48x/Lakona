using System.Xml.Linq;

namespace Lakona.RepositoryGuards.Tests.PackageVersions;

internal static class GitChangeSetReader
{
    private const string ToolProjectPath = "src/Lakona.Tool/Lakona.Tool.csproj";

    public static string FindRepositoryRoot()
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

    public static GitChangeSet Read(string repositoryRoot)
    {
        var head = Environment.GetEnvironmentVariable("LAKONA_VERSION_GUARD_HEAD");
        var @base = Environment.GetEnvironmentVariable("LAKONA_VERSION_GUARD_BASE");
        if (!string.IsNullOrWhiteSpace(head) && !string.IsNullOrWhiteSpace(@base) && !IsAllZeroGitRef(@base!))
            return ReadExplicit(repositoryRoot, @base!, head!);

        var status = GitRunner.Run(repositoryRoot, "status", "--porcelain", "--untracked-files=all");
        var defaultHead = string.IsNullOrWhiteSpace(status) ? "HEAD" : "WORKTREE";
        var defaultBase = ResolveToolVersionAnchorBase(repositoryRoot, defaultHead);
        return ReadExplicit(repositoryRoot, defaultBase, defaultHead);
    }

    private static string ResolveToolVersionAnchorBase(string repositoryRoot, string head)
    {
        var commits = GitRunner.Run(repositoryRoot, "log", "--format=%H", "--", ToolProjectPath)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var anchors = new List<ToolVersionAnchor>();
        foreach (var commit in commits)
        {
            var parents = GitRunner.Run(repositoryRoot, "rev-list", "--parents", "-n", "1", commit)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parents.Length < 2)
                continue;

            var parent = parents[1];
            var currentVersion = ReadToolVersionAtRef(repositoryRoot, commit);
            var parentVersion = ReadToolVersionAtRef(repositoryRoot, parent);
            if (!string.IsNullOrWhiteSpace(currentVersion) &&
                !string.Equals(currentVersion, parentVersion, StringComparison.Ordinal))
            {
                anchors.Add(new ToolVersionAnchor(commit, parent));
            }
        }

        if (anchors.Count > 0)
        {
            var latest = anchors[0];
            if (HasPackageRelevantChangesAfter(repositoryRoot, latest.Commit, head))
                return latest.Commit;

            return anchors.Count > 1 ? anchors[1].Commit : latest.Parent;
        }

        throw new InvalidOperationException("Could not resolve package version guard base from Lakona.Tool version history. Set LAKONA_VERSION_GUARD_BASE and LAKONA_VERSION_GUARD_HEAD.");
    }

    private static bool HasPackageRelevantChangesAfter(string repositoryRoot, string @base, string head)
    {
        return ReadChangedRelativePaths(repositoryRoot, @base, head)
            .Any(IsPackageRelevantPath);
    }

    private static IReadOnlyList<string> ReadChangedRelativePaths(string repositoryRoot, string @base, string head)
    {
        var diffHead = string.Equals(head, "WORKTREE", StringComparison.Ordinal) ? string.Empty : head;
        var diffOutput = diffHead.Length == 0
            ? GitRunner.Run(repositoryRoot, "diff", "--name-only", @base)
            : GitRunner.Run(repositoryRoot, "diff", "--name-only", @base, diffHead);
        var untrackedOutput = diffHead.Length == 0
            ? GitRunner.Run(repositoryRoot, "ls-files", "--others", "--exclude-standard")
            : string.Empty;

        return diffOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Concat(untrackedOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static bool IsPackageRelevantPath(string path)
    {
        var normalized = path.Replace('\\', '/');
        var fileName = Path.GetFileName(normalized);
        return normalized.StartsWith("src/", StringComparison.Ordinal) ||
               fileName is "Directory.Build.props" or "Directory.Build.targets" or "global.json" ||
               normalized.Contains("/build/", StringComparison.OrdinalIgnoreCase) && (normalized.EndsWith(".props", StringComparison.OrdinalIgnoreCase) || normalized.EndsWith(".targets", StringComparison.OrdinalIgnoreCase));
    }

    private static string? ReadToolVersionAtRef(string repositoryRoot, string gitRef)
    {
        string xml;
        try
        {
            xml = GitRunner.Run(repositoryRoot, "show", $"{gitRef}:{ToolProjectPath}");
        }
        catch (InvalidOperationException)
        {
            return null;
        }

        var document = XDocument.Parse(xml);
        return document.Root?
            .Elements("PropertyGroup")
            .Elements("Version")
            .Select(element => element.Value.Trim())
            .FirstOrDefault(value => value.Length > 0);
    }

    private static GitChangeSet ReadExplicit(string repositoryRoot, string @base, string head)
    {
        var diffHead = string.Equals(head, "WORKTREE", StringComparison.Ordinal) ? string.Empty : head;
        var diffOutput = diffHead.Length == 0
            ? GitRunner.Run(repositoryRoot, "diff", "--name-only", @base)
            : GitRunner.Run(repositoryRoot, "diff", "--name-only", @base, diffHead);
        var untrackedOutput = diffHead.Length == 0
            ? GitRunner.Run(repositoryRoot, "ls-files", "--others", "--exclude-standard")
            : string.Empty;
        var changed = diffOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Concat(untrackedOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(path => PackageProjectReader.NormalizePath(Path.Combine(repositoryRoot, path)))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Console.WriteLine($"Package version graph base: {@base}");
        Console.WriteLine($"Package version graph head: {head}");
        return new GitChangeSet(@base, head, changed);
    }

    private static bool IsAllZeroGitRef(string value)
    {
        return value.Length >= 40 && value.All(character => character == '0');
    }

    private sealed record ToolVersionAnchor(string Commit, string Parent);
}
