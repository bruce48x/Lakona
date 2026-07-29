using System.Xml.Linq;
using Lakona.RepositoryGuards.Tests.PackageVersions;
using Xunit;

namespace Lakona.RepositoryGuards.Tests;

public sealed class PackageOwnershipRepositoryTests
{
    [Fact]
    public void Project_system_is_an_internal_module_without_package_identity()
    {
        var repositoryRoot = GitChangeSetReader.FindRepositoryRoot();
        var projectPath = Path.Combine(
            repositoryRoot,
            "src",
            "Lakona.ProjectSystem",
            "Lakona.ProjectSystem.csproj");
        var project = XDocument.Load(projectPath);
        var properties = project
            .Descendants()
            .Where(static element => element.Parent?.Name.LocalName == "PropertyGroup")
            .ToDictionary(
                static element => element.Name.LocalName,
                static element => element.Value.Trim(),
                StringComparer.Ordinal);

        Assert.Equal("false", properties["IsPackable"], ignoreCase: true);
        Assert.DoesNotContain("PackageId", properties.Keys);
        Assert.DoesNotContain("Version", properties.Keys);
        Assert.DoesNotContain("PackageReadmeFile", properties.Keys);
        Assert.DoesNotContain(
            project.Descendants(),
            static element => string.Equals(
                (string?)element.Attribute("Pack"),
                "true",
                StringComparison.OrdinalIgnoreCase));
    }

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

    [Fact]
    public void Rpc_core_does_not_name_friend_assemblies()
    {
        var repositoryRoot = GitChangeSetReader.FindRepositoryRoot();
        var projectPath = Path.Combine(
            repositoryRoot,
            "src",
            "Lakona.Rpc.Core",
            "Lakona.Rpc.Core.csproj");
        var project = XDocument.Load(projectPath);

        Assert.DoesNotContain(
            project.Descendants(),
            static element =>
                element.Name.LocalName == "InternalsVisibleTo" ||
                element.Name.LocalName == "AssemblyAttribute" &&
                string.Equals(
                    (string?)element.Attribute("Include"),
                    "System.Runtime.CompilerServices.InternalsVisibleTo",
                    StringComparison.Ordinal));
    }

    [Fact]
    public void Rpc_server_does_not_grant_friend_access_to_game_server()
    {
        var repositoryRoot = GitChangeSetReader.FindRepositoryRoot();
        var projectPath = Path.Combine(
            repositoryRoot,
            "src",
            "Lakona.Rpc.Server",
            "Lakona.Rpc.Server.csproj");
        var project = XDocument.Load(projectPath);

        Assert.DoesNotContain(
            project.Descendants("AssemblyAttribute"),
            static attribute =>
                string.Equals(
                    (string?)attribute.Attribute("Include"),
                    "System.Runtime.CompilerServices.InternalsVisibleTo",
                    StringComparison.Ordinal) &&
                attribute.Elements().Any(static parameter =>
                    parameter.Name.LocalName == "_Parameter1" &&
                    string.Equals(
                        parameter.Value.Trim(),
                        "Lakona.Game.Server",
                        StringComparison.Ordinal)));
    }
}
