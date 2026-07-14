using Lakona.Tool.Domain;
using Lakona.Tool.Execution;
using Lakona.Tool.Infrastructure;
using Lakona.Tool.Planning;
using Lakona.Tool.Rendering.Client;
using Lakona.Tool.Rendering.Common;
using Lakona.Tool.Rendering.Docs;
using Lakona.Tool.Rendering.Operations;
using Lakona.Tool.Rendering.Server;
using Lakona.Tool.Rendering.Shared;

namespace Lakona.ProjectSystem;

public sealed class LakonaProjectCreator
{
    private readonly ProjectSpecFactory specFactory;
    private readonly LakonaProjectGenerator generator;

    public LakonaProjectCreator()
        : this(new GitCommandRunner())
    {
    }

    internal LakonaProjectCreator(IGitCommandRunner gitCommandRunner)
        : this(new ProjectSpecFactory(), CreateGenerator(gitCommandRunner))
    {
    }

    internal LakonaProjectCreator(ProjectSpecFactory specFactory, LakonaProjectGenerator generator)
    {
        this.specFactory = specFactory;
        this.generator = generator;
    }

    private static LakonaProjectGenerator CreateGenerator(IGitCommandRunner gitCommandRunner) => new(
        new LakonaProjectPlanBuilder(
            [
                new GitRenderer(),
                new SharedProjectRenderer(),
                new ServerAppRenderer(),
                new HotfixRenderer(),
                new OperationsRenderer(),
                new GeneratedProjectGuideRenderer()
            ],
            [new UnityClientRenderer(), new GodotClientRenderer(), new ConsoleClientRenderer()]),
        new GenerationExecutor(new TransactionalOutputWriter()),
        new GitInitializer(gitCommandRunner));

    public async Task<LakonaProjectCreationResult> CreateAsync(
        LakonaProjectCreationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var spec = specFactory.Create(request);
        var result = await generator.GenerateAsync(spec, cancellationToken).ConfigureAwait(false);
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
