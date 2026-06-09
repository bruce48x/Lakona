using System.Text.Json;
using Lakona.Tool.RpcStarter;

internal sealed class CliApplication(
    RpcStarterGenerator rpcStarterGenerator,
    ProjectScaffolder projectScaffolder,
    ToolConfigStore configStore,
    ToolText? text = null,
    ICliTerminal? terminal = null)
{
    private readonly ToolText text = text ?? ToolText.Current;
    private readonly ICliTerminal terminal = terminal ?? new ConsoleCliTerminal();

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
        var options = new NewCommandPrompter(text, terminal)
            .Complete(CliParser.ParseNewOptions(args, text));
        var outputDirectory = Path.GetFullPath(options.OutputPath ?? Directory.GetCurrentDirectory());
        Directory.CreateDirectory(outputDirectory);

        var projectName = string.IsNullOrWhiteSpace(options.Name) ? ProjectConventions.DefaultProjectName : options.Name;
        var projectRoot = Path.Combine(outputDirectory, projectName);

        rpcStarterGenerator.Generate(ToRpcStarterOptions(projectName, outputDirectory, options));

        if (!Directory.Exists(projectRoot))
        {
            terminal.WriteErrorLine(text.GeneratedProjectRootNotFound(projectRoot));
            return 1;
        }

        await projectScaffolder.AugmentProjectWithLakonaGameAsync(projectRoot, options).ConfigureAwait(false);

        var configPath = Path.Combine(projectRoot, ProjectConventions.ConfigFileName);
        if (File.Exists(configPath))
        {
            terminal.WriteErrorLine(text.ConfigAlreadyExists(configPath));
            return 1;
        }

        await configStore.SaveAsync(configPath, ToolConfig.CreateDefault(projectName, options)).ConfigureAwait(false);
        Console.WriteLine(text.CreatedToolConfig(configPath));
        PrintNewProjectNextSteps(projectRoot, options);
        return 0;
    }

    private void PrintHelp()
    {
        Console.WriteLine(text.HelpText);
    }

    private void PrintNewProjectNextSteps(string projectRoot, NewCommandOptions options)
    {
        Console.WriteLine(text.NewProjectReadyHeader);
        Console.WriteLine($"  1) cd \"{projectRoot}\"");
        Console.WriteLine(text.CheckProjectStep);
        Console.WriteLine(text.StartServerStep);
        Console.WriteLine(text.OpenClientStep(options.ClientEngine));
    }

    private static RpcStarterNewOptions ToRpcStarterOptions(
        string projectName,
        string outputDirectory,
        NewCommandOptions options)
    {
        return new RpcStarterNewOptions(
            projectName,
            outputDirectory,
            ToolOptionValues.ParseClientEngine(options.ClientEngine),
            ToolOptionValues.ParseTransport(options.Transport),
            ToolOptionValues.ParseSerializer(options.Serializer),
            ToolOptionValues.ParseNuGetForUnitySource(options.NuGetForUnitySource));
    }
}

internal sealed class ToolConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public Task SaveAsync(string configPath, ToolConfig config)
    {
        var json = JsonSerializer.Serialize(config, JsonOptions);
        return File.WriteAllTextAsync(configPath, json);
    }

    public async Task<ToolConfig> LoadAsync(string configPath)
    {
        await using var stream = File.OpenRead(configPath);
        var config = await JsonSerializer.DeserializeAsync<ToolConfig>(stream, JsonOptions).ConfigureAwait(false);
        return config ?? throw new InvalidOperationException($"Failed to parse tool config: {configPath}");
    }
}
