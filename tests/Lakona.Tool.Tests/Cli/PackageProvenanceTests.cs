using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using Lakona.Tool.Cli.Commands.Server;
using Xunit;

namespace Lakona.Tool.Tests.Cli;

public sealed class PackageProvenanceTests
{
    [Fact]
    public async Task Cli_packages_record_the_tool_release_in_all_manifests()
    {
        var root = Path.Combine(Path.GetTempPath(), nameof(PackageProvenanceTests), Guid.NewGuid().ToString("N"));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(3));
        var token = deadline.Token;
        var toolAssembly = typeof(ServerPackCommand).Assembly;
        var expectedVersion = toolAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "Server"));
            await File.WriteAllTextAsync(Path.Combine(root, "Server", "BuildTag.props"),
                "<Project><PropertyGroup><LakonaBuildTag>Provenance1</LakonaBuildTag></PropertyGroup></Project>", token);
            foreach (var name in new[] { "App", "Hotfix" })
            {
                var directory = Path.Combine(root, "Server", name);
                Directory.CreateDirectory(directory);
                await File.WriteAllTextAsync(Path.Combine(directory, $"Server.{name}.csproj"), $$"""
                    <Project Sdk="Microsoft.NET.Sdk">
                      <Import Project="../BuildTag.props" />
                      <PropertyGroup>
                        <TargetFramework>net10.0</TargetFramework>
                        <OutputType>{{(name == "App" ? "Exe" : "Library")}}</OutputType>
                      </PropertyGroup>
                      <ItemGroup><AssemblyMetadata Include="LakonaBuildTag" Value="$(LakonaBuildTag)" /></ItemGroup>
                    </Project>
                    """, token);
                await File.WriteAllTextAsync(Path.Combine(directory, "Code.cs"),
                    name == "App" ? "System.Console.WriteLine(\"package provenance\");" : "public sealed class Behavior { }", token);
            }

            await RunToolAsync(["server", "pack", "--runtime", "linux-x64", "--output", "full"]);
            await RunToolAsync(["hotfix", "pack", "--output", "hotfix"]);
            foreach (var kind in new[] { "full", "hotfix" })
            {
                using var archive = ZipFile.OpenRead(Assert.Single(Directory.GetFiles(Path.Combine(root, kind), "*.zip")));
                var manifests = archive.Entries.Where(entry =>
                    entry.FullName == "lakona-server.json" || entry.FullName.EndsWith("hotfix.json", StringComparison.Ordinal)).ToArray();
                Assert.Equal(kind == "full" ? 2 : 1, manifests.Length);
                foreach (var manifest in manifests)
                {
                    using var stream = manifest.Open();
                    using var document = await JsonDocument.ParseAsync(stream, cancellationToken: token);
                    Assert.Equal(expectedVersion, document.RootElement.GetProperty("toolVersion").GetString());
                    Assert.Equal("Provenance1", document.RootElement.GetProperty("buildTag").GetString());
                }
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }

        async Task RunToolAsync(string[] arguments)
        {
            var start = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = root,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            start.ArgumentList.Add(toolAssembly.Location);
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync(token);
            var error = process.StandardError.ReadToEndAsync(token);
            try
            {
                await process.WaitForExitAsync(token);
                Assert.True(process.ExitCode == 0, await output + await error);
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(CancellationToken.None);
                }
            }
        }
    }
}
