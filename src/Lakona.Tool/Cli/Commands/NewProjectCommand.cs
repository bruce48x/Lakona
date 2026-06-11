using Lakona.Tool.Cli.Options;
using Lakona.Tool.Domain;
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
            await generator.GenerateAsync(spec, cancellationToken).ConfigureAwait(false);
            terminal.WriteLine(text.NewProjectReadyHeader);
            terminal.WriteLine($"  1) cd \"{spec.Layout.RootPath}\"");
            terminal.WriteLine(text.CheckProjectStep);
            terminal.WriteLine(text.BuildSolutionStep);
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
}
