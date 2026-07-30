using Lakona.ProjectSystem;
using Xunit;

namespace Lakona.ProjectSystem.Tests;

public sealed class LakonaProjectPackagerTests
{
    [Fact]
    public async Task PackAsync_packages_a_linux_server_from_the_standard_project_shape()
    {
        var root = CreateProjectRoot();
        try
        {
            var backend = new RecordingPackageBackend();
            var progress = new List<LakonaPackageProgress>();
            var packager = new LakonaProjectPackager(
                backend,
                new FixedTimeProvider(new DateTimeOffset(2026, 7, 30, 8, 9, 10, TimeSpan.Zero)));

            var result = await packager.PackAsync(
                new LakonaPackageRequest(
                    root,
                    LakonaPackageKind.Server,
                    "linux-x64",
                    "Release"),
                new RecordingProgress<LakonaPackageProgress>(progress),
                TestContext.Current.CancellationToken);

            var request = Assert.IsType<LakonaServerPackagePlan>(backend.ServerRequest);
            Assert.Equal(Path.Combine(root, "Server", "App", "Server.App.csproj"), request.ProjectPath);
            Assert.Equal(Path.Combine(root, "Server", "Hotfix", "Server.Hotfix.csproj"), request.HotfixProjectPath);
            Assert.Equal(Path.Combine(root, "Server", "Build"), request.OutputDirectory);
            Assert.Equal("linux-x64", request.RuntimeIdentifier);
            Assert.Equal("Release", request.Configuration);
            Assert.Equal("20260730-080910Z", request.Version);
            Assert.Equal(LakonaPackageKind.Server, result.Kind);
            Assert.Equal(Path.Combine(root, "Server", "Build", "server.zip"), result.ArtifactPath);
            Assert.Equal(
                [LakonaPackageStage.Validating, LakonaPackageStage.Building, LakonaPackageStage.Completed],
                progress.Select(item => item.Stage));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PackAsync_packages_a_hotfix_without_requiring_a_runtime_identifier()
    {
        var root = CreateProjectRoot();
        try
        {
            var backend = new RecordingPackageBackend();
            var packager = new LakonaProjectPackager(
                backend,
                new FixedTimeProvider(new DateTimeOffset(2026, 7, 30, 8, 9, 10, TimeSpan.Zero)));

            var result = await packager.PackAsync(
                new LakonaPackageRequest(
                    root,
                    LakonaPackageKind.Hotfix,
                    RuntimeIdentifier: null,
                    Configuration: "Debug"),
                cancellationToken: TestContext.Current.CancellationToken);

            var request = Assert.IsType<LakonaHotfixPackagePlan>(backend.HotfixRequest);
            Assert.Equal(Path.Combine(root, "Server", "Hotfix", "Server.Hotfix.csproj"), request.ProjectPath);
            Assert.Equal(Path.Combine(root, "Server", "Build"), request.OutputDirectory);
            Assert.Equal("Debug", request.Configuration);
            Assert.Equal("20260730-080910Z", request.Version);
            Assert.Equal(LakonaPackageKind.Hotfix, result.Kind);
            Assert.Null(result.RuntimeIdentifier);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("Release-1")]
    [InlineData("Release_1")]
    [InlineData("Release.1")]
    [InlineData("版本1")]
    public async Task PackAsync_rejects_a_build_tag_that_is_not_ascii_alphanumeric(string buildTag)
    {
        var root = CreateProjectRoot();
        try
        {
            File.WriteAllText(
                Path.Combine(root, "Server", "BuildTag.props"),
                $"<Project><PropertyGroup><LakonaBuildTag>{buildTag}</LakonaBuildTag></PropertyGroup></Project>");
            var backend = new RecordingPackageBackend();

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => new LakonaProjectPackager(backend, TimeProvider.System).PackAsync(
                    new LakonaPackageRequest(root, LakonaPackageKind.Hotfix),
                    cancellationToken: TestContext.Current.CancellationToken));

            Assert.Contains("letters and digits", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Null(backend.HotfixRequest);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateProjectRoot()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "LakonaProjectPackagerTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Server", "App"));
        Directory.CreateDirectory(Path.Combine(root, "Server", "Hotfix"));
        File.WriteAllText(Path.Combine(root, "Server", "App", "Server.App.csproj"), "<Project />");
        File.WriteAllText(Path.Combine(root, "Server", "Hotfix", "Server.Hotfix.csproj"), "<Project />");
        File.WriteAllText(
            Path.Combine(root, "Server", "BuildTag.props"),
            "<Project><PropertyGroup><LakonaBuildTag>Agar1</LakonaBuildTag></PropertyGroup></Project>");
        return root;
    }

    private sealed class RecordingPackageBackend : ILakonaPackageBackend
    {
        public LakonaServerPackagePlan? ServerRequest { get; private set; }

        public LakonaHotfixPackagePlan? HotfixRequest { get; private set; }

        public Task<string> PackServerAsync(
            LakonaServerPackagePlan request,
            CancellationToken cancellationToken)
        {
            ServerRequest = request;
            return Task.FromResult(Path.Combine(request.OutputDirectory, "server.zip"));
        }

        public Task<string> PackHotfixAsync(
            LakonaHotfixPackagePlan request,
            CancellationToken cancellationToken)
        {
            HotfixRequest = request;
            return Task.FromResult(Path.Combine(request.OutputDirectory, "hotfix.zip"));
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class RecordingProgress<T>(ICollection<T> items) : IProgress<T>
    {
        public void Report(T value) => items.Add(value);
    }
}
