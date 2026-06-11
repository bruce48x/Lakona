using Lakona.Tool.Cli.Commands;
using Lakona.Tool.Cli.Options;
using Lakona.Tool.Domain;
using Lakona.Tool.Execution;
using Lakona.Tool.Planning;
using Lakona.Tool.Rendering.Client;
using Lakona.Tool.Rendering.Common;
using Lakona.Tool.Rendering.Docs;
using Lakona.Tool.Rendering.Operations;
using Lakona.Tool.Rendering.Project;
using Lakona.Tool.Rendering.Server;
using Lakona.Tool.Rendering.Shared;

internal sealed class CliApplication
{
    private readonly ToolText text;
    private readonly ICliTerminal terminal;

    public CliApplication(ToolText? text = null, ICliTerminal? terminal = null)
    {
        this.text = text ?? ToolText.Current;
        this.terminal = terminal ?? new ConsoleCliTerminal();
    }

    public async Task<int> RunAsync(string[] args)
    {
        try
        {
            if (args.Length == 0)
            {
                PrintHelp();
                return 0;
            }

            return args[0] switch
            {
                "help" or "--help" or "-h" => HelpResult(),
                "new" or "init" => await NewAsync(args.Skip(1).ToArray()).ConfigureAwait(false),
                _ => UnknownCommand(args[0])
            };
        }
        catch (CliUsageException ex)
        {
            terminal.WriteErrorLine($"{text.ErrorPrefix}: {ex.Message}");
            terminal.WriteErrorLine(text.RunHelpForUsage);
            return 1;
        }
    }

    private int HelpResult()
    {
        PrintHelp();
        return 0;
    }

    private int UnknownCommand(string command)
    {
        terminal.WriteErrorLine(text.UnknownCommand(command));
        terminal.WriteErrorLine("");
        PrintHelp();
        return 1;
    }

    private async Task<int> NewAsync(string[] args)
    {
        return await CreateNewProjectCommand().RunAsync(args, CancellationToken.None).ConfigureAwait(false);
    }

    private NewProjectCommand CreateNewProjectCommand()
    {
        return new NewProjectCommand(
            new NewProjectPrompter(text, terminal),
            new LakonaProjectSpecFactory(),
            new LakonaProjectGenerator(
                new LakonaProjectPlanBuilder(
                    [
                        new GitRenderer(),
                        new ProjectConfigRenderer(),
                        new SharedProjectRenderer(),
                        new ServerAppRenderer(),
                        new HotfixRenderer(),
                        new OperationsRenderer(),
                        new GeneratedProjectDocsRenderer()
                    ],
                    [new UnityClientRenderer(), new GodotClientRenderer()]),
                new GenerationExecutor(new TransactionalOutputWriter())),
            text,
            terminal);
    }

    private void PrintHelp()
    {
        Console.WriteLine(text.HelpText);
    }

}
