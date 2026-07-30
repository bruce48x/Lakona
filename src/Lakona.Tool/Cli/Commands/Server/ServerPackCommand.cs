using Lakona.ProjectSystem;

namespace Lakona.Tool.Cli.Commands.Server;

internal sealed class ServerPackCommand
{
    private const string DefaultProjectPath = "Server/App/Server.App.csproj";
    private const string DefaultHotfixProjectPath = "Server/Hotfix/Server.Hotfix.csproj";
    private const string DefaultOutputDirectory = "Server/Build";

    private readonly ICliTerminal terminal;
    private readonly ILakonaProjectPackager packager;

    public ServerPackCommand(ICliTerminal terminal, ILakonaProjectPackager? packager = null)
    {
        this.terminal = terminal;
        this.packager = packager ?? new LakonaProjectPackager();
    }

    public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        var request = Parse(args);
        var result = await packager.PackAsync(
            request,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        terminal.WriteLine($"Packed server {result.ArtifactPath}.");
        return 0;
    }

    private static LakonaPackageRequest Parse(string[] args)
    {
        var project = DefaultProjectPath;
        var hotfixProject = DefaultHotfixProjectPath;
        var output = DefaultOutputDirectory;
        var configuration = "Release";
        string? runtime = null;
        var version = "v" + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss'Z'");

        for (var index = 0; index < args.Length; index++)
        {
            var option = args[index];
            if (!option.StartsWith("--", StringComparison.Ordinal))
            {
                throw new CliUsageException($"Unexpected argument '{option}'.");
            }

            switch (option)
            {
                case "--project":
                    project = ReadValue(args, ref index, option);
                    break;
                case "--hotfix-project":
                    hotfixProject = ReadValue(args, ref index, option);
                    break;
                case "--output":
                    output = ReadValue(args, ref index, option);
                    break;
                case "--configuration":
                    configuration = ReadValue(args, ref index, option);
                    break;
                case "--runtime":
                    runtime = ReadValue(args, ref index, option);
                    break;
                case "--version":
                    version = ReadValue(args, ref index, option);
                    break;
                default:
                    throw new CliUsageException($"Unknown server pack option '{option}'.");
            }
        }

        if (string.IsNullOrWhiteSpace(runtime))
        {
            throw new CliUsageException("Missing required --runtime option.");
        }

        if (string.IsNullOrWhiteSpace(configuration))
        {
            throw new CliUsageException("--configuration cannot be empty.");
        }

        return new LakonaPackageRequest(
            Directory.GetCurrentDirectory(),
            LakonaPackageKind.Server,
            runtime,
            configuration,
            output,
            version,
            project,
            hotfixProject);
    }

    private static string ReadValue(string[] args, ref int index, string option)
    {
        if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            throw new CliUsageException($"Missing value for {option}.");
        }

        index++;
        return args[index];
    }
}
