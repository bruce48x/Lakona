namespace Lakona.ProjectSystem;

public interface ILakonaProjectCreator
{
    Task<LakonaProjectCreationResult> CreateAsync(
        LakonaProjectCreationRequest request,
        CancellationToken cancellationToken = default);

    Task<LakonaProjectCreationResult> CreateAsync(
        LakonaProjectCreationRequest request,
        IProgress<LakonaProjectCreationProgress>? progress,
        CancellationToken cancellationToken = default) =>
        CreateAsync(request, cancellationToken);
}

public enum LakonaProjectCreationStage
{
    Preparing,
    RestoringClientDependencies,
    WritingProject,
    InitializingGit,
    Completed
}

public sealed record LakonaProjectCreationProgress(LakonaProjectCreationStage Stage);
