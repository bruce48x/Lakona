using System.Text.RegularExpressions;
using Lakona.RepositoryGuards.Tests.PackageVersions;
using Xunit;

namespace Lakona.RepositoryGuards.Tests;

public sealed class PackageReadmeLinkRepositoryTests
{
    [Fact]
    public void Package_readme_repository_links_resolve_to_files_and_sections()
    {
        const string repositoryUrl = "https://github.com/bruce48x/Lakona/blob/main/";
        var root = GitChangeSetReader.FindRepositoryRoot();
        var failures = new List<string>();

        foreach (var directory in Directory.GetDirectories(Path.Combine(root, "src")))
        {
            var readmePath = Path.Combine(directory, "README.md");
            if (!File.Exists(readmePath))
                continue;

            foreach (Match link in Regex.Matches(File.ReadAllText(readmePath), @"\]\((?<url>[^)\s]+)\)"))
            {
                var url = link.Groups["url"].Value;
                if (!url.StartsWith(repositoryUrl, StringComparison.Ordinal))
                    continue;

                var reference = Uri.UnescapeDataString(url[repositoryUrl.Length..]).Split('#', 2);
                var target = Path.Combine(root, reference[0]);
                var source = Path.GetRelativePath(root, readmePath);
                if (!File.Exists(target))
                {
                    failures.Add($"{source}: missing link target {url}");
                    continue;
                }

                if (reference.Length == 2 && !GetHeadingIds(File.ReadAllText(target)).Contains(reference[1]))
                    failures.Add($"{source}: missing section {url}");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private static HashSet<string> GetHeadingIds(string markdown)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match heading in Regex.Matches(markdown, @"^#{1,6}\s+(.+?)\s*$", RegexOptions.Multiline))
        {
            var text = heading.Groups[1].Value.Trim().ToLowerInvariant();
            var id = Regex.Replace(text, @"[^\p{L}\p{N}_\- ]", "").Replace(' ', '-');
            var uniqueId = id;
            for (var suffix = 1; !ids.Add(uniqueId); suffix++)
                uniqueId = $"{id}-{suffix}";
        }

        return ids;
    }
}
