using System.Text;
using Lakona.Tool.Cli.Options;
using Lakona.Tool.Domain;
using Lakona.Tool.Execution;
using Lakona.Tool.Planning;
using Lakona.Tool.Rendering.Client;
using Lakona.Tool.Rendering.Common;
using Lakona.Tool.Rendering.Docs;
using Lakona.Tool.Rendering.Operations;
using Lakona.Tool.Rendering.Project;
using Lakona.Tool.Rendering.Server;
using Lakona.Tool.Rendering.Shared;
using Xunit;

namespace Lakona.Tool.Tests.Integration;

public sealed class ToolArchitectureScanTests
{
    [Fact]
    public void ToolSource_DoesNotContainStarterPipelineArtifacts()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourceText = ReadAllTextFiles(Path.Combine(repositoryRoot, "src", "Lakona.Tool"))
            + ReadAllTextFiles(Path.Combine(repositoryRoot, "tests", "Lakona.Tool.Tests"));

        Assert.DoesNotContain(string.Concat("Rpc", "Starter"), sourceText, StringComparison.Ordinal);
        Assert.DoesNotContain(string.Concat("Starter", "Template"), sourceText, StringComparison.Ordinal);
        Assert.DoesNotContain(string.Concat("Starter", "Paths"), sourceText, StringComparison.Ordinal);
        Assert.DoesNotContain(string.Concat("AugmentProjectWithLakona", "Game"), sourceText, StringComparison.Ordinal);
        Assert.DoesNotContain(string.Concat("ULink", "RPC"), sourceText, StringComparison.Ordinal);
        Assert.DoesNotContain(string.Concat("ULink", "Game"), sourceText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NewProject_DoesNotGenerateLegacyStarterLayout()
    {
        var parentRoot = Path.Combine(Path.GetTempPath(), "lakona-tool-architecture-scan-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(parentRoot);
        try
        {
            var spec = new LakonaProjectSpecFactory().Create(new NewProjectOptions(
                "MyGame",
                parentRoot,
                ClientEngine.Unity,
                TransportKind.Kcp,
                SerializerKind.MemoryPack,
                PersistenceKind.None,
                NuGetForUnitySource.OpenUpm,
                DeploymentProfile.None));
            var generator = CreateGenerator();

            await generator.GenerateAsync(spec, TestContext.Current.CancellationToken);

            Assert.False(Directory.Exists(Path.Combine(spec.Layout.RootPath, "Server", "Server")));
            Assert.True(File.Exists(Path.Combine(spec.Layout.RootPath, "Server", "App", "Server.App.csproj")));
            Assert.False(Directory.Exists(Path.Combine(spec.Layout.RootPath, "Client", "Assets", "Scripts", "Rpc", "Generated")));

            var generatedText = ReadAllTextFiles(spec.Layout.RootPath);
            Assert.DoesNotContain(string.Concat("ULink", "RPC"), generatedText, StringComparison.Ordinal);
            Assert.DoesNotContain(string.Concat("ULink", "Game"), generatedText, StringComparison.Ordinal);
            Assert.DoesNotContain(string.Concat("Rpc", "Starter"), generatedText, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(parentRoot, recursive: true);
        }
    }

    private static LakonaProjectGenerator CreateGenerator()
    {
        return new LakonaProjectGenerator(
            new LakonaProjectPlanBuilder(
                [
                    new GitRenderer(),
                    new ProjectConfigRenderer(),
                    new SharedProjectRenderer(),
                    new ServerAppRenderer(),
                    new HotfixRenderer(),
                    new OperationsRenderer(),
                    new GeneratedProjectDocsRenderer()
                ],
                [new UnityClientRenderer(), new GodotClientRenderer()]),
            new GenerationExecutor(new TransactionalOutputWriter()));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "src", "Lakona.Tool"))
                && Directory.Exists(Path.Combine(directory.FullName, "tests", "Lakona.Tool.Tests")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }

    private static string ReadAllTextFiles(string root)
    {
        var builder = new StringBuilder();
        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                     .Where(IsTextSourceFile)
                     .Order(StringComparer.Ordinal))
        {
            builder.AppendLine(File.ReadAllText(path));
        }

        return builder.ToString();
    }

    private static bool IsTextSourceFile(string path)
    {
        var extension = Path.GetExtension(path);
        return extension is ".cs" or ".csproj" or ".json" or ".md" or ".slnx" or ".props" or ".asmdef" or ".config" or ".tscn" or ".tres" or ".xml" or ".txt";
    }
}
