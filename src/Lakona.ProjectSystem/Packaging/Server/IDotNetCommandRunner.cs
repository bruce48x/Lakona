namespace Lakona.ProjectSystem.Packaging.Server;

internal interface IDotNetCommandRunner
{
    Task<DotNetCommandResult> RunAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken);
}
