using Lakona.Tool.Cli.Options;
using Lakona.Tool.Domain;
using Lakona.Tool.Execution;
using Lakona.Tool.Planning;

namespace Lakona.Tool.Cli.Commands;

internal sealed class NewProjectCommand(
    NewProjectPrompter prompter,
    LakonaProjectSpecFactory specFactory,
    LakonaProjectGenerator generator,
    global::ToolText text,
    global::ICliTerminal terminal)
{
    public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        try
        {
            var options = prompter.Complete(NewProjectOptionParser.Parse(args, text));
            var spec = specFactory.Create(options);
            var result = await generator.GenerateAsync(spec, cancellationToken).ConfigureAwait(false);
            terminal.WriteLine(RenderGitStatus(result.Git));
            terminal.WriteLine(text.NewProjectReadyHeader);
            terminal.WriteLine($"  1) cd \"{spec.Layout.RootPath}\"");
            terminal.WriteLine(text.BuildSolutionStep);
            terminal.WriteLine(text.CheckProjectStep);
            terminal.WriteLine(text.StartServerStep);
            terminal.WriteLine(text.OpenClientStep(Rendering.ToolEnumText.ToCliValue(spec.ClientEngine)));
            return 0;
        }
        catch (global::CliUsageException ex)
        {
            terminal.WriteErrorLine($"{text.ErrorPrefix}: {ex.Message}");
            terminal.WriteErrorLine(text.RunHelpForUsage);
            return 1;
        }
    }

    private string RenderGitStatus(GitInitializationResult git)
    {
        return git.Status switch
        {
            GitInitializationStatus.InitializedAndCommitted => text.GitStatusInitializedAndCommitted,
            GitInitializationStatus.InitializedNoCommitMissingIdentity => text.GitStatusInitializedNoCommitMissingIdentity,
            GitInitializationStatus.InitializedNoCommitNoFiles => text.GitStatusInitializedNoCommitNoFiles,
            GitInitializationStatus.SkippedParentWorktree => text.GitStatusSkippedParentWorktree,
            GitInitializationStatus.SkippedAlreadyCommitted => text.GitStatusSkippedAlreadyCommitted,
            GitInitializationStatus.SkippedGitUnavailable => text.GitStatusSkippedGitUnavailable,
            GitInitializationStatus.InitializationFailed => text.GitStatusInitFailed(git.Reason ?? "unknown"),
            GitInitializationStatus.CommitFailed => text.GitStatusCommitFailed(git.Reason ?? "unknown"),
            _ => ""
        };
    }
}
