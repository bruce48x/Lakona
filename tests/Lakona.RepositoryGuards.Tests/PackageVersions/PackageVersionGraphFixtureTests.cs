using Xunit;

namespace Lakona.RepositoryGuards.Tests.PackageVersions;

public sealed class PackageVersionGraphFixtureTests
{
    [Fact]
    public void PackageProjectReader_LoadsPackableProjectsAndEdges()
    {
        using var fixture = FixtureRepository.Create();
        fixture.WriteProject("src/A/A.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <PackageId>A</PackageId>
                <Version>1.0.0</Version>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="..\B\B.csproj" />
              </ItemGroup>
            </Project>
            """);
        fixture.WriteProject("src/B/B.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <PackageId>B</PackageId>
                <Version>2.0.0</Version>
              </PropertyGroup>
            </Project>
            """);

        var projects = PackageProjectReader.ReadCurrent(fixture.Root);

        var a = Assert.Single(projects, project => project.PackageId == "A");
        Assert.Equal("1.0.0", a.Version);
        Assert.Contains(a.ProjectReferences, reference => reference.EndsWith("src/B/B.csproj", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(projects, project => project.PackageId == "B" && project.Version == "2.0.0");
    }

    [Fact]
    public void PackageProjectReader_NormalizesMsBuildBackslashPathsOnAllPlatforms()
    {
        using var fixture = FixtureRepository.Create();
        fixture.WriteProject("src/A/A.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <PackageId>A</PackageId>
                <Version>1.0.0</Version>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="..\B\B.csproj" />
                <Compile Include="..\Shared.cs" Link="Shared.cs" />
              </ItemGroup>
              <Target Name="GenerateVersions">
                <XmlPeek
                  XmlInputPath="$(MSBuildProjectDirectory)\..\B\B.csproj"
                  Query="/Project/PropertyGroup/Version/text()" />
              </Target>
            </Project>
            """);
        fixture.WriteProject("src/B/B.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <PackageId>B</PackageId>
                <Version>1.0.0</Version>
              </PropertyGroup>
            </Project>
            """);
        fixture.WriteFile("src/Shared.cs", "internal static class SharedMarker { }");

        var projects = PackageProjectReader.ReadCurrent(fixture.Root);

        var a = Assert.Single(projects, project => project.PackageId == "A");
        var expectedB = PackageProjectReader.NormalizePath(Path.Combine(fixture.Root, "src", "B", "B.csproj"));
        var expectedShared = PackageProjectReader.NormalizePath(Path.Combine(fixture.Root, "src", "Shared.cs"));
        Assert.Contains(expectedB, a.ProjectReferences);
        Assert.Contains(expectedB, a.VersionSourceReferences);
        Assert.Contains(expectedShared, a.PackedInputPaths);
    }

    [Fact]
    public void PackageProjectReader_IgnoresNonPackableProjects()
    {
        using var fixture = FixtureRepository.Create();
        fixture.WriteProject("src/A/A.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <PackageId>A</PackageId>
                <Version>1.0.0</Version>
                <IsPackable>false</IsPackable>
              </PropertyGroup>
            </Project>
            """);

        var projects = PackageProjectReader.ReadCurrent(fixture.Root);

        Assert.Empty(projects);
    }

    [Fact]
    public void PackageVersionGuard_RequiresDirectConsumerBumpWhenDependencyVersionChanges()
    {
        var baseProjects = new[]
        {
            Project("src/A/A.csproj", "A", "1.0.0", "src/B/B.csproj"),
            Project("src/B/B.csproj", "B", "1.0.0")
        };
        var headProjects = new[]
        {
            Project("src/A/A.csproj", "A", "1.0.0", "src/B/B.csproj"),
            Project("src/B/B.csproj", "B", "1.1.0")
        };

        var result = PackageVersionGuard.Evaluate(baseProjects, headProjects, changedPaths: ["src/B/B.csproj"]);

        Assert.Contains(result.Failures, failure => failure.PackageId == "A" && failure.Reason.Contains("A -> B", StringComparison.Ordinal));
    }

    [Fact]
    public void PackageVersionGuard_PropagatesTransitivelyThroughConsumers()
    {
        var baseProjects = new[]
        {
            Project("src/A/A.csproj", "A", "1.0.0", "src/B/B.csproj"),
            Project("src/B/B.csproj", "B", "1.0.0", "src/C/C.csproj"),
            Project("src/C/C.csproj", "C", "1.0.0")
        };
        var headProjects = new[]
        {
            Project("src/A/A.csproj", "A", "1.0.0", "src/B/B.csproj"),
            Project("src/B/B.csproj", "B", "1.1.0", "src/C/C.csproj"),
            Project("src/C/C.csproj", "C", "1.1.0")
        };

        var result = PackageVersionGuard.Evaluate(baseProjects, headProjects, changedPaths: ["src/C/C.csproj"]);

        Assert.Contains(result.Failures, failure => failure.PackageId == "A" && failure.Reason.Contains("A -> B -> C", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Failures, failure => failure.PackageId == "B");
    }

    [Fact]
    public void PackageVersionGuard_UsesVersionSourceEdges()
    {
        var baseProjects = new[]
        {
            Project("src/Tool/Tool.csproj", "Tool", "1.0.0", versionSources: ["src/Runtime/Runtime.csproj"]),
            Project("src/Runtime/Runtime.csproj", "Runtime", "1.0.0")
        };
        var headProjects = new[]
        {
            Project("src/Tool/Tool.csproj", "Tool", "1.0.0", versionSources: ["src/Runtime/Runtime.csproj"]),
            Project("src/Runtime/Runtime.csproj", "Runtime", "1.1.0")
        };

        var result = PackageVersionGuard.Evaluate(baseProjects, headProjects, changedPaths: ["src/Runtime/Runtime.csproj"]);

        Assert.Contains(result.Failures, failure => failure.PackageId == "Tool" && failure.Reason.Contains("Tool -> Runtime", StringComparison.Ordinal));
    }

    [Fact]
    public void PackageVersionGuard_TreatsDirectoryBuildPropsAsAllPackagesChanged()
    {
        var baseProjects = new[]
        {
            Project("src/A/A.csproj", "A", "1.0.0"),
            Project("src/B/B.csproj", "B", "1.0.0")
        };
        var headProjects = new[]
        {
            Project("src/A/A.csproj", "A", "1.0.0"),
            Project("src/B/B.csproj", "B", "1.1.0")
        };

        var result = PackageVersionGuard.Evaluate(baseProjects, headProjects, changedPaths: [PackageProjectReader.NormalizePath("Directory.Build.props")]);

        Assert.Contains(result.Failures, failure => failure.PackageId == "A");
        Assert.DoesNotContain(result.Failures, failure => failure.PackageId == "B");
    }

    [Fact]
    public void PackageVersionGuard_DoesNotRequireBumpWhenDependencyUnchanged()
    {
        var baseProjects = new[]
        {
            Project("src/A/A.csproj", "A", "1.0.0", "src/B/B.csproj"),
            Project("src/B/B.csproj", "B", "1.0.0")
        };
        var headProjects = new[]
        {
            Project("src/A/A.csproj", "A", "1.0.0", "src/B/B.csproj"),
            Project("src/B/B.csproj", "B", "1.0.0")
        };

        var result = PackageVersionGuard.Evaluate(
            baseProjects,
            headProjects,
            changedPaths: [PackageProjectReader.NormalizePath("src/Unrelated/Unrelated.cs")]);

        Assert.Empty(result.Failures);
    }

    [Fact]
    public void PackageVersionGuard_RequiresBumpWhenLinkedPackedInputChanges()
    {
        var sharedPath = PackageProjectReader.NormalizePath("src/Shared.cs");
        var baseProjects = new[]
        {
            ProjectWithPackedInputs("src/A/A.csproj", "A", "1.0.0", sharedPath)
        };
        var headProjects = new[]
        {
            ProjectWithPackedInputs("src/A/A.csproj", "A", "1.0.0", sharedPath)
        };

        var result = PackageVersionGuard.Evaluate(baseProjects, headProjects, changedPaths: [sharedPath]);

        Assert.Contains(result.Failures, failure => failure.PackageId == "A");
    }

    [Fact]
    public void PackageVersionGuard_RequiresToolBumpWhenAnyPackageVersionChanges()
    {
        var baseProjects = new[]
        {
            Project("src/Lakona.Tool/Lakona.Tool.csproj", "Lakona.Tool", "1.0.0"),
            Project("src/A/A.csproj", "A", "1.0.0")
        };
        var headProjects = new[]
        {
            Project("src/Lakona.Tool/Lakona.Tool.csproj", "Lakona.Tool", "1.0.0"),
            Project("src/A/A.csproj", "A", "1.1.0")
        };

        var result = PackageVersionGuard.Evaluate(baseProjects, headProjects, changedPaths: [PackageProjectReader.NormalizePath("src/A/A.cs")]);

        Assert.Contains(result.Failures, failure => failure.PackageId == "Lakona.Tool");
    }

    [Fact]
    public void PackageProjectReader_IgnoresSuppressedProjectReferences()
    {
        using var fixture = FixtureRepository.Create();
        fixture.WriteProject("src/A/A.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <PackageId>A</PackageId>
                <Version>1.0.0</Version>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="..\B\B.csproj" PrivateAssets="all" ReferenceOutputAssembly="false" />
              </ItemGroup>
            </Project>
            """);
        fixture.WriteProject("src/B/B.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <PackageId>B</PackageId>
                <Version>1.0.0</Version>
              </PropertyGroup>
            </Project>
            """);

        var projects = PackageProjectReader.ReadCurrent(fixture.Root);

        var a = Assert.Single(projects, project => project.PackageId == "A");
        Assert.Empty(a.ProjectReferences);
    }

    [Fact]
    public void GitChangeSetReader_UsesPreviousToolVersionCommitWhenHeadBumpsTool()
    {
        using var fixture = FixtureRepository.CreateGitRepository();
        fixture.WriteFile("Lakona.slnx", "");
        fixture.WriteProject("src/Lakona.Tool/Lakona.Tool.csproj", ToolProject("0.1.0"));
        fixture.WriteProject("src/A/A.csproj", PackageProject("A", "1.0.0"));
        fixture.WriteFile("src/A/A.cs", "internal sealed class A { }");
        fixture.Commit("Initial package state");

        fixture.WriteProject("src/Lakona.Tool/Lakona.Tool.csproj", ToolProject("0.2.0"));
        var previousToolVersionCommit = fixture.Commit("Bump tool to 0.2.0");

        fixture.WriteFile("src/A/A.cs", "internal sealed class A { public int Value => 1; }");
        fixture.WriteProject("src/Lakona.Tool/Lakona.Tool.csproj", ToolProject("0.3.0"));
        fixture.Commit("Bump tool to 0.3.0");

        var changeSet = fixture.ReadChangeSetWithDefaultEnvironment();

        Assert.Equal(previousToolVersionCommit, changeSet.BaseRef);
        Assert.Equal("HEAD", changeSet.HeadRef);
        Assert.Contains(changeSet.ChangedPaths, path => path.EndsWith("src/A/A.cs", StringComparison.Ordinal));
        Assert.Contains(changeSet.ChangedPaths, path => path.EndsWith("src/Lakona.Tool/Lakona.Tool.csproj", StringComparison.Ordinal));
    }

    [Fact]
    public void GitChangeSetReader_UsesToolVersionParentWhenOnlyOneToolAnchorExists()
    {
        using var fixture = FixtureRepository.CreateGitRepository();
        fixture.WriteFile("Lakona.slnx", "");
        fixture.WriteProject("src/Lakona.Tool/Lakona.Tool.csproj", ToolProject("0.1.0"));
        fixture.WriteProject("src/A/A.csproj", PackageProject("A", "1.0.0"));
        fixture.WriteFile("src/A/A.cs", "internal sealed class A { }");
        var initialCommit = fixture.Commit("Initial package state");

        fixture.WriteFile("src/A/A.cs", "internal sealed class A { public int Value => 1; }");
        fixture.WriteProject("src/Lakona.Tool/Lakona.Tool.csproj", ToolProject("0.2.0"));
        fixture.Commit("Bump first tool anchor");

        var changeSet = fixture.ReadChangeSetWithDefaultEnvironment();

        Assert.Equal(initialCommit, changeSet.BaseRef);
        Assert.Equal("HEAD", changeSet.HeadRef);
        Assert.Contains(changeSet.ChangedPaths, path => path.EndsWith("src/A/A.cs", StringComparison.Ordinal));
        Assert.Contains(changeSet.ChangedPaths, path => path.EndsWith("src/Lakona.Tool/Lakona.Tool.csproj", StringComparison.Ordinal));
    }

    [Fact]
    public void GitChangeSetReader_UsesToolVersionAnchorForDirtyWorktree()
    {
        using var fixture = FixtureRepository.CreateGitRepository();
        fixture.WriteFile("Lakona.slnx", "");
        fixture.WriteProject("src/Lakona.Tool/Lakona.Tool.csproj", ToolProject("0.1.0"));
        fixture.WriteProject("src/A/A.csproj", PackageProject("A", "1.0.0"));
        fixture.WriteFile("src/A/A.cs", "internal sealed class A { }");
        fixture.Commit("Initial package state");

        fixture.WriteProject("src/Lakona.Tool/Lakona.Tool.csproj", ToolProject("0.2.0"));
        var toolVersionCommit = fixture.Commit("Bump tool to 0.2.0");

        fixture.WriteFile("src/A/A.cs", "internal sealed class A { public int Value => 1; }");
        fixture.Commit("Change package A after tool anchor");
        fixture.WriteFile("src/A/Untracked.cs", "internal sealed class Untracked { }");

        var changeSet = fixture.ReadChangeSetWithDefaultEnvironment();

        Assert.Equal(toolVersionCommit, changeSet.BaseRef);
        Assert.Equal("WORKTREE", changeSet.HeadRef);
        Assert.Contains(changeSet.ChangedPaths, path => path.EndsWith("src/A/A.cs", StringComparison.Ordinal));
        Assert.Contains(changeSet.ChangedPaths, path => path.EndsWith("src/A/Untracked.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void GitChangeSetReader_RequiresToolBumpForPackageOnlyChangesAfterLatestToolAnchor()
    {
        using var fixture = FixtureRepository.CreateGitRepository();
        fixture.WriteFile("Lakona.slnx", "");
        fixture.WriteProject("src/Lakona.Tool/Lakona.Tool.csproj", ToolProject("0.1.0"));
        fixture.WriteProject("src/A/A.csproj", PackageProject("A", "1.0.0"));
        fixture.WriteFile("src/A/A.cs", "internal sealed class A { }");
        fixture.Commit("Initial package state");

        fixture.WriteProject("src/Lakona.Tool/Lakona.Tool.csproj", ToolProject("0.2.0"));
        var latestToolVersionCommit = fixture.Commit("Bump tool to 0.2.0");

        fixture.WriteProject("src/A/A.csproj", PackageProject("A", "1.1.0"));
        fixture.WriteFile("src/A/A.cs", "internal sealed class A { public int Value => 1; }");
        fixture.Commit("Bump package A without tool");

        var changeSet = fixture.ReadChangeSetWithDefaultEnvironment();
        var baseProjects = PackageProjectReader.ReadAtGitRef(fixture.Root, changeSet.BaseRef);
        var headProjects = PackageProjectReader.ReadAtGitRef(fixture.Root, changeSet.HeadRef);

        var result = PackageVersionGuard.Evaluate(baseProjects, headProjects, changeSet.ChangedPaths);

        Assert.Equal(latestToolVersionCommit, changeSet.BaseRef);
        Assert.Contains(result.Failures, failure => failure.PackageId == "Lakona.Tool");
    }

    private static PackageProject Project(
        string path,
        string packageId,
        string version,
        params string[] references)
    {
        return new PackageProject(
            PackageProjectReader.NormalizePath(path),
            packageId,
            version,
            references.Select(PackageProjectReader.NormalizePath).ToArray(),
            [],
            []);
    }

    private static PackageProject Project(
        string path,
        string packageId,
        string version,
        IReadOnlyList<string> versionSources)
    {
        return new PackageProject(
            PackageProjectReader.NormalizePath(path),
            packageId,
            version,
            [],
            versionSources.Select(PackageProjectReader.NormalizePath).ToArray(),
            []);
    }

    private static PackageProject ProjectWithPackedInputs(
        string path,
        string packageId,
        string version,
        params string[] packedInputs)
    {
        return new PackageProject(
            PackageProjectReader.NormalizePath(path),
            packageId,
            version,
            [],
            [],
            packedInputs.Select(PackageProjectReader.NormalizePath).ToArray());
    }

    private static string ToolProject(string version) => PackageProject("Lakona.Tool", version);

    private static string PackageProject(string packageId, string version) => $$"""
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <PackageId>{{packageId}}</PackageId>
            <Version>{{version}}</Version>
          </PropertyGroup>
        </Project>
        """;

    private sealed class FixtureRepository : IDisposable
    {
        private FixtureRepository(string root)
        {
            Root = root;
        }

        public string Root { get; }

        public static FixtureRepository Create()
        {
            var root = Path.Combine(Path.GetTempPath(), "lakona-version-guard-fixtures", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return new FixtureRepository(root);
        }

        public static FixtureRepository CreateGitRepository()
        {
            var fixture = Create();
            fixture.Git("init");
            fixture.Git("config", "user.email", "test@example.invalid");
            fixture.Git("config", "user.name", "Lakona Test");
            return fixture;
        }

        public void WriteProject(string relativePath, string content)
        {
            WriteFile(relativePath, content);
        }

        public void WriteFile(string relativePath, string content)
        {
            var path = Path.Combine(Root, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content.Replace("\r\n", "\n"));
        }

        public string Commit(string message)
        {
            Git("add", ".");
            Git("commit", "-m", message);
            return Git("rev-parse", "HEAD").Trim();
        }

        public GitChangeSet ReadChangeSetWithDefaultEnvironment()
        {
            var previousBase = Environment.GetEnvironmentVariable("LAKONA_VERSION_GUARD_BASE");
            var previousHead = Environment.GetEnvironmentVariable("LAKONA_VERSION_GUARD_HEAD");
            try
            {
                Environment.SetEnvironmentVariable("LAKONA_VERSION_GUARD_BASE", null);
                Environment.SetEnvironmentVariable("LAKONA_VERSION_GUARD_HEAD", null);
                return GitChangeSetReader.Read(Root);
            }
            finally
            {
                Environment.SetEnvironmentVariable("LAKONA_VERSION_GUARD_BASE", previousBase);
                Environment.SetEnvironmentVariable("LAKONA_VERSION_GUARD_HEAD", previousHead);
            }
        }

        private string Git(params string[] arguments) => GitRunner.Run(Root, arguments);

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                foreach (var path in Directory.GetFileSystemEntries(Root, "*", SearchOption.AllDirectories))
                    File.SetAttributes(path, FileAttributes.Normal);

                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
