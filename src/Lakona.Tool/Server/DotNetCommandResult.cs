namespace Lakona.Tool.Server;

internal sealed record DotNetCommandResult(int ExitCode, string StandardOutput, string StandardError);
