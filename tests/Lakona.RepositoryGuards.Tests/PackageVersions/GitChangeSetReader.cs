namespace Lakona.RepositoryGuards.Tests.PackageVersions;

internal static class GitChangeSetReader
{
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
        if (!string.IsNullOrWhiteSpace(status))
            return ReadExplicit(repositoryRoot, "HEAD", "WORKTREE");

        var mergeBase = GitRunner.Run(repositoryRoot, "merge-base", "HEAD", "origin/main").Trim();
        if (mergeBase.Length == 0)
            throw new InvalidOperationException("Could not resolve package version guard base. Set LAKONA_VERSION_GUARD_BASE and LAKONA_VERSION_GUARD_HEAD.");

        return ReadExplicit(repositoryRoot, mergeBase, "HEAD");
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
}
