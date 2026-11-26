using Lakona.Hub.Applications;
using Lakona.ProjectSystem;
using Xunit;

namespace Lakona.Hub.Tests;

public sealed class FlatpakRiderTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "Lakona Hub Tests", Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("user", "--user")]
    [InlineData("system", "--system")]
    public void DetectedRiderOpensServerAndConsoleClientThroughItsFlatpakInstallation(string installation, string scopeArgument)
    {
        var files = Path.Combine(root, installation, "app", "com.jetbrains.Rider", "current", "active", "files");
        var riderPath = CreateFile(Path.Combine(files, "bin", "rider"));
        CreateFile(Path.Combine(files, "product-info.json"), """{"version":"2026.2.4"}""");
        var solution = CreateFile(Path.Combine(root, "Server", "Server.slnx"), "<Solution />");
        var client = CreateFile(Path.Combine(root, "Client", "Client.csproj"), "<Project />");

        var rider = Assert.Single(FindInstallations(flatpakAvailable: true));

        Assert.Equal(LocalApplicationKind.Rider, rider.Kind);
        Assert.Equal(riderPath, rider.ExecutablePath);
        Assert.Equal("2026.2.4", rider.Version);

        var serverPlan = ApplicationLaunchPlanner.OpenServer(root, rider);
        Assert.Equal("/usr/bin/flatpak", serverPlan.ExecutablePath);
        Assert.Equal(Path.GetDirectoryName(solution), serverPlan.WorkingDirectory);
        Assert.Equal(["run", scopeArgument, "com.jetbrains.Rider", solution], serverPlan.Arguments);

        var clientPlan = ApplicationLaunchPlanner.OpenClient(root, LakonaProjectClient.Console, rider);
        Assert.Equal("/usr/bin/flatpak", clientPlan.ExecutablePath);
        Assert.Equal(Path.GetDirectoryName(client), clientPlan.WorkingDirectory);
        Assert.Equal(["run", scopeArgument, "com.jetbrains.Rider", client], clientPlan.Arguments);
    }

    [Fact]
    public void OnlyActiveRiderInstallationsWithAFlatpakLauncherAreDetected()
    {
        var relativeAppPath = Path.Combine("app", "com.jetbrains.Rider");
        CreateFile(Path.Combine(root, "system", relativeAppPath, "current", "old-revision", "files", "bin", "rider"));
        CreateFile(Path.Combine(root, "user", relativeAppPath, "current", "active", "files", "bin", "rider"));

        Assert.Empty(FindInstallations(flatpakAvailable: false));
        Assert.Null(Assert.Single(FindInstallations(flatpakAvailable: true)).Version);
    }

    private IEnumerable<LocalApplicationInstallation> FindInstallations(bool flatpakAvailable) =>
        FlatpakRider.FindInstallations(Path.Combine(root, "user"), Path.Combine(root, "system"), flatpakAvailable);

    private static string CreateFile(string path, string contents = "")
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
