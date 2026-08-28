using Lakona.ProjectSystem.Generation.Domain;
using Lakona.ProjectSystem.Generation.Execution;
using Lakona.ProjectSystem.Generation.Infrastructure;
using Lakona.ProjectSystem.Generation.Planning;
using Lakona.ProjectSystem.Generation.Rendering.Client;
using Lakona.ProjectSystem.Generation.Rendering.Common;
using Lakona.ProjectSystem.Generation.Rendering.Docs;
using Lakona.ProjectSystem.Generation.Rendering.Operations;
using Lakona.ProjectSystem.Generation.Rendering.Server;
using Lakona.ProjectSystem.Generation.Rendering.Shared;

namespace Lakona.ProjectSystem;

public sealed class LakonaProjectCreator : ILakonaProjectCreator
{
    private readonly ProjectSpecFactory specFactory;
    private readonly LakonaProjectGenerator generator;

    public LakonaProjectCreator()
        : this(new GitCommandRunner())
    {
    }

    internal LakonaProjectCreator(IGitCommandRunner gitCommandRunner)
        : this(gitCommandRunner, new UnityDependencyRestorer())
    {
    }

    internal LakonaProjectCreator(IGitCommandRunner gitCommandRunner, IUnityDependencyRestorer unityDependencyRestorer)
        : this(new ProjectSpecFactory(), CreateGenerator(gitCommandRunner, unityDependencyRestorer))
    {
    }

    internal LakonaProjectCreator(ProjectSpecFactory specFactory, LakonaProjectGenerator generator)
    {
        this.specFactory = specFactory;
        this.generator = generator;
    }

    private static LakonaProjectGenerator CreateGenerator(
        IGitCommandRunner gitCommandRunner,
        IUnityDependencyRestorer unityDependencyRestorer) => new(
        new LakonaProjectPlanBuilder(
            [
                new GitRenderer(),
                new SharedProjectRenderer(),
                new ServerAppRenderer(),
                new HotfixRenderer(),
                new OperationsRenderer(),
                new AgentSkillsRenderer(),
                new GeneratedProjectGuideRenderer()
            ],
            [new UnityClientRenderer(), new GodotClientRenderer(), new ConsoleClientRenderer()]),
        new GenerationExecutor(new TransactionalOutputWriter()),
        new GitInitializer(gitCommandRunner),
        unityDependencyRestorer);

    public async Task<LakonaProjectCreationResult> CreateAsync(
        LakonaProjectCreationRequest request,
        CancellationToken cancellationToken = default)
        => await CreateAsync(request, progress: null, cancellationToken).ConfigureAwait(false);

    public async Task<LakonaProjectCreationResult> CreateAsync(
        LakonaProjectCreationRequest request,
        IProgress<LakonaProjectCreationProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        progress?.Report(new LakonaProjectCreationProgress(LakonaProjectCreationStage.Preparing));
        var spec = specFactory.Create(request);
        var result = await generator.GenerateAsync(spec, progress, cancellationToken).ConfigureAwait(false);
        return new LakonaProjectCreationResult(
            result.RootPath,
            MapGitStatus(result.Git.Status),
            result.Git.Reason);
    }

    private static LakonaGitInitializationStatus MapGitStatus(GitInitializationStatus status) => status switch
    {
        GitInitializationStatus.InitializedAndCommitted => LakonaGitInitializationStatus.InitializedAndCommitted,
        GitInitializationStatus.InitializedNoCommitMissingIdentity => LakonaGitInitializationStatus.InitializedNoCommitMissingIdentity,
        GitInitializationStatus.InitializedNoCommitNoFiles => LakonaGitInitializationStatus.InitializedNoCommitNoFiles,
        GitInitializationStatus.SkippedParentWorktree => LakonaGitInitializationStatus.SkippedParentWorktree,
        GitInitializationStatus.SkippedAlreadyCommitted => LakonaGitInitializationStatus.SkippedAlreadyCommitted,
        GitInitializationStatus.SkippedGitUnavailable => LakonaGitInitializationStatus.SkippedGitUnavailable,
        GitInitializationStatus.InitializationFailed => LakonaGitInitializationStatus.InitializationFailed,
        GitInitializationStatus.CommitFailed => LakonaGitInitializationStatus.CommitFailed,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
    };
}
