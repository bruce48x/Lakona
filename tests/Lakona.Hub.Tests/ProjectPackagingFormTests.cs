using System.Diagnostics;
using Lakona.ProjectSystem;
using Xunit;

namespace Lakona.Hub.Tests;

public sealed class ProjectPackagingFormTests
{
    [Fact]
    public async Task PackageAsync_keeps_success_when_the_shell_launch_returns_no_process_handle()
    {
        var projectRoot = CreateProjectRoot(nameof(PackageAsync_keeps_success_when_the_shell_launch_returns_no_process_handle));
        var artifactFolder = Path.Combine(projectRoot, "artifacts", "server");
        Directory.CreateDirectory(artifactFolder);
        ProcessStartInfo? launch = null;
        var launcher = new SystemArtifactFolderLauncher(startInfo =>
        {
            launch = startInfo;
            return null;
        });
        var form = new ProjectPackagingForm(
            projectRoot,
            CreateDotNetExecutablePath(projectRoot),
            new RecordingPackager(),
            new HubLocalization(HubLanguage.English),
            artifactFolderLauncher: launcher);

        await form.PackageAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(launch);
        Assert.Equal(artifactFolder, launch.FileName);
        Assert.True(launch.UseShellExecute);
        Assert.True(form.HasArtifact);
        Assert.Equal("Package created successfully.", form.StatusText);
    }

    [Fact]
    public async Task PackageAsync_builds_the_selected_server_package_and_exposes_its_artifact()
    {
        var projectRoot = CreateProjectRoot(nameof(PackageAsync_builds_the_selected_server_package_and_exposes_its_artifact));
        var dotNetExecutablePath = CreateDotNetExecutablePath(projectRoot);
        var packager = new RecordingPackager();
        var folderLauncher = new RecordingArtifactFolderLauncher();
        var form = new ProjectPackagingForm(
            projectRoot,
            dotNetExecutablePath,
            packager,
            new HubLocalization(HubLanguage.English),
            "Release1",
            folderLauncher);

        form.SelectedRuntime = form.RuntimeOptions.Single(option => option.Id == "linux-arm64");
        form.SelectedConfiguration = form.ConfigurationOptions.Single(option => option.Id == "Debug");

        await form.PackageAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(packager.Request);
        Assert.Equal(LakonaPackageKind.Server, packager.Request.Kind);
        Assert.Equal("linux-arm64", packager.Request.RuntimeIdentifier);
        Assert.Equal("Debug", packager.Request.Configuration);
        Assert.Equal("Release1", form.BuildTag);
        Assert.Equal(dotNetExecutablePath, packager.Request.DotNetExecutablePath);
        Assert.False(form.IsPackaging);
        Assert.True(form.HasArtifact);
        Assert.Equal(
            Path.Combine(projectRoot, "artifacts", "server", "server.zip"),
            form.ArtifactPath);
        Assert.Equal(form.ArtifactPath, folderLauncher.OpenedArtifactPath);
        Assert.Equal("Package created successfully.", form.StatusText);
    }

    [Fact]
    public async Task PackageAsync_opens_the_artifact_folder_after_success()
    {
        var projectRoot = CreateProjectRoot(nameof(PackageAsync_opens_the_artifact_folder_after_success));
        var folderLauncher = new RecordingArtifactFolderLauncher();
        var form = new ProjectPackagingForm(
            projectRoot,
            CreateDotNetExecutablePath(projectRoot),
            new RecordingPackager(),
            new HubLocalization(HubLanguage.English),
            artifactFolderLauncher: folderLauncher);

        await form.PackageAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            Path.Combine(projectRoot, "artifacts", "server", "server.zip"),
            folderLauncher.OpenedArtifactPath);
    }

    [Fact]
    public void Selecting_hotfix_hides_and_omits_the_runtime()
    {
        var projectRoot = CreateProjectRoot(nameof(Selecting_hotfix_hides_and_omits_the_runtime));
        var form = new ProjectPackagingForm(
            projectRoot,
            CreateDotNetExecutablePath(projectRoot),
            new RecordingPackager(),
            new HubLocalization(HubLanguage.English));

        form.SelectedKind = form.KindOptions.Single(option => option.Id == "hotfix");

        var request = form.CreateRequest();

        Assert.False(form.ShowsRuntime);
        Assert.Equal(LakonaPackageKind.Hotfix, request.Kind);
        Assert.Null(request.RuntimeIdentifier);
    }

    [Fact]
    public async Task PackageAsync_reports_failure_and_allows_retry()
    {
        var projectRoot = CreateProjectRoot(nameof(PackageAsync_reports_failure_and_allows_retry));
        var packager = new RecordingPackager
        {
            Error = new InvalidOperationException("publish failed")
        };
        var form = new ProjectPackagingForm(
            projectRoot,
            CreateDotNetExecutablePath(projectRoot),
            packager,
            new HubLocalization(HubLanguage.English));

        await form.PackageAsync(TestContext.Current.CancellationToken);

        Assert.False(form.IsPackaging);
        Assert.False(form.HasArtifact);
        Assert.True(form.CanPackage);
        Assert.Equal("Packaging failed: publish failed", form.StatusText);
    }

    private static string CreateProjectRoot(string testName) =>
        Path.Combine(Path.GetTempPath(), nameof(ProjectPackagingFormTests), testName);

    private static string CreateDotNetExecutablePath(string projectRoot) =>
        Path.Combine(projectRoot, "sdk", OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");

    private sealed class RecordingPackager : ILakonaProjectPackager
    {
        public LakonaPackageRequest? Request { get; private set; }

        public Exception? Error { get; init; }

        public Task<LakonaPackageResult> PackAsync(
            LakonaPackageRequest request,
            IProgress<LakonaPackageProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            Request = request;
            if (Error is not null)
            {
                return Task.FromException<LakonaPackageResult>(Error);
            }

            progress?.Report(new LakonaPackageProgress(
                LakonaPackageStage.Building,
                "Building package."));
            var directory = request.Kind == LakonaPackageKind.Server ? "server" : "hotfix";
            return Task.FromResult(new LakonaPackageResult(
                request.Kind,
                Path.Combine(request.ProjectRoot, "artifacts", directory, $"{directory}.zip"),
                request.RuntimeIdentifier,
                request.Configuration,
                "20260730-120000Z"));
        }
    }

    private sealed class RecordingArtifactFolderLauncher : IArtifactFolderLauncher
    {
        public string? OpenedArtifactPath { get; private set; }

        public void OpenContainingFolder(string artifactPath)
        {
            OpenedArtifactPath = artifactPath;
        }
    }
}
