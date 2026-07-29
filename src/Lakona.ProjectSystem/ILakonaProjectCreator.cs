namespace Lakona.ProjectSystem;

public interface ILakonaProjectCreator
{
    Task<LakonaProjectCreationResult> CreateAsync(
        LakonaProjectCreationRequest request,
        CancellationToken cancellationToken = default);
}
