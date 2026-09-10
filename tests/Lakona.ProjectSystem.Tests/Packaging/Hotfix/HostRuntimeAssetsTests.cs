using System.IO.Compression;
using Lakona.ProjectSystem.Packaging.Hotfix;
using Xunit;

namespace Lakona.ProjectSystem.Tests.Packaging.Hotfix;

public sealed class HostRuntimeAssetsTests
{
    [Fact]
    public async Task Package_omits_identical_runtime_assets_but_preserves_private_changed_and_content_files()
    {
        var root = Path.Combine(Path.GetTempPath(), nameof(HostRuntimeAssetsTests), Guid.NewGuid().ToString("N"));
        var host = Directory.CreateDirectory(Path.Combine(root, "host")).FullName;
        var hotfix = Directory.CreateDirectory(Path.Combine(root, "hotfix")).FullName;
        var token = TestContext.Current.CancellationToken;
        try
        {
            await File.WriteAllTextAsync(Path.Combine(host, "Host.deps.json"), """
                { "targets": { "net10.0": { "Host/1.0": {
                  "runtime": { "Host.dll": {}, "lib/net10.0/Shared.dll": {}, "Changed.dll": {} },
                  "resources": { "lib/net10.0/fr/Shared.resources.dll": { "locale": "fr" } },
                  "runtimeTargets": { "runtimes/linux-x64/native/libshared.so": { "rid": "linux-x64", "assetType": "native" } }
                } } } }
                """, token);
            foreach (var name in new[] { "Host.dll", "Shared.dll", "Shared.pdb", "Changed.dll", "fr/Shared.resources.dll", "runtimes/linux-x64/native/libshared.so", "settings.json" })
            {
                foreach (var directory in new[] { host, hotfix })
                {
                    var path = Path.Combine(directory, name);
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    await File.WriteAllTextAsync(path, name, token);
                }
            }
            await File.WriteAllTextAsync(Path.Combine(hotfix, "Changed.dll"), "other-version", token);
            await File.WriteAllTextAsync(Path.Combine(hotfix, "Private.dll"), "private", token);
            await File.WriteAllTextAsync(Path.Combine(hotfix, "Hotfix.dll"), "hotfix", token);
            var zip = await new HotfixPackageWriter().WritePackageAsync(hotfix, Path.Combine(root, "packages"),
                "Hotfix", "net10.0", "Test", "v1", DateTimeOffset.UtcNow, token, host);
            using (var archive = ZipFile.OpenRead(zip))
            {
                var names = archive.Entries.Select(entry => entry.FullName).ToArray();
                Assert.DoesNotContain("Host.dll", names);
                Assert.DoesNotContain("Shared.dll", names);
                Assert.DoesNotContain("Shared.pdb", names);
                Assert.DoesNotContain("fr/Shared.resources.dll", names);
                Assert.DoesNotContain("runtimes/linux-x64/native/libshared.so", names);
                Assert.Contains("Changed.dll", names);
                Assert.Contains("Private.dll", names);
                Assert.Contains("settings.json", names);
                Assert.Contains("Hotfix.dll", names);
            }
            // Installation verifies that checksums describe the filtered payload exactly.
            Assert.Equal("v1", await new HotfixPackageInstaller().InstallAsync(zip, Path.Combine(root, "installed"), token));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
