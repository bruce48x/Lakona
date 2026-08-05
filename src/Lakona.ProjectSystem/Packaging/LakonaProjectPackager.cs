using Lakona.ProjectSystem.Packaging;
using Lakona.ProjectSystem.Packaging.Hotfix;
using Lakona.ProjectSystem.Packaging.Server;

namespace Lakona.ProjectSystem;

public enum LakonaPackageKind
{
    Server,
    Hotfix
}

public enum LakonaPackageStage
{
    Validating,
    Building,
    Completed
}

public sealed record LakonaPackageRequest(
    string ProjectRoot,
    LakonaPackageKind Kind,
    string? RuntimeIdentifier = null,
    string Configuration = "Release",
    string? OutputDirectory = null,
    string? ServerProjectPath = null,
    string? HotfixProjectPath = null,
    string? DotNetExecutablePath = null);

public sealed record LakonaPackageProgress(
    LakonaPackageStage Stage,
    string Message);

public sealed record LakonaPackageResult(
    LakonaPackageKind Kind,
    string ArtifactPath,
    string? RuntimeIdentifier,
    string Configuration,
    string Version);

public interface ILakonaProjectPackager
{
    Task<LakonaPackageResult> PackAsync(
        LakonaPackageRequest request,
        IProgress<LakonaPackageProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed class LakonaHotfixPackageInstaller
{
    public Task<string> InstallAsync(
        string packagePath,
        string installationRoot,
        CancellationToken cancellationToken = default)
    {
        return new HotfixPackageInstaller()
            .InstallAsync(packagePath, installationRoot, cancellationToken);
    }
}

public sealed class LakonaProjectPackager : ILakonaProjectPackager
{
    private readonly ILakonaPackageBackend backend;
    private readonly TimeProvider timeProvider;

    public LakonaProjectPackager()
        : this(new LakonaPackageBackend(), TimeProvider.System)
    {
    }

    internal LakonaProjectPackager(
        ILakonaPackageBackend backend,
        TimeProvider timeProvider)
    {
        this.backend = backend ?? throw new ArgumentNullException(nameof(backend));
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<LakonaPackageResult> PackAsync(
        LakonaPackageRequest request,
        IProgress<LakonaPackageProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ProjectRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Configuration);

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new LakonaPackageProgress(
            LakonaPackageStage.Validating,
            "Validating project package inputs."));

        var projectRoot = Path.GetFullPath(request.ProjectRoot);
        var appProject = ResolveProjectPath(
            projectRoot,
            request.ServerProjectPath,
            Path.Combine("Server", "App", "Server.App.csproj"));
        var hotfixProject = ResolveProjectPath(
            projectRoot,
            request.HotfixProjectPath,
            Path.Combine("Server", "Hotfix", "Server.Hotfix.csproj"));
        RequireFile(appProject, "server project");
        RequireFile(hotfixProject, "hotfix project");
        _ = BuildTagReader.Read(hotfixProject);

        var version = timeProvider.GetUtcNow().ToString("yyyyMMdd-HHmmss'Z'");

        progress?.Report(new LakonaPackageProgress(
            LakonaPackageStage.Building,
            request.Kind == LakonaPackageKind.Server
                ? "Building the deployable server package."
                : "Building the hotfix package."));

        string artifactPath;
        string? runtimeIdentifier;
        switch (request.Kind)
        {
            case LakonaPackageKind.Server:
                ArgumentException.ThrowIfNullOrWhiteSpace(request.RuntimeIdentifier);
                runtimeIdentifier = request.RuntimeIdentifier;
                artifactPath = await backend.PackServerAsync(
                    new LakonaServerPackagePlan(
                        appProject,
                        hotfixProject,
                        ResolveOutputDirectory(
                            projectRoot,
                            request.OutputDirectory,
                            Path.Combine("Server", "Build")),
                        runtimeIdentifier,
                        request.Configuration,
                        version,
                        request.DotNetExecutablePath),
                    cancellationToken);
                break;

            case LakonaPackageKind.Hotfix:
                runtimeIdentifier = null;
                artifactPath = await backend.PackHotfixAsync(
                    new LakonaHotfixPackagePlan(
                        hotfixProject,
                        ResolveOutputDirectory(
                            projectRoot,
                            request.OutputDirectory,
                            Path.Combine("Server", "Build")),
                        request.Configuration,
                        version,
                        request.DotNetExecutablePath),
                    cancellationToken);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(request), request.Kind, "Unknown package kind.");
        }

        var result = new LakonaPackageResult(
            request.Kind,
            Path.GetFullPath(artifactPath),
            runtimeIdentifier,
            request.Configuration,
            version);
        progress?.Report(new LakonaPackageProgress(
            LakonaPackageStage.Completed,
            $"Package created at '{result.ArtifactPath}'."));
        return result;
    }

    private static string ResolveOutputDirectory(
        string projectRoot,
        string? requestedOutputDirectory,
        string defaultRelativePath)
    {
        if (string.IsNullOrWhiteSpace(requestedOutputDirectory))
        {
            return Path.Combine(projectRoot, defaultRelativePath);
        }

        return Path.GetFullPath(
            Path.IsPathRooted(requestedOutputDirectory)
                ? requestedOutputDirectory
                : Path.Combine(projectRoot, requestedOutputDirectory));
    }

    private static string ResolveProjectPath(
        string projectRoot,
        string? requestedProjectPath,
        string defaultRelativePath)
    {
        var path = string.IsNullOrWhiteSpace(requestedProjectPath)
            ? defaultRelativePath
            : requestedProjectPath;
        return Path.GetFullPath(
            Path.IsPathRooted(path)
                ? path
                : Path.Combine(projectRoot, path));
    }

    private static void RequireFile(string path, string description)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Lakona {description} '{path}' does not exist.",
                path);
        }
    }
}

internal sealed record LakonaServerPackagePlan(
    string ProjectPath,
    string HotfixProjectPath,
    string OutputDirectory,
    string RuntimeIdentifier,
    string Configuration,
    string Version,
    string? DotNetExecutablePath);

internal sealed record LakonaHotfixPackagePlan(
    string ProjectPath,
    string OutputDirectory,
    string Configuration,
    string Version,
    string? DotNetExecutablePath);

internal interface ILakonaPackageBackend
{
    Task<string> PackServerAsync(
        LakonaServerPackagePlan request,
        CancellationToken cancellationToken);

    Task<string> PackHotfixAsync(
        LakonaHotfixPackagePlan request,
        CancellationToken cancellationToken);
}

internal sealed class LakonaPackageBackend : ILakonaPackageBackend
{
    public Task<string> PackServerAsync(
        LakonaServerPackagePlan request,
        CancellationToken cancellationToken)
    {
        var server = new ServerPackageWriter(
            new DotNetCommandRunner(request.DotNetExecutablePath));
        return server.PackAsync(
            new ServerPackOptions(
                request.ProjectPath,
                request.HotfixProjectPath,
                request.OutputDirectory,
                request.RuntimeIdentifier,
                request.Configuration,
                request.Version),
            cancellationToken);
    }

    public Task<string> PackHotfixAsync(
        LakonaHotfixPackagePlan request,
        CancellationToken cancellationToken)
    {
        var hotfix = new HotfixPackageWriter(
            new DotNetCommandRunner(request.DotNetExecutablePath));
        return hotfix.PackAsync(
            request.ProjectPath,
            request.OutputDirectory,
            request.Configuration,
            request.Version,
            cancellationToken);
    }
}
