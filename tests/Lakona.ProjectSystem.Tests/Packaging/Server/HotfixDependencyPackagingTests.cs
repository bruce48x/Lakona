using System.IO.Compression;
using System.Runtime.InteropServices;
using Lakona.ProjectSystem.Packaging.Server;
using Xunit;

namespace Lakona.ProjectSystem.Tests.Packaging.Server;

public sealed class HotfixDependencyPackagingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Full_package_executes_hotfix_only_dependency_without_build_outputs(bool useDllReference)
    {
        var root = Path.Combine(Path.GetTempPath(), nameof(HotfixDependencyPackagingTests), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var cancellationToken = TestContext.Current.CancellationToken;
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "BuildTag.props"),
                "<Project><PropertyGroup><LakonaBuildTag>DependencyTest</LakonaBuildTag></PropertyGroup></Project>", cancellationToken);
            foreach (var name in new[] { "App", "Hotfix", "Dependency", "Transitive" })
                Directory.CreateDirectory(Path.Combine(root, name));

            await WriteProjectAsync("Transitive", "", "public static class Transitive { public static string Value => \"dependency-ok\"; }");
            await WriteProjectAsync("Dependency", "<ItemGroup><ProjectReference Include=\"../Transitive/Transitive.csproj\" /></ItemGroup>",
                "public static class Dependency { public static string Value => Transitive.Value; }");
            var dependencyReference = "<ProjectReference Include=\"../Dependency/Dependency.csproj\" />";
            if (useDllReference)
            {
                var build = await new DotNetCommandRunner().RunAsync(root,
                    ["build", Path.Combine(root, "Dependency", "Dependency.csproj"), "-c", "Release"], cancellationToken);
                Assert.True(build.ExitCode == 0, build.StandardOutput + build.StandardError);
                dependencyReference = "<Reference Include=\"Dependency\"><HintPath>../Dependency/bin/Release/net10.0/Dependency.dll</HintPath></Reference>";
            }
            await WriteProjectAsync("Hotfix", $"""
                <ItemGroup>
                  <ProjectReference Include="../App/App.csproj" />
                  {dependencyReference}
                </ItemGroup>
                """, "public static class Entry { public static string Run() => Dependency.Value; }");
            await WriteProjectAsync("App", "<PropertyGroup><OutputType>Exe</OutputType></PropertyGroup>", """
                using System;
                using System.IO;
                using System.Reflection;
                using System.Runtime.Loader;
                var version = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "hotfix", "current.txt")).Trim();
                var path = Path.Combine(AppContext.BaseDirectory, "hotfix", "versions", version, "Hotfix.dll");
                var context = new HotfixContext(path);
                var assembly = context.LoadFromAssemblyPath(path);
                Console.WriteLine(assembly.GetType("Entry")!.GetMethod("Run")!.Invoke(null, null));
                sealed class HotfixContext(string path) : AssemblyLoadContext(isCollectible: true)
                {
                    private readonly AssemblyDependencyResolver resolver = new(path);
                    protected override Assembly? Load(AssemblyName name)
                    {
                        var resolved = resolver.ResolveAssemblyToPath(name);
                        return resolved is null ? null : LoadFromAssemblyPath(resolved);
                    }
                }
                """);

            // A stale bin artifact must not become part of the deployment.
            var staleDirectory = Path.Combine(root, "Hotfix", "bin", "Release", "net10.0");
            Directory.CreateDirectory(staleDirectory);
            await File.WriteAllTextAsync(Path.Combine(staleDirectory, "Obsolete.dll"), "stale", cancellationToken);
            var zip = await new ServerPackageWriter().PackAsync(new ServerPackOptions(
                Path.Combine(root, "App", "App.csproj"), Path.Combine(root, "Hotfix", "Hotfix.csproj"),
                Path.Combine(root, "packages"), RuntimeInformation.RuntimeIdentifier, "Release", "20260909-120000Z"), cancellationToken);
            var deployed = Path.Combine(root, "deployed");
            ZipFile.ExtractToDirectory(zip, deployed);
            Assert.False(File.Exists(Path.Combine(deployed, "Dependency.dll")));
            Assert.DoesNotContain(Directory.EnumerateFiles(deployed, "*", SearchOption.AllDirectories),
                file => Path.GetFileName(file) == "Obsolete.dll");
            // Move all source/build inputs out of their original location before launching.
            foreach (var name in new[] { "App", "Hotfix", "Dependency", "Transitive" })
                Directory.Move(Path.Combine(root, name), Path.Combine(root, name + "-unavailable"));
            var result = await new DotNetCommandRunner().RunAsync(deployed, [Path.Combine(deployed, "App.dll")], cancellationToken);
            Assert.True(result.ExitCode == 0, result.StandardOutput + result.StandardError);
            Assert.Contains("dependency-ok", result.StandardOutput);

            async Task WriteProjectAsync(string name, string extra, string source)
            {
                await File.WriteAllTextAsync(Path.Combine(root, name, name + ".csproj"),
                    "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable></PropertyGroup>" + extra + "</Project>", cancellationToken);
                await File.WriteAllTextAsync(Path.Combine(root, name, "Code.cs"), source, cancellationToken);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
