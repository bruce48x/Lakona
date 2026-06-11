using Lakona.Tool.Execution;
using Lakona.Tool.Planning;
using Xunit;

namespace Lakona.Tool.Tests.Execution;

public sealed class TransactionalOutputWriterTests
{
    [Fact]
    public async Task WriteAsync_WritesPlanIntoTargetDirectory()
    {
        var parentRoot = CreateTempRoot();
        var targetRoot = Path.Combine(parentRoot, "Sample");
        try
        {
            var plan = new GenerationPlan(
                targetRoot,
                [new GeneratedFile("Shared/Contracts/Login.cs", "public sealed class Login {}", FileWriteMode.Replace, GeneratedFileKind.Text)],
                [new GeneratedDirectory("Server/App")],
                []);

            await new TransactionalOutputWriter().WriteAsync(plan, TestContext.Current.CancellationToken);

            Assert.True(Directory.Exists(Path.Combine(targetRoot, "Server", "App")));
            Assert.Equal(
                "public sealed class Login {}\n",
                await File.ReadAllTextAsync(Path.Combine(targetRoot, "Shared", "Contracts", "Login.cs"), TestContext.Current.CancellationToken));
            Assert.Empty(Directory.GetDirectories(parentRoot, ".Sample.tmp-*", SearchOption.TopDirectoryOnly));
        }
        finally
        {
            Directory.Delete(parentRoot, recursive: true);
        }
    }

    [Fact]
    public async Task WriteAsync_Fails_WhenTargetDirectoryIsNotEmpty()
    {
        var parentRoot = CreateTempRoot();
        var targetRoot = Path.Combine(parentRoot, "Sample");
        Directory.CreateDirectory(targetRoot);
        await File.WriteAllTextAsync(Path.Combine(targetRoot, "existing.txt"), "x", TestContext.Current.CancellationToken);

        try
        {
            var plan = new GenerationPlan(targetRoot, [], [], []);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new TransactionalOutputWriter().WriteAsync(plan, TestContext.Current.CancellationToken));

            Assert.Contains("Target directory already exists and is not empty", exception.Message, StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(targetRoot, "existing.txt")));
        }
        finally
        {
            Directory.Delete(parentRoot, recursive: true);
        }
    }

    [Fact]
    public async Task WriteAsync_ExtractsEmbeddedArchiveIntoTargetDirectory()
    {
        var parentRoot = CreateTempRoot();
        var targetRoot = Path.Combine(parentRoot, "Sample");
        try
        {
            var plan = new GenerationPlan(
                targetRoot,
                [],
                [],
                [],
                [new GeneratedArchive(
                    "Lakona.Tool.Rendering.Client.TemplateAssets.NuGetForUnity.4.5.0.zip",
                    "Client/Packages")]);

            await new TransactionalOutputWriter().WriteAsync(plan, TestContext.Current.CancellationToken);

            Assert.True(File.Exists(Path.Combine(
                targetRoot,
                "Client",
                "Packages",
                "com.github-glitchenzo.nugetforunity",
                "package.json")));
        }
        finally
        {
            Directory.Delete(parentRoot, recursive: true);
        }
    }

    [Fact]
    public async Task WriteAsync_RollsBackStagingDirectory_WhenWriteFails()
    {
        var parentRoot = CreateTempRoot();
        var targetRoot = Path.Combine(parentRoot, "Sample");

        try
        {
            var plan = new GenerationPlan(
                targetRoot,
                [
                    new GeneratedFile("ok.txt", "ok", FileWriteMode.Replace, GeneratedFileKind.Text),
                    new GeneratedFile("../escape.txt", "bad", FileWriteMode.Replace, GeneratedFileKind.Text)
                ],
                [],
                []);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new TransactionalOutputWriter().WriteAsync(plan, TestContext.Current.CancellationToken));

            Assert.False(Directory.Exists(targetRoot));
            Assert.False(File.Exists(Path.Combine(parentRoot, "escape.txt")));
            Assert.Empty(Directory.GetDirectories(parentRoot, ".Sample.tmp-*", SearchOption.TopDirectoryOnly));
        }
        finally
        {
            Directory.Delete(parentRoot, recursive: true);
        }
    }

    [Fact]
    public async Task GenerationExecutor_FailsBeforeWriting_WhenPlanHasValidationErrors()
    {
        var parentRoot = CreateTempRoot();
        var targetRoot = Path.Combine(parentRoot, "Sample");

        try
        {
            var plan = new GenerationPlan(
                targetRoot,
                [new GeneratedFile(string.Concat("Server", "/Server", "/Server.csproj"), "legacy", FileWriteMode.Replace, GeneratedFileKind.Project)],
                [],
                []);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new GenerationExecutor(new TransactionalOutputWriter()).ExecuteAsync(plan, TestContext.Current.CancellationToken));

            Assert.Contains("LTPLAN003", exception.Message, StringComparison.Ordinal);
            Assert.False(Directory.Exists(targetRoot));
            Assert.Empty(Directory.GetDirectories(parentRoot, ".Sample.tmp-*", SearchOption.TopDirectoryOnly));
        }
        finally
        {
            Directory.Delete(parentRoot, recursive: true);
        }
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "lakona-tool-execution-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
