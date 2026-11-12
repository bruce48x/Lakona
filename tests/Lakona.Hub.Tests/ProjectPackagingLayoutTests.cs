using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using Lakona.ProjectSystem;
using Xunit;

namespace Lakona.Hub.Tests;

public sealed class ProjectPackagingLayoutTests
{
    [Theory]
    [InlineData("Server")]
    [InlineData("Backend")]
    public async Task Real_packages_use_the_inspected_layout_and_saved_build_tag(string directoryName)
    {
        var root = Path.Combine(Path.GetTempPath(), nameof(ProjectPackagingLayoutTests), Guid.NewGuid().ToString("N"));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(3));
        var token = deadline.Token;
        try
        {
            var serverDirectory = Path.Combine(root, "Server");
            foreach (var directory in new[] { "Server/App", "Server/Hotfix", "Shared", "Client" })
                Directory.CreateDirectory(Path.Combine(root, directory));
            await File.WriteAllTextAsync(Path.Combine(root, "Shared", "Shared.csproj"), "<Project />", token);
            await File.WriteAllTextAsync(Path.Combine(root, "Client", "Client.csproj"), "<Project />", token);
            await File.WriteAllTextAsync(Path.Combine(serverDirectory, "Server.slnx"), "<Solution />", token);
            await File.WriteAllTextAsync(Path.Combine(serverDirectory, "BuildTag.props"),
                "<Project><PropertyGroup><LakonaBuildTag>Before</LakonaBuildTag></PropertyGroup></Project>", token);
            foreach (var name in new[] { "App", "Hotfix" })
            {
                var projectDirectory = Path.Combine(serverDirectory, name);
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, $"Server.{name}.csproj"), $$"""
                    <Project Sdk="Microsoft.NET.Sdk">
                      <Import Project="../BuildTag.props" />
                      <PropertyGroup>
                        <TargetFramework>net10.0</TargetFramework>
                        <OutputType>{{(name == "App" ? "Exe" : "Library")}}</OutputType>
                      </PropertyGroup>
                      <ItemGroup><AssemblyMetadata Include="LakonaBuildTag" Value="$(LakonaBuildTag)" /></ItemGroup>
                    </Project>
                    """, token);
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Code.cs"),
                    name == "App" ? "System.Console.WriteLine(\"layout fixture\");" : "public sealed class Behavior { }", token);
            }

            // Keep the old entry point's standard-layout behavior covered.
            LakonaProjectBuildTag.Save(root, "Legacy1");
            if (directoryName != "Server")
            {
                var renamed = Path.Combine(root, directoryName);
                Directory.Move(serverDirectory, renamed);
                serverDirectory = renamed;
            }
            Assert.Equal(LakonaProjectStatus.Ready, new LakonaProjectInspector().Inspect(root).Status);
            using var form = new ProjectPackagingForm(root, "dotnet",
                localization: new HubLocalization(HubLanguage.English), artifactFolderLauncher: new NoOpLauncher());
            Assert.Equal("Legacy1", form.BuildTag);
            form.BuildTag = "Updated2";
            Assert.True(form.CanPackage, form.BuildTagStatus);
            Assert.Equal("Updated2", new LakonaProjectInspector().Inspect(root).BuildTag);
            // Packaging is cross-targeted; the artifact is inspected, not launched.
            form.SelectedRuntime = form.RuntimeOptions.Single(option => option.Id == "linux-x64");

            foreach (var kind in new[] { "server", "hotfix" })
            {
                form.SelectedKind = form.KindOptions.Single(option => option.Id == kind);
                await form.PackageAsync(token);
                Assert.True(form.HasArtifact, form.FailureLogPath is { } log ? File.ReadAllText(log) : form.StatusText);
                Assert.Equal(Path.Combine(serverDirectory, "Build"), Path.GetDirectoryName(form.ArtifactPath));
                using var archive = ZipFile.OpenRead(form.ArtifactPath!);
                var manifests = archive.Entries.Where(entry =>
                    entry.FullName == "lakona-server.json" || entry.FullName.EndsWith("hotfix.json", StringComparison.Ordinal)).ToArray();
                Assert.Equal(kind == "server" ? 2 : 1, manifests.Length);
                foreach (var manifest in manifests)
                {
                    using var stream = manifest.Open();
                    using var document = await JsonDocument.ParseAsync(stream, cancellationToken: token);
                    Assert.Equal("Updated2", document.RootElement.GetProperty("buildTag").GetString());
                    Assert.Equal(
                        typeof(ProjectPackagingForm).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion,
                        document.RootElement.GetProperty("toolVersion").GetString());
                }
                if (directoryName != "Server") Assert.False(Directory.Exists(Path.Combine(root, "Server")));
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private sealed class NoOpLauncher : IArtifactFolderLauncher
    {
        public void OpenContainingFolder(string artifactPath) { }
    }
}
