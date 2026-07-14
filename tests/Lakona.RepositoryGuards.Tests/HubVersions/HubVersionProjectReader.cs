using System.Xml.Linq;
using Lakona.RepositoryGuards.Tests.PackageVersions;

namespace Lakona.RepositoryGuards.Tests.HubVersions;

internal static class HubVersionProjectReader
{
    internal const string ProjectPath = "src/Lakona.Hub/Lakona.Hub.csproj";

    public static string Read(string repositoryRoot, string gitRef)
    {
        var xml = string.Equals(gitRef, "WORKTREE", StringComparison.Ordinal)
            ? File.ReadAllText(Path.Combine(repositoryRoot, ProjectPath))
            : GitRunner.Run(repositoryRoot, "show", $"{gitRef}:{ProjectPath}");
        var document = XDocument.Parse(xml);
        return document.Root?
                   .Elements("PropertyGroup")
                   .Elements("Version")
                   .Select(element => element.Value.Trim())
                   .FirstOrDefault(value => value.Length > 0)
               ?? throw new InvalidOperationException($"{ProjectPath} does not declare a Version at {gitRef}.");
    }
}
