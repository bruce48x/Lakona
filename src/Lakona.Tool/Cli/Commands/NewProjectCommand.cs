using Lakona.ProjectSystem;
using Lakona.Tool.Cli.Options;

namespace Lakona.Tool.Cli.Commands;

internal sealed class NewProjectCommand(
    NewProjectPrompter prompter,
    LakonaProjectCreator creator,
    global::ToolText text,
    global::ICliTerminal terminal)
{
    public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        try
        {
            var options = prompter.Complete(NewProjectOptionParser.Parse(args, text));
            var result = await creator.CreateAsync(options.ToCreationRequest(), cancellationToken).ConfigureAwait(false);
            terminal.WriteLine(RenderGitStatus(result));
            terminal.WriteLine(text.NewProjectReadyHeader);
            terminal.WriteLine($"  1) cd \"{result.RootPath}\"");
            terminal.WriteLine(text.BuildSolutionStep);
            terminal.WriteLine(text.StartServerStep);
            terminal.WriteLine(text.CheckProjectStep);
            terminal.WriteLine(text.OpenClientStep(
                Rendering.ToolEnumText.ToCliValue(options.ClientEngine),
                options.ClientEngineVersion is { } version
                    ? Rendering.ToolEnumText.ToCliValue(version)
                    : null));
            return 0;
        }
        catch (global::CliUsageException ex)
        {
            return RenderError(ex.Message);
        }
        catch (LakonaProjectCreationException ex)
        {
            return RenderError(ex.Message);
        }
    }

    private int RenderError(string message)
    {
        terminal.WriteErrorLine($"{text.ErrorPrefix}: {message}");
        terminal.WriteErrorLine(text.RunHelpForUsage);
        return 1;
    }

    private string RenderGitStatus(LakonaProjectCreationResult result)
    {
        return result.GitStatus switch
        {
            LakonaGitInitializationStatus.InitializedAndCommitted => text.GitStatusInitializedAndCommitted,
            LakonaGitInitializationStatus.InitializedNoCommitMissingIdentity => text.GitStatusInitializedNoCommitMissingIdentity,
            LakonaGitInitializationStatus.InitializedNoCommitNoFiles => text.GitStatusInitializedNoCommitNoFiles,
            LakonaGitInitializationStatus.SkippedParentWorktree => text.GitStatusSkippedParentWorktree,
            LakonaGitInitializationStatus.SkippedAlreadyCommitted => text.GitStatusSkippedAlreadyCommitted,
            LakonaGitInitializationStatus.SkippedGitUnavailable => text.GitStatusSkippedGitUnavailable,
            LakonaGitInitializationStatus.InitializationFailed => text.GitStatusInitFailed(result.GitReason ?? "unknown"),
            LakonaGitInitializationStatus.CommitFailed => text.GitStatusCommitFailed(result.GitReason ?? "unknown"),
            _ => ""
        };
    }

}
