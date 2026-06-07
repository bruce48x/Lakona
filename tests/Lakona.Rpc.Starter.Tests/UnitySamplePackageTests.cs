using System.Reflection;
using System.Xml.Linq;
using Xunit;

namespace Lakona.Rpc.Starter.Tests;

public sealed class UnitySamplePackageTests
{
    [Fact]
    public void UnitySamples_EmbedCurrentLakonaRpcPackages()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourceVersions = ReadSourcePackageVersions(repositoryRoot);
        var unityRoots = new[]
        {
            Path.Combine(repositoryRoot, "samples", "Rpc.Unity.Json.Websocket", "Client"),
            Path.Combine(repositoryRoot, "samples", "Rpc.Unity.MemoryPack.Kcp", "Client"),
            Path.Combine(repositoryRoot, "samples", "Rpc.Unity.MemoryPack.Tcp", "Client")
        };

        foreach (var unityRoot in unityRoots)
        {
            foreach (var package in ReadLakonaRpcPackagesConfig(unityRoot))
            {
                Assert.True(sourceVersions.TryGetValue(package.Id, out var expectedVersion), $"No source project version was found for {package.Id}.");
                Assert.Equal(expectedVersion, package.Version);

                var packageRoot = Path.Combine(unityRoot, "Assets", "Packages", $"{package.Id}.{package.Version}");
                Assert.True(Directory.Exists(packageRoot), $"Expected embedded package directory at {packageRoot}.");

                var nuspecPath = Path.Combine(packageRoot, $"{package.Id}.nuspec");
                Assert.True(File.Exists(nuspecPath), $"Expected nuspec at {nuspecPath}.");
                var nuspec = ReadNuspec(nuspecPath);
                Assert.Equal(expectedVersion, nuspec.Version);

                foreach (var dependency in nuspec.LakonaRpcDependencies)
                {
                    Assert.True(sourceVersions.TryGetValue(dependency.Id, out var expectedDependencyVersion), $"No source project version was found for dependency {dependency.Id}.");
                    Assert.Equal(expectedDependencyVersion, dependency.Version);
                }

                var packageDll = Directory
                    .EnumerateFiles(packageRoot, $"{package.Id}.dll", SearchOption.AllDirectories)
                    .FirstOrDefault();
                Assert.True(packageDll is not null, $"Expected package DLL for {package.Id} under {packageRoot}.");

                var assemblyName = AssemblyName.GetAssemblyName(packageDll);
                Assert.Equal(expectedVersion, assemblyName.Version?.ToString(fieldCount: 3));
            }
        }
    }

    [Fact]
    public void UnitySamples_EmbedCorePackageWithNotificationContractApi()
    {
        var repositoryRoot = FindRepositoryRoot();
        var expectedCoreVersion = ReadProjectVersion(repositoryRoot, "src", "Lakona.Rpc.Core", "Lakona.Rpc.Core.csproj");
        var unityRoots = new[]
        {
            Path.Combine(repositoryRoot, "samples", "Rpc.Unity.Json.Websocket", "Client"),
            Path.Combine(repositoryRoot, "samples", "Rpc.Unity.MemoryPack.Kcp", "Client"),
            Path.Combine(repositoryRoot, "samples", "Rpc.Unity.MemoryPack.Tcp", "Client")
        };

        foreach (var unityRoot in unityRoots)
        {
            var packagesConfigVersion = ReadPackagesConfigVersion(unityRoot, "Lakona.Rpc.Core");
            var coreDll = Path.Combine(
                unityRoot,
                "Assets",
                "Packages",
                $"Lakona.Rpc.Core.{expectedCoreVersion}",
                "lib",
                "netstandard2.1",
                "Lakona.Rpc.Core.dll");

            Assert.Equal(expectedCoreVersion, packagesConfigVersion);
            Assert.True(File.Exists(coreDll), $"Expected Unity sample Core DLL at {coreDll}.");

            var assemblyName = AssemblyName.GetAssemblyName(coreDll);
            Assert.Equal(expectedCoreVersion, assemblyName.Version?.ToString(fieldCount: 3));

            var coreAssembly = Assembly.LoadFrom(coreDll);
            Assert.NotNull(coreAssembly.GetType("Lakona.Rpc.Core.RpcNotificationContractAttribute"));

            var rpcServiceAttribute = coreAssembly.GetType("Lakona.Rpc.Core.RpcServiceAttribute");
            Assert.NotNull(rpcServiceAttribute?.GetProperty("NotificationContract"));
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CONTRIBUTING.md")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find repository root from test output directory.");
    }

    private static string ReadPackagesConfigVersion(string unityRoot, string packageId)
    {
        var packagesConfig = XDocument.Load(Path.Combine(unityRoot, "Assets", "packages.config"));
        return packagesConfig.Root?
            .Elements("package")
            .Where(element => string.Equals((string?)element.Attribute("id"), packageId, StringComparison.Ordinal))
            .Select(element => (string?)element.Attribute("version"))
            .FirstOrDefault(version => !string.IsNullOrWhiteSpace(version))
            ?? throw new InvalidOperationException($"Package '{packageId}' not found in Assets/packages.config.");
    }

    private static List<PackageVersion> ReadLakonaRpcPackagesConfig(string unityRoot)
    {
        var packagesConfig = XDocument.Load(Path.Combine(unityRoot, "Assets", "packages.config"));
        return packagesConfig.Root?
            .Elements("package")
            .Where(element => ((string?)element.Attribute("id"))?.StartsWith("Lakona.Rpc.", StringComparison.Ordinal) is true)
            .Select(element => new PackageVersion(
                (string?)element.Attribute("id") ?? throw new InvalidOperationException("Package id missing."),
                (string?)element.Attribute("version") ?? throw new InvalidOperationException("Package version missing.")))
            .ToList()
            ?? throw new InvalidOperationException("packages.config root missing.");
    }

    private static Dictionary<string, string> ReadSourcePackageVersions(string repositoryRoot)
    {
        return Directory
            .EnumerateDirectories(Path.Combine(repositoryRoot, "src"), "Lakona.Rpc.*", SearchOption.TopDirectoryOnly)
            .Select(directory =>
            {
                var projectName = Path.GetFileName(directory);
                return new PackageVersion(projectName, ReadProjectVersion(repositoryRoot, "src", projectName, $"{projectName}.csproj"));
            })
            .ToDictionary(package => package.Id, package => package.Version, StringComparer.Ordinal);
    }

    private static NuspecPackage ReadNuspec(string nuspecPath)
    {
        var nuspec = XDocument.Load(nuspecPath);
        var namespaceName = nuspec.Root?.Name.Namespace ?? XNamespace.None;
        var metadata = nuspec.Root?.Element(namespaceName + "metadata")
            ?? throw new InvalidOperationException($"Nuspec metadata missing in {nuspecPath}.");
        var version = metadata.Element(namespaceName + "version")?.Value
            ?? throw new InvalidOperationException($"Nuspec version missing in {nuspecPath}.");
        var dependencies = metadata
            .Descendants(namespaceName + "dependency")
            .Select(element => new PackageVersion(
                (string?)element.Attribute("id") ?? throw new InvalidOperationException($"Dependency id missing in {nuspecPath}."),
                (string?)element.Attribute("version") ?? throw new InvalidOperationException($"Dependency version missing in {nuspecPath}.")))
            .Where(package => package.Id.StartsWith("Lakona.Rpc.", StringComparison.Ordinal))
            .ToList();

        return new NuspecPackage(version, dependencies);
    }

    private static string ReadProjectVersion(string repositoryRoot, params string[] pathParts)
    {
        var project = XDocument.Load(Path.Combine([repositoryRoot, .. pathParts]));
        return project.Root?
            .Elements("PropertyGroup")
            .Elements("Version")
            .Select(element => element.Value)
            .FirstOrDefault(version => !string.IsNullOrWhiteSpace(version))
            ?? throw new InvalidOperationException($"Project version not found in {Path.Combine(pathParts)}.");
    }

    private sealed record PackageVersion(string Id, string Version);

    private sealed record NuspecPackage(string Version, List<PackageVersion> LakonaRpcDependencies);
}
