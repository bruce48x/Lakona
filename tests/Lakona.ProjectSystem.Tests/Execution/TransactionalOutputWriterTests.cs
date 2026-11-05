using Lakona.ProjectSystem;
using Lakona.ProjectSystem.Generation.Execution;
using Lakona.ProjectSystem.Generation.Planning;
using Xunit;

namespace Lakona.ProjectSystem.Tests.Execution;

public sealed class TransactionalOutputWriterTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WriteAsync_WritesPlanIntoTargetDirectory(bool targetExists)
    {
        var parentRoot = CreateTempRoot();
        var targetRoot = Path.Combine(parentRoot, "Sample");
        try
        {
            if (targetExists)
                Directory.CreateDirectory(targetRoot);
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

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task WriteAsync_PreservesTargetClaimedDuringGeneration(bool targetExists, bool addUserFile)
    {
        var parentRoot = CreateTempRoot();
        var targetRoot = Path.Combine(parentRoot, "Sample");
        var userFile = Path.Combine(targetRoot, "notes.txt");
        try
        {
            if (targetExists)
                Directory.CreateDirectory(targetRoot);

            var files = new FilesWrittenDuringStaging(() =>
            {
                Directory.CreateDirectory(targetRoot);
                if (addUserFile)
                    File.WriteAllText(userFile, "user notes");
            });
            var plan = new GenerationPlan(targetRoot, files, [], []);
            await Assert.ThrowsAsync<LakonaProjectCreationException>(() =>
                new TransactionalOutputWriter().WriteAsync(plan, TestContext.Current.CancellationToken));

            Assert.True(Directory.Exists(targetRoot));
            Assert.False(File.Exists(Path.Combine(targetRoot, "generated.txt")));
            if (addUserFile)
                Assert.Equal("user notes", File.ReadAllText(userFile));
            Assert.Empty(Directory.GetDirectories(parentRoot, ".Sample.tmp-*"));
        }
        finally
        {
            Directory.Delete(parentRoot, recursive: true);
        }
    }

    // The writer enumerates files after validating the target and creating staging.
    private sealed class FilesWrittenDuringStaging(Action claimTarget) : IReadOnlyList<GeneratedFile>
    {
        private readonly GeneratedFile[] files = [new("generated.txt", "generated", FileWriteMode.CreateOnly, GeneratedFileKind.Text)];
        public int Count => files.Length;
        public GeneratedFile this[int index] => files[index];
        public IEnumerator<GeneratedFile> GetEnumerator()
        {
            claimTarget();
            return ((IEnumerable<GeneratedFile>)files).GetEnumerator();
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
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

            var exception = await Assert.ThrowsAsync<LakonaProjectCreationException>(() =>
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
                    "Lakona.ProjectSystem.Generation.Rendering.Client.TemplateAssets.NuGetForUnity.4.5.0.zip",
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

            Assert.Contains("LAKONA50003", exception.Message, StringComparison.Ordinal);
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
