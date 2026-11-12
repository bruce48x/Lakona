using Lakona.Tool.Hotfix;

namespace Lakona.Tool.Cli.Commands.Hotfix;

internal sealed class HotfixActivateCommand
{
    private readonly ICliTerminal terminal;

    public HotfixActivateCommand(ICliTerminal terminal)
    {
        this.terminal = terminal;
    }

    public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]) || args[0].StartsWith("--", StringComparison.Ordinal))
        {
            throw new CliUsageException("Missing hotfix version.");
        }

        string? server = null;
        string? expectedCurrentVersion = null;
        for (var index = 1; index < args.Length; index++)
        {
            var option = args[index];
            switch (option)
            {
                case "--server":
                    if (server is not null)
                    {
                        throw new CliUsageException($"Duplicate hotfix activate option '{option}'.");
                    }

                    server = ReadValue(args, ref index, option);
                    break;
                case "--expected-current-version":
                    if (expectedCurrentVersion is not null)
                    {
                        throw new CliUsageException($"Duplicate hotfix activate option '{option}'.");
                    }

                    expectedCurrentVersion = ReadValue(args, ref index, option);
                    break;
                default:
                    throw new CliUsageException($"Unexpected hotfix activate argument '{option}'.");
            }
        }

        var json = await new HotfixAdminClient().PostAsync(
            server ?? "http://127.0.0.1:20080",
            "/_lakona/hotfix/activate",
            new { version = args[0], expectedCurrentVersion, operationId = args[0] },
            cancellationToken).ConfigureAwait(false);
        terminal.WriteLine(json);
        return 0;
    }

    private static string ReadValue(string[] args, ref int index, string option)
    {
        if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]) ||
            args[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            throw new CliUsageException($"Missing value for {option}.");
        }

        return args[++index];
    }
}
