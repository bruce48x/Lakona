using System.Diagnostics;
using System.IO.Compression;
using Xunit;

namespace Lakona.Game.Server.Tests.Packaging;

public sealed class LakonaGameServerPackageTests
{
    [Fact]
    public async Task Release_build_can_be_packed_without_rebuilding()
    {
        var repositoryRoot = FindRepositoryRoot();
        var testRoot = Path.Combine(
            Path.GetTempPath(),
            nameof(LakonaGameServerPackageTests),
            Guid.NewGuid().ToString("N"));
        var artifactsRoot = Path.Combine(testRoot, "artifacts");
        var packageRoot = Path.Combine(testRoot, "packages");

        try
        {
            var project = Path.Combine(
                repositoryRoot,
                "src",
                "Lakona.Game.Server",
                "Lakona.Game.Server.csproj");

            await RunDotNetAsync(
                repositoryRoot,
                [
                    "build",
                    project,
                    "--artifacts-path",
                    artifactsRoot,
                    "--disable-build-servers",
                    "-c",
                    "Release",
                    "-m:1",
                    "/nr:false",
                    "/p:UseSharedCompilation=false"
                ],
                TestContext.Current.CancellationToken);

            await RunDotNetAsync(
                repositoryRoot,
                [
                    "pack",
                    project,
                    "--artifacts-path",
                    artifactsRoot,
                    "--disable-build-servers",
                    "--no-build",
                    "--no-restore",
                    "-c",
                    "Release",
                    "-o",
                    packageRoot
                ],
                TestContext.Current.CancellationToken);

            var packagePath = Assert.Single(Directory.GetFiles(
                packageRoot,
                "Lakona.Game.Server.*.nupkg",
                SearchOption.TopDirectoryOnly));
            using var package = ZipFile.OpenRead(packagePath);
            var entries = package.Entries
                .Select(static entry => entry.FullName)
                .ToHashSet(StringComparer.Ordinal);

            Assert.Contains(
                "lib/net10.0/Lakona.Game.Server.Hotfix.Abstractions.dll",
                entries);
            Assert.Contains(
                "analyzers/dotnet/cs/Lakona.Game.Server.Hotfix.Generators.dll",
                entries);
        }
        finally
        {
            if (Directory.Exists(testRoot))
                Directory.Delete(testRoot, recursive: true);
        }
    }

    private static async Task RunDotNetAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo) ??
                            throw new InvalidOperationException("Could not start dotnet.");
        var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = await standardOutput;
        var error = await standardError;

        Assert.True(
            process.ExitCode == 0,
            $"dotnet {string.Join(' ', arguments)} failed with exit code {process.ExitCode}." +
            Environment.NewLine +
            output +
            Environment.NewLine +
            error);
    }

    private static string FindRepositoryRoot()
    {
        var directory = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(directory))
        {
            if (File.Exists(Path.Combine(directory, "Lakona.slnx")) &&
                Directory.Exists(Path.Combine(directory, "src")))
                return directory;

            directory = Directory.GetParent(directory)?.FullName;
        }

        throw new InvalidOperationException("Unable to locate the repository root.");
    }
}
