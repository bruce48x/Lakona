using System.Text.Json;
using Lakona.Tool.RpcStarter;

internal sealed class CliApplication(
    RpcStarterGenerator rpcStarterGenerator,
    ProjectScaffolder projectScaffolder,
    ToolConfigStore configStore,
    ToolText? text = null)
{
    private readonly ToolText text = text ?? ToolText.Current;

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
            Console.Error.WriteLine($"{text.ErrorPrefix}: {ex.Message}");
            Console.Error.WriteLine(text.RunHelpForUsage);
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
        Console.Error.WriteLine(text.UnknownCommand(command));
        Console.Error.WriteLine();
        PrintHelp();
        return 1;
    }

    private async Task<int> NewAsync(string[] args)
    {
        var options = CliParser.ParseNewOptions(args, text);
        var outputDirectory = Path.GetFullPath(options.OutputPath ?? Directory.GetCurrentDirectory());
        Directory.CreateDirectory(outputDirectory);

        var projectName = string.IsNullOrWhiteSpace(options.Name) ? ProjectConventions.DefaultProjectName : options.Name;
        var projectRoot = Path.Combine(outputDirectory, projectName);

        rpcStarterGenerator.Generate(ToRpcStarterOptions(projectName, outputDirectory, options));

        if (!Directory.Exists(projectRoot))
        {
            Console.Error.WriteLine(text.GeneratedProjectRootNotFound(projectRoot));
            return 1;
        }

        await projectScaffolder.AugmentProjectWithLakonaGameAsync(projectRoot, options).ConfigureAwait(false);

        var configPath = Path.Combine(projectRoot, ProjectConventions.ConfigFileName);
        if (File.Exists(configPath))
        {
            Console.Error.WriteLine(text.ConfigAlreadyExists(configPath));
            return 1;
        }

        await configStore.SaveAsync(configPath, ToolConfig.CreateDefault(projectName, options)).ConfigureAwait(false);
        Console.WriteLine(text.CreatedToolConfig(configPath));
        PrintNewProjectNextSteps(projectRoot);
        return 0;
    }

    private void PrintHelp()
    {
        Console.WriteLine(text.HelpText);
    }

    private void PrintNewProjectNextSteps(string projectRoot)
    {
        Console.WriteLine(text.NewProjectReadyHeader);
        Console.WriteLine($"  1) cd \"{projectRoot}\"");
        Console.WriteLine(text.CheckProjectStep);
        Console.WriteLine(text.StartServerStep);
    }

    private static RpcStarterNewOptions ToRpcStarterOptions(
        string projectName,
        string outputDirectory,
        NewCommandOptions options)
    {
        return new RpcStarterNewOptions(
            projectName,
            outputDirectory,
            ParseClientEngine(options.ClientEngine),
            ParseTransport(options.Transport),
            ParseSerializer(options.Serializer),
            ParseNuGetForUnitySource(options.NuGetForUnitySource));
    }

    private static ClientEngineKind ParseClientEngine(string value) => value switch
    {
        "unity" => ClientEngineKind.Unity,
        "unity-cn" => ClientEngineKind.UnityCn,
        "tuanjie" => ClientEngineKind.Tuanjie,
        "godot" => ClientEngineKind.Godot,
        _ => throw new InvalidOperationException($"Unsupported --client-engine value after validation: {value}")
    };

    private static TransportKind ParseTransport(string value) => value switch
    {
        "tcp" => TransportKind.Tcp,
        "websocket" => TransportKind.WebSocket,
        "kcp" => TransportKind.Kcp,
        _ => throw new InvalidOperationException($"Unsupported --transport value after validation: {value}")
    };

    private static SerializerKind ParseSerializer(string value) => value switch
    {
        "json" => SerializerKind.Json,
        "memorypack" => SerializerKind.MemoryPack,
        _ => throw new InvalidOperationException($"Unsupported --serializer value after validation: {value}")
    };

    private static NuGetForUnitySourceKind ParseNuGetForUnitySource(string value) => value switch
    {
        "embedded" => NuGetForUnitySourceKind.Embedded,
        "openupm" => NuGetForUnitySourceKind.OpenUpm,
        _ => throw new InvalidOperationException($"Unsupported --nugetforunity-source value after validation: {value}")
    };
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
