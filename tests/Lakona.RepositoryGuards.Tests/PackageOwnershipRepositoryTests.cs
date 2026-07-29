using System.Xml.Linq;
using System.Text.RegularExpressions;
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

    [Fact]
    public void Hotfix_abstractions_does_not_grant_friend_access_to_game_server()
    {
        var repositoryRoot = GitChangeSetReader.FindRepositoryRoot();
        var assemblyInfoPath = Path.Combine(
            repositoryRoot,
            "src",
            "Lakona.Game.Server.Hotfix.Abstractions",
            "Properties",
            "AssemblyInfo.cs");
        var assemblyInfo = File.ReadAllText(assemblyInfoPath);

        Assert.DoesNotContain(
            "InternalsVisibleTo(\"Lakona.Game.Server\")",
            assemblyInfo,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Game_server_declares_each_test_friend_once_in_its_project()
    {
        var repositoryRoot = GitChangeSetReader.FindRepositoryRoot();
        var projectPath = Path.Combine(
            repositoryRoot,
            "src",
            "Lakona.Game.Server",
            "Lakona.Game.Server.csproj");
        var assemblyInfoPath = Path.Combine(
            repositoryRoot,
            "src",
            "Lakona.Game.Server",
            "Properties",
            "AssemblyInfo.cs");
        var friends = XDocument.Load(projectPath)
            .Descendants("InternalsVisibleTo")
            .Select(static element => (string?)element.Attribute("Include"))
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Select(static name => name!)
            .ToArray();

        Assert.False(File.Exists(assemblyInfoPath));
        Assert.Equal(friends.Length, friends.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            [
                "BusinessLogic.Tests",
                "Lakona.Game.Cluster.Rpc.Tests",
                "Lakona.Game.Cluster.Tests",
                "Lakona.Game.Server.Hotfix.Tests",
                "Lakona.Game.Server.Tests"
            ],
            friends.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Production_friend_grants_are_limited_to_the_known_project_system_exception()
    {
        var repositoryRoot = GitChangeSetReader.FindRepositoryRoot();
        var sourceRoot = Path.Combine(repositoryRoot, "src");
        var productionFriends = new List<string>();

        foreach (var projectPath in Directory.EnumerateFiles(
                     sourceRoot,
                     "*.csproj",
                     SearchOption.AllDirectories))
        {
            var owner = Path.GetFileNameWithoutExtension(projectPath);
            var project = XDocument.Load(projectPath);
            var targets = project
                .Descendants("InternalsVisibleTo")
                .Select(static element => (string?)element.Attribute("Include"))
                .Concat(project
                    .Descendants("AssemblyAttribute")
                    .Where(static attribute => string.Equals(
                        (string?)attribute.Attribute("Include"),
                        "System.Runtime.CompilerServices.InternalsVisibleTo",
                        StringComparison.Ordinal))
                    .SelectMany(static attribute => attribute
                        .Elements()
                        .Where(static parameter => parameter.Name.LocalName == "_Parameter1")
                        .Select(static parameter => parameter.Value.Trim())));

            productionFriends.AddRange(targets
                .Where(static target => IsProductionFriend(target))
                .Select(target => $"{owner} -> {target}"));
        }

        foreach (var assemblyInfoPath in Directory.EnumerateFiles(
                     sourceRoot,
                     "AssemblyInfo.cs",
                     SearchOption.AllDirectories))
        {
            var owner = Directory.GetParent(Path.GetDirectoryName(assemblyInfoPath)!)!.Name;
            var assemblyInfo = File.ReadAllText(assemblyInfoPath);
            productionFriends.AddRange(
                Regex.Matches(
                        assemblyInfo,
                        """InternalsVisibleTo\("([^"]+)"\)""",
                        RegexOptions.CultureInvariant)
                    .Select(static match => match.Groups[1].Value)
                    .Where(static target => IsProductionFriend(target))
                    .Select(target => $"{owner} -> {target}"));
        }

        Assert.Equal(
            ["Lakona.ProjectSystem -> Lakona.Tool"],
            productionFriends.Order(StringComparer.Ordinal));
    }

    private static bool IsProductionFriend(string? assemblyName)
    {
        return !string.IsNullOrWhiteSpace(assemblyName) &&
               !assemblyName.EndsWith(".Tests", StringComparison.Ordinal);
    }
}
