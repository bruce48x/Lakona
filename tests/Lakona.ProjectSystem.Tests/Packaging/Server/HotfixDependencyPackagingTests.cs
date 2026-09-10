using System.IO.Compression;
using System.Runtime.InteropServices;
using Lakona.ProjectSystem.Packaging.Server;
using Lakona.ProjectSystem.Packaging.Hotfix;
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
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        var cancellationToken = timeout.Token;
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "BuildTag.props"),
                "<Project><PropertyGroup><LakonaBuildTag>DependencyTest</LakonaBuildTag></PropertyGroup></Project>", cancellationToken);
            foreach (var name in new[] { "App", "Hotfix", "Dependency", "Transitive", "HostDependency", "LazyHostDependency" })
                Directory.CreateDirectory(Path.Combine(root, name));

            await WriteProjectAsync("HostDependency", "", "public interface IDatabase { } public sealed class Database : IDatabase { }");
            await WriteProjectAsync("LazyHostDependency", "", "public static class LazyHost { public static string Value => \"lazy-host-ok\"; }");
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
                """, "public sealed class Entry(IDatabase database) { public object Database => database; public string Run() => Dependency.Value + LazyHost.Value; }");
            var repository = new DirectoryInfo(AppContext.BaseDirectory);
            while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "Lakona.slnx")))
                repository = repository.Parent;
            Assert.NotNull(repository);
            var runtimeProject = Path.Combine(repository.FullName, "src", "Lakona.Game.Server", "Lakona.Game.Server.csproj");
            await WriteProjectAsync("App", $"""
                <PropertyGroup><OutputType>Exe</OutputType></PropertyGroup>
                <ItemGroup>
                  <ProjectReference Include="{runtimeProject}" />
                  <ProjectReference Include="../HostDependency/HostDependency.csproj" />
                  <ProjectReference Include="../LazyHostDependency/LazyHostDependency.csproj" />
                </ItemGroup>
                """, """
                using System;
                using System.IO;
                using System.Linq;
                using System.Reflection;
                using System.Runtime.Loader;
                using Microsoft.Extensions.DependencyInjection;
                using Lakona.Game.Server.Hotfix;
                var version = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "hotfix", "current.txt")).Trim();
                var path = Path.Combine(AppContext.BaseDirectory, "hotfix", "versions", version, "Hotfix.dll");
                if (AssemblyLoadContext.Default.Assemblies.Any(a => a.GetName().Name == "LazyHostDependency"))
                    throw new Exception("Lazy host dependency loaded before the test");
                // Exercise the shipped loader, not a look-alike test load context.
                var loader = typeof(HotfixManager).Assembly.GetType("Lakona.Game.Server.Hotfix.Loading.HotfixAssemblyLoadContext")!;
                var context = (AssemblyLoadContext)Activator.CreateInstance(loader, path, Array.Empty<string>())!;
                var assembly = (Assembly)loader.GetMethod("LoadMainAssemblyFromBytes")!.Invoke(context, new object[] { path })!;
                var database = new Database();
                using var services = new ServiceCollection().AddSingleton<IDatabase>(database).BuildServiceProvider();
                var type = assembly.GetType("Entry")!;
                var entry = ActivatorUtilities.CreateInstance(services, type);
                if (!ReferenceEquals(database, type.GetProperty("Database")!.GetValue(entry)))
                    throw new Exception("Host service instance identity was lost");
                Console.WriteLine(type.GetMethod("Run")!.Invoke(entry, null));
                if (!AssemblyLoadContext.Default.Assemblies.Any(a => a.GetName().Name == "LazyHostDependency"))
                    throw new Exception("Lazy host dependency was loaded privately");
                if (AssemblyLoadContext.Default.Assemblies.Any(a => a.GetName().Name == "Dependency"))
                    throw new Exception("Hotfix private dependency leaked into the host");
                context.Unload();
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
            var initialHotfixDirectory = Path.Combine(deployed, "hotfix", "versions", "20260909-120000Z");
            Assert.False(File.Exists(Path.Combine(initialHotfixDirectory, "HostDependency.dll")));
            Assert.False(File.Exists(Path.Combine(initialHotfixDirectory, "LazyHostDependency.dll")));
            Assert.True(File.Exists(Path.Combine(initialHotfixDirectory, "Hotfix.dll")));
            Assert.True(File.Exists(Path.Combine(initialHotfixDirectory, "Dependency.dll")));
            Assert.False(File.Exists(Path.Combine(deployed, "Dependency.dll")));
            Assert.DoesNotContain(Directory.EnumerateFiles(deployed, "*", SearchOption.AllDirectories),
                file => Path.GetFileName(file) == "Obsolete.dll");
            var standalone = await new LakonaProjectPackager().PackAsync(new LakonaPackageRequest(
                root, LakonaPackageKind.Hotfix, Configuration: "Release",
                OutputDirectory: Path.Combine(root, "standalone"),
                ServerProjectPath: Path.Combine("App", "App.csproj"),
                HotfixProjectPath: Path.Combine("Hotfix", "Hotfix.csproj")), cancellationToken: cancellationToken);
            var installedVersion = await new HotfixPackageInstaller().InstallAsync(
                standalone.ArtifactPath, Path.Combine(deployed, "hotfix"), cancellationToken);
            var standaloneDirectory = Path.Combine(deployed, "hotfix", "versions", installedVersion);
            Assert.False(File.Exists(Path.Combine(standaloneDirectory, "HostDependency.dll")));
            Assert.False(File.Exists(Path.Combine(standaloneDirectory, "LazyHostDependency.dll")));
            Assert.True(File.Exists(Path.Combine(standaloneDirectory, "Dependency.dll")));
            // Move all source/build inputs out of their original location before launching.
            foreach (var name in new[] { "App", "Hotfix", "Dependency", "Transitive", "HostDependency", "LazyHostDependency" })
                Directory.Move(Path.Combine(root, name), Path.Combine(root, name + "-unavailable"));
            var result = await new DotNetCommandRunner().RunAsync(deployed, [Path.Combine(deployed, "App.dll")], cancellationToken);
            Assert.True(result.ExitCode == 0, result.StandardOutput + result.StandardError);
            Assert.Contains("dependency-ok", result.StandardOutput);
            Assert.Contains("lazy-host-ok", result.StandardOutput);
            await File.WriteAllTextAsync(Path.Combine(deployed, "hotfix", "current.txt"), installedVersion, cancellationToken);
            var standaloneResult = await new DotNetCommandRunner().RunAsync(deployed, [Path.Combine(deployed, "App.dll")], cancellationToken);
            Assert.True(standaloneResult.ExitCode == 0, standaloneResult.StandardOutput + standaloneResult.StandardError);
            Assert.Contains("dependency-ok", standaloneResult.StandardOutput);
            Assert.Contains("lazy-host-ok", standaloneResult.StandardOutput);

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
