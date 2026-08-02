using System.Xml.Linq;

namespace Lakona.RepositoryGuards.Tests.PackageVersions;

internal static class GitChangeSetReader
{
    private const string ToolProjectPath = "src/Lakona.Tool/Lakona.Tool.csproj";

    private static readonly VersionGuardScope PackageScope = new(
        ToolProjectPath,
        "LAKONA_VERSION_GUARD_BASE",
        "LAKONA_VERSION_GUARD_HEAD",
        "Package version graph",
        IsPackageRelevantPath);

    public static string FindRepositoryRoot()
    {
        foreach (var startDirectory in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            var directory = startDirectory;
            while (!string.IsNullOrEmpty(directory))
            {
                if (File.Exists(Path.Combine(directory, "Lakona.slnx")) && Directory.Exists(Path.Combine(directory, "src")))
                    return directory;

                directory = Directory.GetParent(directory)?.FullName ?? string.Empty;
            }
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }

    public static GitChangeSet Read(string repositoryRoot)
    {
        return Read(repositoryRoot, PackageScope);
    }

    internal static GitChangeSet Read(string repositoryRoot, VersionGuardScope scope)
    {
        var head = Environment.GetEnvironmentVariable(scope.HeadEnvironmentVariable);
        var @base = Environment.GetEnvironmentVariable(scope.BaseEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(head) && !string.IsNullOrWhiteSpace(@base) && !IsAllZeroGitRef(@base!))
            return ReadExplicit(repositoryRoot, @base!, head!, scope.DisplayName);

        var status = GitRunner.Run(repositoryRoot, "status", "--porcelain", "--untracked-files=all");
        var defaultHead = string.IsNullOrWhiteSpace(status) ? "HEAD" : "WORKTREE";
        var defaultBase = ResolveVersionAnchorBase(repositoryRoot, defaultHead, scope);
        return ReadExplicit(repositoryRoot, defaultBase, defaultHead, scope.DisplayName);
    }

    private static string ResolveVersionAnchorBase(string repositoryRoot, string head, VersionGuardScope scope)
    {
        var commits = GitRunner.Run(repositoryRoot, "log", "--format=%H", "--", scope.ProjectPath)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var anchors = new List<VersionAnchor>();
        foreach (var commit in commits)
        {
            var parents = GitRunner.Run(repositoryRoot, "rev-list", "--parents", "-n", "1", commit)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parents.Length < 2)
                continue;

            var parent = parents[1];
            var currentVersion = ReadVersionAtRef(repositoryRoot, commit, scope.ProjectPath);
            var parentVersion = ReadVersionAtRef(repositoryRoot, parent, scope.ProjectPath);
            if (!string.IsNullOrWhiteSpace(currentVersion) &&
                !string.Equals(currentVersion, parentVersion, StringComparison.Ordinal))
            {
                anchors.Add(new VersionAnchor(commit, parent));
            }
        }

        if (anchors.Count > 0)
        {
            var latest = anchors[0];
            if (HasRelevantChangesAfter(repositoryRoot, latest.Commit, head, scope.IsRelevantPath))
                return latest.Commit;

            return anchors.Count > 1 ? anchors[1].Commit : latest.Parent;
        }

        throw new InvalidOperationException(
            $"Could not resolve {scope.DisplayName} base from {scope.ProjectPath} version history. " +
            $"Set {scope.BaseEnvironmentVariable} and {scope.HeadEnvironmentVariable}.");
    }

    private static bool HasRelevantChangesAfter(
        string repositoryRoot,
        string @base,
        string head,
        Func<string, bool> isRelevantPath)
    {
        return ReadChangedRelativePaths(repositoryRoot, @base, head)
            .Any(isRelevantPath);
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

    private static string? ReadVersionAtRef(string repositoryRoot, string gitRef, string projectPath)
    {
        string xml;
        try
        {
            xml = GitRunner.Run(repositoryRoot, "show", $"{gitRef}:{projectPath}");
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

    private static GitChangeSet ReadExplicit(string repositoryRoot, string @base, string head, string displayName)
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

        Console.WriteLine($"{displayName} base: {@base}");
        Console.WriteLine($"{displayName} head: {head}");
        return new GitChangeSet(@base, head, changed);
    }

    private static bool IsAllZeroGitRef(string value)
    {
        return value.Length >= 40 && value.All(character => character == '0');
    }

    private sealed record VersionAnchor(string Commit, string Parent);
}

internal sealed record VersionGuardScope(
    string ProjectPath,
    string BaseEnvironmentVariable,
    string HeadEnvironmentVariable,
    string DisplayName,
    Func<string, bool> IsRelevantPath);
