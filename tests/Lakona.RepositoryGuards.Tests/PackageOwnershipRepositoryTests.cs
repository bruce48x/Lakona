using System.Xml.Linq;
using Lakona.RepositoryGuards.Tests.PackageVersions;
using Xunit;

namespace Lakona.RepositoryGuards.Tests;

public sealed class PackageOwnershipRepositoryTests
{
    [Fact]
    public void Rpc_core_owns_the_bundled_rpc_analyzer_as_a_package_input()
    {
        var repositoryRoot = GitChangeSetReader.FindRepositoryRoot();
        var projectPath = Path.Combine(
            repositoryRoot,
            "src",
            "Lakona.Rpc.Core",
            "Lakona.Rpc.Core.csproj");
        var packageInputs = XDocument.Load(projectPath)
            .Descendants("PackageInputProject")
            .Select(static input => (string?)input.Attribute("Include"))
            .Where(static include => !string.IsNullOrWhiteSpace(include))
            .Select(static include =>
                Path.GetFileNameWithoutExtension(include!.Replace('\\', '/')))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("Lakona.Rpc.Analyzers", packageInputs);
    }
}
