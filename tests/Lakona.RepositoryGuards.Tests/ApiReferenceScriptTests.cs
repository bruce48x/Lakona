using System.Diagnostics;
using Lakona.RepositoryGuards.Tests.PackageVersions;
using Xunit;

namespace Lakona.RepositoryGuards.Tests;

public sealed class ApiReferenceScriptTests
{
    [Theory]
    [InlineData("root", "default")]
    [InlineData("root", "relative")]
    [InlineData("root", "absolute")]
    [InlineData("script", "default")]
    [InlineData("script", "relative")]
    [InlineData("script", "absolute")]
    [InlineData("unrelated", "default")]
    [InlineData("unrelated", "relative")]
    [InlineData("unrelated", "absolute")]
    public async Task Generates_complete_document_independent_of_working_directory(string start, string output)
    {
        using var fixture = new Fixture();
        var destination = fixture.OutputPath(output);
        var result = await fixture.RunAsync(start, output);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(destination, result.Output, StringComparison.Ordinal);
        var document = await File.ReadAllTextAsync(destination, TestContext.Current.CancellationToken);
        foreach (var package in Fixture.Packages)
        {
            Assert.Contains($"## {package}", document, StringComparison.Ordinal);
            Assert.Contains($"{package} documentation.", document, StringComparison.Ordinal);
        }
        Assert.Contains("scripts/rpc/generate-api-reference.ps1", document, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, "scripts", "blog")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.Unrelated));
    }

    [Theory]
    [InlineData("default")]
    [InlineData("relative")]
    [InlineData("absolute")]
    public async Task Missing_last_xml_preserves_existing_document_and_does_not_create_new_output(string output)
    {
        using var fixture = new Fixture();
        var missing = fixture.XmlPath(Fixture.Packages[^1]);
        File.Delete(missing);
        var destination = fixture.OutputPath(output);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        byte[] original = [0xef, 0xbb, 0xbf, 0x41, 0x0d, 0x0a, 0x42];
        await File.WriteAllBytesAsync(destination, original, TestContext.Current.CancellationToken);

        var result = await fixture.RunAsync("unrelated", output);

        Assert.NotEqual(0, result.ExitCode);
        // PowerShell's console formatter may wrap paths; inspect its full error text.
        Assert.Contains("Missing XML documentation file", result.Output, StringComparison.Ordinal);
        Assert.Contains(Fixture.Packages[^1] + ".xml", result.Output, StringComparison.Ordinal);
        Assert.Equal(original, await File.ReadAllBytesAsync(destination, TestContext.Current.CancellationToken));
        File.Delete(destination);
        var second = await fixture.RunAsync("script", output);
        Assert.NotEqual(0, second.ExitCode);
        Assert.False(File.Exists(destination));
    }

    [Fact]
    public async Task Configuration_and_framework_select_the_requested_inputs()
    {
        using var fixture = new Fixture("Debug", "fixture-tfm");
        var result = await fixture.RunAsync("unrelated", "relative", "Debug", "fixture-tfm");
        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(fixture.OutputPath("relative")), result.Output);
    }

    [Fact]
    public async Task Misplaced_script_fails_before_writing_output()
    {
        using var fixture = new Fixture();
        File.Delete(Path.Combine(fixture.Root, "CONTRIBUTING.md"));
        var result = await fixture.RunAsync("unrelated", "absolute");
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Expected repository root", result.Output, StringComparison.Ordinal);
        Assert.False(File.Exists(fixture.OutputPath("absolute")));
    }

    private sealed class Fixture : IDisposable
    {
        internal static readonly string[] Packages = ["Lakona.Rpc.Core", "Lakona.Rpc.Client", "Lakona.Rpc.Server"];
        private readonly string temporaryRoot = Path.Combine(Path.GetTempPath(), "lakona-api-reference-" + Guid.NewGuid().ToString("N"));
        private readonly string configuration;
        private readonly string framework;
        internal string Root => Path.Combine(temporaryRoot, "repository [fixture]");
        internal string Unrelated => Path.Combine(temporaryRoot, "unrelated");
        private string Script => Path.Combine(Root, "scripts", "rpc", "generate-api-reference.ps1");

        internal Fixture(string configuration = "Release", string framework = "net10.0")
        {
            this.configuration = configuration;
            this.framework = framework;
            Directory.CreateDirectory(Path.GetDirectoryName(Script)!);
            Directory.CreateDirectory(Unrelated);
            File.Copy(Path.Combine(GitChangeSetReader.FindRepositoryRoot(), "scripts", "rpc", "generate-api-reference.ps1"), Script);
            File.WriteAllText(Path.Combine(Root, "CONTRIBUTING.md"), "Fixture repository entry point.");
            foreach (var package in Packages)
            {
                var xml = XmlPath(package);
                Directory.CreateDirectory(Path.GetDirectoryName(xml)!);
                File.WriteAllText(xml, $"<doc><members><member name=\"T:{package}.Example\"><summary>{package} documentation.</summary></member></members></doc>");
            }
        }

        internal string XmlPath(string package) => Path.Combine(Root, "src", package, "bin", configuration, framework, package + ".xml");
        internal string OutputPath(string mode) => mode switch
        {
            "default" => Path.Combine(Root, "blog", "content", "reference", "api.md"),
            "relative" => Path.Combine(Root, "custom docs", "api [reference].md"),
            "absolute" => Path.Combine(temporaryRoot, "external docs", "api [reference].md"),
            _ => throw new ArgumentException(mode)
        };

        internal async Task<(int ExitCode, string Output)> RunAsync(string start, string output, string configuration = "Release", string framework = "net10.0")
        {
            var info = new ProcessStartInfo("pwsh")
            {
                WorkingDirectory = start switch { "root" => Root, "script" => Path.GetDirectoryName(Script)!, _ => Unrelated },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var argument in new[] { "-NoProfile", "-File", Script, "-Configuration", configuration, "-Framework", framework })
                info.ArgumentList.Add(argument);
            if (output != "default")
            {
                info.ArgumentList.Add("-OutputPath");
                info.ArgumentList.Add(output == "relative" ? Path.GetRelativePath(Root, OutputPath(output)) : OutputPath(output));
            }
            using var process = Process.Start(info) ?? throw new InvalidOperationException("Failed to start pwsh.");
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(30));
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            try { await process.WaitForExitAsync(deadline.Token); }
            catch
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
                throw;
            }
            return (process.ExitCode, await stdout + await stderr);
        }

        public void Dispose() => Directory.Delete(temporaryRoot, recursive: true);
    }
}
